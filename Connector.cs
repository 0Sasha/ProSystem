using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using ProSystem.Algorithms;
using static ProSystem.MainWindow;

namespace ProSystem;

internal static class TXmlConnector
{
    #region Fields
    private delegate bool CallBackDelegate(IntPtr Data);
    private static readonly CallBackDelegate CallbackDel = new(CallBack);

    private static bool Scheduled;
    private static int WaitingTime = 18000;
    private static DateTime TriggerDataProcessing = DateTime.Now;

    private static readonly XmlReaderSettings XS = new()
    {
        IgnoreWhitespace = true,
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Parse
    };
    private static readonly string DTForm = "dd.MM.yyyy HH:mm:ss";

    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> DataQueue = new();
    private static readonly System.Threading.Thread DataProcessor = 
        new(ProcessDataQueue) { IsBackground = true, Name = "DataProcessor" };
    #endregion

    #region Methods for processing data
    private static bool CallBack(IntPtr Data)
    {
        DataQueue.Enqueue(Marshal.PtrToStringUTF8(Data));
        FreeMemory(Data);
        return true;
    }
    private static void ProcessDataQueue()
    {
        while (true)
        {
            string Data = null;
            try
            {
                while (!DataQueue.IsEmpty)
                {
                    if (DataQueue.TryDequeue(out Data)) ProcessData(Data);
                    else AddInfo("ProcessDataQueue: не удалось взять объект из очереди.");
                }
            }
            catch (Exception e)
            {
                AddInfo("ProcessDataQueue: Исключение: " + e.Message, notify: true);
                AddInfo("Трассировка стека: " + e.StackTrace);
                AddInfo("Данные: " + Data, false);
                if (e.InnerException != null)
                {
                    AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                    AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
                }
            }
            System.Threading.Thread.Sleep(5);
        }
    }
    private static string ProcessData(string Data)
    {
        string Section = null;
        using XmlReader XR = XmlReader.Create(new StringReader(Data), XS);

        while (XR.Read())
        {
            Section = XR.Name;

            // Высокочастотные секции
            if (Section == "alltrades")
            {
                Tool MyTool;
                Trade MyTrade;
                while (XR.Read())
                {
                    if (!XR.ReadToFollowing("time")) { XR.Close(); return Section; }
                    XR.Read(); MyTrade = new Trade(DateTime.ParseExact(XR.Value, DTForm, IC));

                    if (!XR.ReadToFollowing("price")) { AddInfo("alltrades: Нет price сделки."); continue; }
                    XR.Read(); MyTrade.Price = double.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("quantity")) { AddInfo("alltrades: Нет quantity сделки."); continue; }
                    XR.Read(); MyTrade.Quantity = int.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("seccode")) { AddInfo("alltrades: Нет seccode сделки."); continue; }
                    XR.Read(); MyTool = Tools.SingleOrDefault(x => x.MySecurity.Seccode == XR.Value);

                    if (MyTool != null) MyTool.MySecurity.LastTrade = MyTrade;
                    else Tools.Single(x => x.BasicSecurity != null && x.BasicSecurity.Seccode == XR.Value).BasicSecurity.LastTrade = MyTrade;
                }
            }
            else if (Section == "candles")
            {
                if (XR.GetAttribute("status") == "3")
                {
                    AddInfo("candles: Запрошенные данные недоступны. Запросите позже.");
                    XR.Close(); return Section;
                }

                Security MySecurity;
                int i = Array.FindIndex(Tools.ToArray(), x => x.MySecurity.Seccode == XR.GetAttribute("seccode"));
                if (i > -1) MySecurity = Tools[i].MySecurity;
                else
                {
                    i = Array.FindIndex(Tools.ToArray(), x => x.BasicSecurity != null && x.BasicSecurity.Seccode == XR.GetAttribute("seccode"));
                    if (i > -1) MySecurity = Tools[i].BasicSecurity;
                    else
                    {
                        string Seccode = XR.GetAttribute("seccode"); XR.Read();
                        if (Seccode.Contains("USD")) USDRUB = double.Parse(XR.GetAttribute("close"), IC);
                        else if (Seccode.Contains("EUR")) EURRUB = double.Parse(XR.GetAttribute("close"), IC);
                        else AddInfo("candles: Неактуальный инструмент: " + Seccode);
                        XR.Close(); return Section;
                    }
                }

                ProcessBars(XR, Tools[i], MySecurity);
                XR.Close();
                return Section;
            }
            // Базовые секции
            else if (Section == "orders")
            {
                ProcessOrders(XR, Orders);
                XR.Close();
                return Section;
            }
            else if (Section == "positions")
            {
                XR.Read();
                ProcessPositions(XR, XR.Name, Portfolio);
                XR.Close();
                return Section;
            }
            else if (Section == "trades")
            {
                ProcessTrades(XR, Trades);
                XR.Close();
                return Section;
            }
            else if (Section == "server_status")
            {
                if (XR.GetAttribute("connected") == "true")
                {
                    ServerAvailable = true;
                    if (XR.GetAttribute("recover") != "true")
                    {
                        Connection = ConnectionState.Connected;
                        AddInfo("Connected", !Scheduled);
                        Scheduled = false;
                    }
                    else
                    {
                        TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
                        Connection = ConnectionState.Connecting;
                        AddInfo("Recover connection");
                    }
                }
                else if (XR.GetAttribute("connected") == "false")
                {
                    SystemReadyToTrading = false;
                    ServerAvailable = true;

                    if (XR.GetAttribute("recover") != "true")
                    {
                        Connection = ConnectionState.Disconnected;
                        AddInfo("Disconnected", !Scheduled);
                        Scheduled = false;
                    }
                    else
                    {
                        Connection = ConnectionState.Connecting;
                        AddInfo("Recover");
                    }
                }
                else if (XR.GetAttribute("connected") == "error")
                {
                    SystemReadyToTrading = false;
                    ServerAvailable = false;
                    BackupServer = !BackupServer;

                    Connection = ConnectionState.Disconnected;
                    XR.Read(); AddInfo("Server error: " + XR.Value + " BackupServer: " + !BackupServer, notify: true);
                }
            }
            else if (Section == "mc_portfolio")
            {
                ProcessPortfolio(XR, Portfolio);
                XR.Close();
                return Section;
            }
            // Второстепенные секции
            else if (Section == "sec_info_upd")
            {
                if (!XR.ReadToFollowing("seccode"))
                {
                    AddInfo("sec_info_upd: Нет seccode.");
                    XR.Close();
                    return Section;
                }
                XR.Read();

                Tool MyTool;
                if (Tools.SingleOrDefault(x => x.MySecurity.Seccode == XR.Value) == null) { XR.Close(); return Section; }
                else MyTool = Tools.Single(x => x.MySecurity.Seccode == XR.Value);

                string Name = "";
                while (XR.Read())
                {
                    if (XR.Name.Length > 0) Name = XR.Name;
                    else if (XR.HasValue)
                    {
                        if (Name == "buy_deposit") MyTool.MySecurity.BuyDeposit = double.Parse(XR.Value, IC);
                        else if (Name == "sell_deposit") MyTool.MySecurity.SellDeposit = double.Parse(XR.Value, IC);
                        else if (Name == "minprice") MyTool.MySecurity.MinPrice = double.Parse(XR.Value, IC);
                        else if (Name == "maxprice") MyTool.MySecurity.MaxPrice = double.Parse(XR.Value, IC);
                        else if (Name == "point_cost") MyTool.MySecurity.PointCost = double.Parse(XR.Value, IC);
                    }
                }
                XR.Close();
                return Section;
            }
            else if (Section == "sec_info")
            {
                if (!XR.ReadToFollowing("seccode"))
                {
                    AddInfo("sec_info: Нет seccode.");
                    XR.Close();
                    return Section;
                }
                XR.Read();

                Tool MyTool;
                if (Tools.SingleOrDefault(x => x.MySecurity.Seccode == XR.Value) == null) { XR.Close(); return Section; }
                else MyTool = Tools.Single(x => x.MySecurity.Seccode == XR.Value);

                if (!XR.ReadToFollowing("minprice"))
                {
                    AddInfo("sec_info: Нет minprice.");
                    XR.Close();
                    return Section;
                }
                XR.Read(); MyTool.MySecurity.MinPrice = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("maxprice"))
                {
                    AddInfo("sec_info: Нет maxprice.");
                    XR.Close();
                    return Section;
                }
                XR.Read(); MyTool.MySecurity.MaxPrice = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("buy_deposit"))
                {
                    AddInfo("sec_info: Нет buy_deposit.");
                    XR.Close();
                    return Section;
                }
                XR.Read(); MyTool.MySecurity.BuyDeposit = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("sell_deposit"))
                {
                    AddInfo("sec_info: Нет sell_deposit.");
                    XR.Close();
                    return Section;
                }
                XR.Read(); MyTool.MySecurity.SellDeposit = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("point_cost"))
                {
                    AddInfo("sec_info: Нет point_cost.");
                    XR.Close();
                    return Section;
                }
                XR.Read(); MyTool.MySecurity.PointCost = double.Parse(XR.Value, IC);
                XR.Close(); return Section;
            }
            else if (Section == "cln_sec_permissions")
            {
                if (!XR.ReadToFollowing("seccode")) { AddInfo("sec_permissions: no seccode"); XR.Close(); return Section; }
                XR.Read(); string Seccode = XR.Value;

                if (Tools.SingleOrDefault(x => x.MySecurity.Seccode == Seccode) == null)
                { AddInfo("sec_permissions: неактуальный инструмент: " + Seccode); XR.Close(); return Section; }
                Tool MyTaskTool = Tools.Single(x => x.MySecurity.Seccode == Seccode);

                if (!XR.ReadToFollowing("riskrate_long")) { AddInfo("sec_permissions: no riskrate_long"); XR.Close(); return Section; }
                XR.Read(); MyTaskTool.MySecurity.RiskrateLong = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("reserate_long")) { AddInfo("sec_permissions: no reserate_long"); XR.Close(); return Section; }
                XR.Read(); MyTaskTool.MySecurity.ReserateLong = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("riskrate_short")) { AddInfo("sec_permissions: no riskrate_short"); XR.Close(); return Section; }
                XR.Read(); MyTaskTool.MySecurity.RiskrateShort = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("reserate_short")) { AddInfo("sec_permissions: no reserate_short"); XR.Close(); return Section; }
                XR.Read(); MyTaskTool.MySecurity.ReserateShort = double.Parse(XR.Value, IC);

                /*if (!XR.ReadToFollowing("riskrate_longx")) { AddInfo("sec_permissions: no riskrate_longx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinRiskrateLong = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("reserate_longx")) { AddInfo("sec_permissions: no reserate_longx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinReserateLong = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("riskrate_shortx")) { AddInfo("sec_permissions: no riskrate_shortx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinRiskrateShort = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("reserate_shortx")) { AddInfo("sec_permissions: no reserate_shortx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinReserateShort = double.Parse(XR.Value, IC);*/

                XR.Close(); return Section;
            }
            else if (Section == "messages") // Текстовые сообщения
            {
                if (XR.ReadToFollowing("text")) { XR.Read(); AddInfo(XR.Value, MySettings.DisplayMessages); }
                break;
            }
            else if (Section == "error") // Внутренние ошибки dll
            {
                XR.Read(); AddInfo(XR.Value);
                break;
            }
            else if (Section == "securities")
            {
                while (XR.Read())
                {
                    if (XR.Name != "security" && !XR.ReadToFollowing("security")) { XR.Close(); return Section; }
                    if (XR.GetAttribute("active") == "false") continue;

                    if (!XR.ReadToFollowing("seccode")) { AddInfo("Не найден seccode."); continue; }
                    XR.Read(); AllSecurities.Add(new Security(XR.Value));

                    while (XR.Read())
                    {
                        if (XR.NodeType == XmlNodeType.EndElement)
                        {
                            if (XR.Name == "security") break;
                            continue;
                        }
                        if (XR.NodeType == XmlNodeType.Element)
                        {
                            if (XR.Name == "currency")
                            {
                                XR.Read();
                                AllSecurities[^1].Currency = XR.Value;
                            }
                            else if (XR.Name == "board")
                            {
                                XR.Read();
                                AllSecurities[^1].Board = XR.Value;
                            }
                            else if (XR.Name == "shortname")
                            {
                                XR.Read();
                                AllSecurities[^1].ShortName = XR.Value;
                            }
                            else if (XR.Name == "decimals")
                            {
                                XR.Read();
                                AllSecurities[^1].Decimals = int.Parse(XR.Value, IC);
                            }
                            else if (XR.Name == "market")
                            {
                                XR.Read();
                                AllSecurities[^1].Market = XR.Value;
                            }
                            else if (XR.Name == "minstep")
                            {
                                XR.Read();
                                AllSecurities[^1].MinStep = double.Parse(XR.Value, IC);
                            }
                            else if (XR.Name == "lotsize")
                            {
                                XR.Read();
                                AllSecurities[^1].LotSize = int.Parse(XR.Value, IC);
                            }
                            else if (XR.Name == "point_cost")
                            {
                                XR.Read();
                                AllSecurities[^1].PointCost = double.Parse(XR.Value, IC);
                            }
                        }

                        /*if (!XR.ReadToFollowing("currency")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден currency.", false); continue; }
                        XR.Read(); AllSecurities[^1].Currency = XR.Value;

                        if (!XR.ReadToFollowing("board")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден board."); continue; }
                        XR.Read(); AllSecurities[^1].Board = XR.Value;

                        if (!XR.ReadToFollowing("shortname")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден shortname."); continue; }
                        XR.Read(); AllSecurities[^1].ShortName = XR.Value;

                        if (!XR.ReadToFollowing("decimals")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден decimals."); continue; }
                        XR.Read(); AllSecurities[^1].Decimals = int.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("market")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден market."); continue; }
                        XR.Read(); AllSecurities[^1].Market = XR.Value;

                        if (!XR.ReadToFollowing("minstep")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден minstep."); continue; }
                        XR.Read(); AllSecurities[^1].MinStep = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("lotsize")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден lotsize."); continue; }
                        XR.Read(); AllSecurities[^1].LotSize = int.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("point_cost")) { AddInfo(AllSecurities[^1].Seccode + ": Не найден point_cost."); continue; }
                        XR.Read(); AllSecurities[^1].PointCost = double.Parse(XR.Value, IC);*/
                    }
                }
            }
            else if (Section == "client")
            {
                if (XR.GetAttribute("remove") == "false")
                {
                    if (Clients.SingleOrDefault(x => x.ID == XR.GetAttribute("id")) == null)
                        Clients.Add(new ClientAccount(XR.GetAttribute("id")));
                    else { AddInfo("Клиент уже есть в коллекции."); XR.Close(); return Section; }
                }
                else { Clients.Remove(Clients.Single(x => x.ID == XR.GetAttribute("id"))); XR.Close(); return Section; }

                if (!XR.ReadToFollowing("market")) { AddInfo("client: no market"); XR.Close(); return Section; };
                XR.Read(); Clients[^1].Market = XR.Value;

                if (!XR.ReadToFollowing("union")) { AddInfo("client: no union"); XR.Close(); return Section; };
                XR.Read(); Clients[^1].Union = XR.Value;

                XR.Close(); return Section;
            }
            else if (Section == "markets")
            {
                string ID = null;
                while (XR.Read())
                {
                    if (XR.HasAttributes) ID = XR.GetAttribute("id");
                    else if (XR.HasValue) Markets.Add(new Market(ID, XR.Value));
                }
                XR.Close(); return Section;
            }
            else if (Section == "candlekinds")
            {
                string ID = null;
                while (XR.Read())
                {
                    if (!XR.ReadToFollowing("id")) { XR.Close(); return Section; }
                    XR.Read(); ID = XR.Value;

                    if (!XR.ReadToFollowing("period")) { AddInfo("candlekinds: no period"); XR.Close(); return Section; }
                    XR.Read(); int Period = int.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("name")) { AddInfo("candlekinds: no name"); XR.Close(); return Section; }
                    XR.Read(); TimeFrames.Add(new TimeFrame(ID, Period, XR.Value));
                }
            }
            else if (Section is "marketord" or "pits" or "boards" or "union" or "overnight" or "news_header") return Section;
            else { AddInfo("ProcessData: Неизвестная секция: " + Section); return Section; }
            //else if (Section == "clientlimits" || Section == "quotes" || Section == "quotations") return Section;
        }
        XR.Close();
        return Section;
    }
    private static void ProcessBars(XmlReader xr, Tool tool, Security security)
    {
        int tf = TimeFrames.Single(x => x.ID == xr.GetAttribute("period")).Period / 60;
        if (security.SourceBars == null || security.SourceBars.TF != tf) security.SourceBars = new Bars(tf);

        List<DateTime> dateTime = new();
        List<double> open = new();
        List<double> high = new();
        List<double> low = new();
        List<double> close = new();
        List<double> volume = new();

        bool Filter = security.Market != "4";
        while (xr.Read())
        {
            if (Filter && xr.HasAttributes && (dateTime.Count == 0 ||
                dateTime[^1].Date != DateTime.ParseExact(xr.GetAttribute("date"), DTForm, IC).Date) &&
                double.Parse(xr.GetAttribute("high"), IC) - double.Parse(xr.GetAttribute("low"), IC) < 0.00001) xr.Read();

            if (xr.HasAttributes)
            {
                dateTime.Add(DateTime.ParseExact(xr.GetAttribute("date"), DTForm, IC));
                open.Add(double.Parse(xr.GetAttribute("open"), IC));
                high.Add(double.Parse(xr.GetAttribute("high"), IC));
                low.Add(double.Parse(xr.GetAttribute("low"), IC));
                close.Add(double.Parse(xr.GetAttribute("close"), IC));
                volume.Add(double.Parse(xr.GetAttribute("volume"), IC));
            }
            else if (xr.NodeType == XmlNodeType.EndElement)
            {
                if (security.SourceBars.DateTime == null) // Исходные данные отсутсвуют
                {
                    security.SourceBars = new Bars(tf)
                    {
                        DateTime = dateTime.ToArray(),
                        Open = open.ToArray(),
                        High = high.ToArray(),
                        Low = low.ToArray(),
                        Close = close.ToArray(),
                        Volume = volume.ToArray()
                    };
                }
                else if (dateTime.Count < 2) return;
                else if (dateTime[^1] >= security.SourceBars.DateTime[^1]) // Полученные данные свежее исходных
                {
                    // Поиск первого общего бара
                    int y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[0]);
                    if (y == -1) y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[1]);

                    if (y > -1) // Есть общие бары
                    {
                        security.SourceBars.DateTime = security.SourceBars.DateTime[..y].Concat(dateTime).ToArray();
                        security.SourceBars.Open = security.SourceBars.Open[..y].Concat(open).ToArray();
                        security.SourceBars.High = security.SourceBars.High[..y].Concat(high).ToArray();
                        security.SourceBars.Low = security.SourceBars.Low[..y].Concat(low).ToArray();
                        security.SourceBars.Close = security.SourceBars.Close[..y].Concat(close).ToArray();
                        security.SourceBars.Volume = security.SourceBars.Volume[..y].Concat(volume).ToArray();
                    }
                    else security.SourceBars = new Bars(tf) // Отсутствует общий бар
                    {
                        DateTime = dateTime.ToArray(),
                        Open = open.ToArray(),
                        High = high.ToArray(),
                        Low = low.ToArray(),
                        Close = close.ToArray(),
                        Volume = volume.ToArray()
                    };
                }
                else if (dateTime[^1] < security.SourceBars.DateTime[0]) // Полученные данные глубже исходных
                {
                    if (dateTime[^1].AddDays(5) < security.SourceBars.DateTime[0])
                    {
                        AddInfo("ProcessBars: Полученные данные слишком старые: " + security.ShortName + " LastBar: " + dateTime[^1]);
                        return;
                    }
                    security.SourceBars.DateTime = dateTime.Concat(security.SourceBars.DateTime).ToArray();
                    security.SourceBars.Open = open.Concat(security.SourceBars.Open).ToArray();
                    security.SourceBars.High = high.Concat(security.SourceBars.High).ToArray();
                    security.SourceBars.Low = low.Concat(security.SourceBars.Low).ToArray();
                    security.SourceBars.Close = close.Concat(security.SourceBars.Close).ToArray();
                    security.SourceBars.Volume = volume.Concat(security.SourceBars.Volume).ToArray();
                }
                else // Полученные данные располагаются внутри массива исходных данных
                {
                    // Поиск общих баров
                    int x = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[0]);
                    int y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[^1]);

                    if (x > -1 && y > -1) // Найдены общие бары
                    {
                        var sourceInnerArr = security.SourceBars.DateTime[x..(y + 1)];
                        if (dateTime.Count != sourceInnerArr.Length)
                        {
                            AddInfo("ProcessBars: Массив полученных баров не соответствуют массиву исходных по количеству: " +
                                security.ShortName + " Исх/получ: " + sourceInnerArr.Length + "/" + dateTime.Count + " Период: " +
                                dateTime[0].Date + "-" + dateTime[^1].Date + " Возможно, требуется перезагрузка баров.", false);
                            return;
                        }
                        return; // Только анализ баров

                        /*security.SourceBars.DateTime =
                            security.SourceBars.DateTime[..x].Concat(dateTime.Concat(security.SourceBars.DateTime[(y + 1)..])).ToArray();

                        security.SourceBars.Open =
                            security.SourceBars.Open[..x].Concat(open.Concat(security.SourceBars.Open[(y + 1)..])).ToArray();

                        security.SourceBars.High =
                            security.SourceBars.High[..x].Concat(high.Concat(security.SourceBars.High[(y + 1)..])).ToArray();

                        security.SourceBars.Low =
                            security.SourceBars.Low[..x].Concat(low.Concat(security.SourceBars.Low[(y + 1)..])).ToArray();

                        security.SourceBars.Close =
                            security.SourceBars.Close[..x].Concat(close.Concat(security.SourceBars.Close[(y + 1)..])).ToArray();

                        security.SourceBars.Volume =
                            security.SourceBars.Volume[..x].Concat(volume.Concat(security.SourceBars.Volume[(y + 1)..])).ToArray();*/
                    }
                    else
                    {
                        AddInfo("ProcessBars: Не найдены общие бары полученных и исходных баров: " + security.ShortName, false);
                        return;
                    }
                }

                Task.Run(() => tool.UpdateBars(security == tool.BasicSecurity));
                return;
            }
        }
        AddInfo("ProcessBars: Не найден EndElement");
    }
    private static void ProcessOrders(XmlReader xr, ObservableCollection<Order> orders)
    {
        while (xr.Read())
        {
            if (!xr.HasAttributes && !xr.ReadToFollowing("order")) return;

            // Первичная идентификация
            int trID = int.Parse(xr.GetAttribute("transactionid"), IC);
            Order[] arrOrders = orders.ToArray();
            if (arrOrders.Where(x => x.TrID == trID).Count() > 1)
            {
                AddInfo("orders: Найдено несколько заявок с одинаковым TrID. Удаление лишних.", notify: true);
                Window.Dispatcher.Invoke(() =>
                {
                    while (arrOrders.Where(x => x.TrID == trID).Count() > 1)
                    {
                        orders.Remove(arrOrders.First(x => x.TrID == trID));
                        arrOrders = orders.ToArray();
                    }
                });
            }

            Order myOrder = arrOrders.SingleOrDefault(x => x.TrID == trID); // Проверка наличия заявки с данным TrID
            if (myOrder == null) myOrder = new Order(trID); // Создание новой заявки с данным TrID

            // Вторичная идентификация
            if (!xr.ReadToFollowing("orderno"))
            {
                AddInfo("orders: Не найден orderno заявки: " + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();

            if (xr.Value == "0") myOrder.OrderNo = 0;
            else if (arrOrders.SingleOrDefault(x => x.OrderNo == long.Parse(xr.Value, IC)) == null) myOrder.OrderNo = long.Parse(xr.Value, IC);
            else // Заявка с данным биржевым номером уже есть в коллекции, обновление TrID и её фиксация
            {
                arrOrders.Single(x => x.OrderNo == long.Parse(xr.Value, IC)).TrID = trID;
                myOrder = arrOrders.Single(x => x.TrID == trID); // Фиксация существующей заявки
            }

            if (!xr.ReadToFollowing("seccode"))
            {
                AddInfo("orders: Не найден seccode заявки: " + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            myOrder.Seccode = xr.Value;

            if (!xr.ReadToFollowing("status"))
            {
                AddInfo("orders: Не найден status заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            myOrder.Status = xr.Value;

            if (!xr.ReadToFollowing("buysell"))
            {
                AddInfo("orders: Не найден buysell заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            myOrder.BuySell = xr.Value;

            xr.Read();
            xr.Read();
            if (xr.Name == "time")
            {
                xr.Read();
                myOrder.Time = DateTime.ParseExact(xr.Value, DTForm, IC);
                xr.Read(); xr.Read();
                if (xr.Name == "accepttime")
                {
                    xr.Read();
                    myOrder.AcceptTime = DateTime.ParseExact(xr.Value, DTForm, IC);
                }
            }
            else if (xr.Name == "accepttime")
            {
                xr.Read();
                myOrder.AcceptTime = DateTime.ParseExact(xr.Value, DTForm, IC);
            }
            else if (myOrder.Status == "active" && myOrder.OrderNo == 0) { }

            if (!xr.ReadToFollowing("balance"))
            {
                AddInfo("orders: Не найден balance заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            myOrder.Balance = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("price"))
            {
                AddInfo("orders: Не найдена price заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            myOrder.Price = double.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("quantity"))
            {
                AddInfo("orders: Не найдено quantity заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            myOrder.Quantity = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("withdrawtime"))
            {
                AddInfo("orders: Не найдено withdrawtime заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            if (xr.Value != "0") myOrder.WithdrawTime = DateTime.ParseExact(xr.Value, DTForm, IC);

            if (!xr.ReadToFollowing("condition"))
            {
                AddInfo("orders: Не найдено condition заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            myOrder.Condition = xr.Value;

            if (myOrder.Condition != "None")
            {
                if (!xr.ReadToFollowing("conditionvalue"))
                {
                    AddInfo("orders: Не найдено conditionvalue заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                    continue;
                }
                xr.Read();
                myOrder.ConditionValue = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("validafter"))
                {
                    AddInfo("orders: Не найдено validafter заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                    continue;
                }
                xr.Read();
                if (xr.Value != "0") myOrder.ValidAfter = DateTime.ParseExact(xr.Value, DTForm, IC);

                if (!xr.ReadToFollowing("validbefore"))
                {
                    AddInfo("orders: Не найдено validbefore заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                    continue;
                }
                xr.Read();
                if (xr.Value != "" && xr.Value != "0") myOrder.ValidBefore = DateTime.ParseExact(xr.Value, DTForm, IC);
            }
            if (!xr.ReadToFollowing("result"))
            {
                AddInfo("orders: Не найден result заявки: " + myOrder.Seccode + "/" + myOrder.TrID, notify: true);
                continue;
            }
            xr.Read();
            if (xr.HasValue)
            {
                if (xr.Value.StartsWith("{37}", SC) || xr.Value.StartsWith("{42}", SC))
                    AddInfo(myOrder.Seccode + "/" + myOrder.TrID + ": OrderReply: " + xr.Value, false);
                else AddInfo(myOrder.Seccode + "/" + myOrder.TrID + ": OrderReply: " + xr.Value);
            }

            int i = Array.FindIndex(orders.ToArray(), x => x.TrID == myOrder.TrID);
            Window.Dispatcher.Invoke(() =>
            {
                if (i > -1) orders[i] = myOrder;
                else orders.Add(myOrder);
            });
        }
    }
    private static void ProcessTrades(XmlReader xr, ObservableCollection<Trade> trades)
    {
        Trade trade;
        while (xr.Read())
        {
            if (!xr.ReadToFollowing("tradeno")) return;
            xr.Read();

            if (trades.SingleOrDefault(x => x.TradeNo == long.Parse(xr.Value, IC)) != null) continue;
            else trade = new Trade(long.Parse(xr.Value, IC));

            if (!xr.ReadToFollowing("orderno"))
            {
                AddInfo("Нет orderno моей сделки.");
                continue;
            }
            xr.Read();
            trade.OrderNo = long.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("seccode"))
            {
                AddInfo("Нет seccode моей сделки.");
                continue;
            }
            xr.Read();
            trade.Seccode = xr.Value;

            if (!xr.ReadToFollowing("buysell"))
            {
                AddInfo("Нет buysell моей сделки.");
                continue;
            }
            xr.Read();
            trade.BuySell = xr.Value;

            if (!xr.ReadToFollowing("time"))
            {
                AddInfo("Нет time моей сделки.");
                continue;
            }
            xr.Read();
            trade.DateTime = DateTime.ParseExact(xr.Value, DTForm, IC);

            if (!xr.ReadToFollowing("price"))
            {
                AddInfo("Нет price моей сделки.");
                continue;
            }
            xr.Read();
            trade.Price = double.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("quantity"))
            {
                AddInfo("Нет quantity моей сделки.");
                continue;
            }
            xr.Read();
            trade.Quantity = int.Parse(xr.Value, IC);

            Window.Dispatcher.Invoke(() => trades.Add(trade));
            bool display = MySettings.DisplayNewTrades && trades.Count > 1 && (trades[^2].Seccode != trade.Seccode ||
                trades[^2].BuySell != trade.BuySell || trades[^2].DateTime < trade.DateTime.AddMinutes(-30));
            AddInfo("Новая сделка: " + trade.Seccode + "/" + trade.BuySell + "/" + trade.Price + "/" + trade.Quantity, display);
        }
    }
    private static void ProcessPositions(XmlReader xr, string subsection, UnitedPortfolio portfolio)
    {
        while (xr.Read())
        {
            if (subsection is "sec_position" or "forts_position")
            {
                if (!xr.ReadToFollowing("seccode")) return;
                xr.Read();

                Position pos = portfolio.Positions.SingleOrDefault(x => x.Seccode == xr.Value);
                if (pos == null)
                {
                    pos = new(xr.Value);
                    portfolio.Positions.Add(pos);
                }

                if (!xr.ReadToFollowing("market"))
                {
                    AddInfo("Не найден market позиции");
                    continue;
                }
                xr.Read();
                pos.Market = xr.Value;

                if (subsection == "forts_position")
                {
                    if (!xr.ReadToFollowing("startnet"))
                    {
                        AddInfo("Не найден startnet позиции");
                        continue;
                    }
                    xr.Read();
                    pos.SaldoIn = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("totalnet"))
                    {
                        AddInfo("Не найден totalnet позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Saldo = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("varmargin"))
                    {
                        AddInfo("Не найдена varmargin позиции");
                        continue;
                    }
                    xr.Read();
                    pos.PL = double.Parse(xr.Value, IC);
                }
                else
                {
                    if (!xr.ReadToFollowing("saldoin"))
                    {
                        AddInfo("Не найдено saldoin позиции");
                        continue;
                    }
                    xr.Read();
                    pos.SaldoIn = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("saldo"))
                    {
                        AddInfo("Не найдено saldo позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Saldo = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("amount"))
                    {
                        AddInfo("Не найдено amount позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Amount = double.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("equity"))
                    {
                        AddInfo("Не найдено equity позиции");
                        continue;
                    }
                    xr.Read();
                    pos.Equity = double.Parse(xr.Value, IC);
                }
            }
            else if (subsection == "united_limits")
            {
                if (!xr.ReadToFollowing("equity"))
                {
                    AddInfo("Нет equity портфеля.");
                    return;
                }
                xr.Read();
                portfolio.Saldo = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("requirements"))
                {
                    AddInfo("Нет requirements портфеля.");
                    return;
                }
                xr.Read();
                portfolio.InitReqs = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("free")) return;
                xr.Read();
                portfolio.Free = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("vm")) return;
                xr.Read();
                portfolio.VarMargin = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("finres")) return;
                xr.Read();
                portfolio.FinRes = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("go")) return;
                xr.Read();
                portfolio.GO = double.Parse(xr.Value, IC);
                return;
            }
            else if (subsection == "money_position")
            {
                if (!xr.ReadToFollowing("shortname")) return;
                xr.Read();

                Position pos = portfolio.MoneyPositions.SingleOrDefault(x => x.ShortName == xr.Value);
                if (pos == null)
                {
                    pos = new() { ShortName = xr.Value };
                    portfolio.MoneyPositions.Add(pos);
                }

                if (!xr.ReadToFollowing("saldoin"))
                {
                    AddInfo("Не найдено saldoin позиции");
                    continue;
                }
                xr.Read();
                pos.SaldoIn = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("saldo"))
                {
                    AddInfo("Не найдено saldo позиции");
                    continue;
                }
                xr.Read();
                pos.Saldo = double.Parse(xr.Value, IC);
            }
            else
            {
                AddInfo("Неизвестная позиция:" + subsection);
                return;
            }
        }
    }
    private static void ProcessPortfolio(XmlReader xr, UnitedPortfolio portfolio)
    {
        portfolio.Union = xr.GetAttribute("union");
        if (!xr.ReadToFollowing("open_equity"))
        {
            AddInfo("Нет open_equity портфеля.");
            return;
        }
        xr.Read();
        portfolio.SaldoIn = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("equity"))
        {
            AddInfo("Нет equity портфеля.");
            return;
        }
        xr.Read();
        portfolio.Saldo = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("pl"))
        {
            AddInfo("Нет pl портфеля.");
            return;
        }
        xr.Read();
        portfolio.PL = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("init_req"))
        {
            AddInfo("Нет init_req портфеля.");
            return;
        }
        xr.Read();
        portfolio.InitReqs = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("maint_req"))
        {
            AddInfo("Нет maint_req портфеля.");
            return;
        }
        xr.Read();
        portfolio.MinReqs = double.Parse(xr.Value, IC);

        if (!xr.ReadToFollowing("unrealized_pnl"))
        {
            AddInfo("Нет unrealized_pnl портфеля.");
            return;
        }
        xr.Read();
        portfolio.UnrealPL = double.Parse(xr.Value, IC);

        while (xr.Read())
        {
            if (!xr.ReadToFollowing("seccode")) return;
            xr.Read();

            Position pos = portfolio.Positions.SingleOrDefault(x => x.Seccode == xr.Value);
            if (pos == null)
            {
                pos = new Position(xr.Value);
                portfolio.Positions.Add(pos);
            }

            if (!xr.ReadToFollowing("open_balance"))
            {
                AddInfo("Не найден open_balance позиции");
                continue;
            }
            xr.Read();
            pos.SaldoIn = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("balance"))
            {
                AddInfo("Не найден balance позиции");
                continue;
            }
            xr.Read();
            pos.Saldo = int.Parse(xr.Value, IC);

            if (!xr.ReadToFollowing("pl"))
            {
                AddInfo("Нет pl позиции.");
                continue;
            }
            xr.Read();
            pos.PL = double.Parse(xr.Value, IC);
        }
    }
    #endregion

    #region Methods for sending commands
    public static void Connect(bool scheduled = false)
    {
        if (!ConnectorInitialized)
        {
            AddInfo("Коннектор не инициализирован.");
            return;
        }

        // Очистка данных
        AllSecurities.Clear(); Markets.Clear();
        TimeFrames.Clear(); Clients.Clear();
        Window.Dispatcher.Invoke(() => Orders.Clear());

        // Подготовка переменных
        WaitingTime = MySettings.RequestTM * 1000 + 3000;
        Connection = ConnectionState.Connecting;

        // Формирование команды
        string RqDelaq = "20"; // Задает частоту обращений коннектора к серверу Transaq в миллисекундах. Минимум 10.
        string UnLimits = MySettings.SessionTM.ToString(IC); // Таймаут информирования о текущих показателях единого портфеля минимум один раз в N секунд.
        string Equity = "0"; // Таймаут информирования о текущей стоимости позиций один раз в N секунд за исключением позиций FORTS.

        string ServerIP = !BackupServer ? "tr1.finam.ru" : "tr2.finam.ru", ServerPort = "3900";
        string StartPart = "<command id=\"connect\"><login>" + MySettings.LoginConnector + "</login><password>";
        string EndPart = "</password><host>" + ServerIP + "</host><port>" + ServerPort + "</port><rqdelay>" + RqDelaq + "</rqdelay><session_timeout>" +
            MySettings.SessionTM.ToString(IC) + "</session_timeout><request_timeout>" + MySettings.RequestTM.ToString(IC) +
            "</request_timeout><push_u_limits>" + UnLimits + "</push_u_limits><push_pos_equity>" + Equity + "</push_pos_equity></command>";

        // Отправка команды
        Scheduled = scheduled;
        string Result = ConnectorSendCommand(StartPart, EndPart, Window.Dispatcher.Invoke(() => Window.TxtPas.SecurePassword));
        GC.Collect();

        if (Result.StartsWith("<result success=\"true\"", SC) || Result.Contains("уже устанавливается")) return; // "уже установлено"
        else
        {
            Connection = ConnectionState.Disconnected;
            AddInfo(Result);
        }
    }
    public static void Disconnect(bool scheduled = false)
    {
        SystemReadyToTrading = false;
        Connection = ConnectionState.Disconnecting;

        Scheduled = scheduled;
        string Сommand = "<command id=\"disconnect\"/>";
        string Result = ConnectorSendCommand(Сommand);
        if (Result.StartsWith("<result success=\"true\"", SC)) Connection = ConnectionState.Disconnected;
        else
        {
            if (Result.StartsWith("<result success=\"false\"><message>Соединение не установлено", SC)) Connection = ConnectionState.Disconnected;
            AddInfo(Result);
        }
        System.Threading.Thread.Sleep(1000);
    }

    public static bool SendOrder(Security Symbol, OrderType OrderType, bool IsBuy, double Price, int Quantity, string Signal,
        Script Sender = null, string Note = null, bool UseCredit = false)
    {
        // Подготовка переменных
        string SenderName = Sender != null ? Sender.Name : "System";
        if (Price < 0.00001 || Quantity < 1)
        {
            AddInfo("SendOrder: Цена заявки или количество <= 0. Отправитель: " + SenderName);
            return false;
        }
        Price = Math.Round(Price, Symbol.Decimals);

        string BuySell = IsBuy ? "B" : "S";
        string Credit = UseCredit ? "<usecredit/>" : "";
        string ID, Market, Condition;

        if (OrderType == OrderType.Limit)
        {
            ID = "neworder";
            Market = "";
            Condition = "None";
        }
        else if (OrderType == OrderType.Conditional)
        {
            ID = "newcondorder";
            Market = "";
            Condition = IsBuy ? "BidOrLast" : "AskOrLast";
        }
        else
        {
            ID = "neworder";
            Market = "<bymarket/>";
            Condition = "None";
        }

        // Формирование команды
        string Сommand = "<command id=\"" + ID + "\"><security><board>" + Symbol.Board + "</board><seccode>" + Symbol.Seccode +
            "</seccode></security><union>" + Portfolio.Union + "</union><price>" + Price.ToString(IC) + "</price><quantity>" + Quantity.ToString(IC) +
            "</quantity><buysell>" + BuySell + "</buysell>" + Market;
        if (OrderType != OrderType.Conditional) Сommand += Credit + "</command>";
        else
        {
            Сommand += "<cond_type>" + Condition + "</cond_type><cond_value>" + Price.ToString(IC) + "</cond_value>" +
                "<validafter>0</validafter><validbefore>till_canceled</validbefore>" + Credit + "</command>";
        }

        // Отправка команды
        using (XmlReader XR = XmlReader.Create(new StringReader(ConnectorSendCommand(Сommand)), XS))
        {
            // Обработка результата
            XR.Read();
            if (XR.GetAttribute("success") == "true")
            {
                int id = int.Parse(XR.GetAttribute("transactionid"), IC);
                Window.Dispatcher.Invoke(() =>
                {
                    if (Sender != null) Sender.MyOrders.Add(new Order(id, Sender.Name, Signal, Note));
                    else SystemOrders.Add(new Order(id, SenderName, Signal, Note));
                });

                AddInfo("SendOrder: Заявка принята: " + SenderName + "/" + Symbol.Seccode + "/" +
                    BuySell + "/" + Price + "/" + Quantity, MySettings.DisplaySentOrders);

                XR.Close();
                return true;
            }
            else if (XR.GetAttribute("success") == "false")
            {
                XR.ReadToFollowing("message"); XR.Read();
                AddInfo("SendOrder: Заявка отправителя " + SenderName + " не принята: " + XR.Value, true, XR.Value.Contains("Недостаток обеспечения"));
            }
            else
            {
                XR.Read();
                AddInfo("SendOrder: Заявка отправителя " + SenderName + " не принята с исключением: " + XR.Value);
            }
            XR.Close();
        }
        return false;
    }
    public static bool CancelOrder(Order ActiveOrder)
    {
        string Reply = ConnectorSendCommand("<command id=\"cancelorder\"><transactionid>" + ActiveOrder.TrID + "</transactionid></command>");
        using XmlReader XR = XmlReader.Create(new StringReader(Reply), XS);
        XR.Read();

        if (XR.Name == "error" || XR.GetAttribute("success") == "false")
        {
            XR.Read(); XR.Read();
            if (XR.Value.Contains("Неверное значение параметра"))
            {
                ActiveOrder.Status = ActiveOrder.Status == "active" ? "cancelled" : "disabled";
                ActiveOrder.DateTime = DateTime.Now.AddDays(-2);
                AddInfo("CancelOrder: " + ActiveOrder.Sender + ": Активная заявка не актуальна. Статус обновлён.");
            }
            else AddInfo("CancelOrder: " + ActiveOrder.Sender + ": Ошибка отмены заявки: " + XR.Value);
            XR.Close(); return false;
        }
        AddInfo("CancelOrder: Запрос на отмену заявки отправлен " + ActiveOrder.Sender + "/" + ActiveOrder.Seccode, false);

        XR.Close();
        return true;
    }
    public static bool ReplaceOrder(Order ActiveOrder, Security Symbol, OrderType OrderType, double Price, int Quantity, string Signal,
        Script Sender = null, string Note = null, bool UseCredit = false)
    {
        string SenderName = Sender != null ? Sender.Name : "System";
        AddInfo("ReplaceOrder: Замена заявки отправителя: " + SenderName, false);
        if (CancelOrder(ActiveOrder))
        {
            System.Threading.Thread.Sleep(150);
            if (ActiveOrder.Status is not "cancelled" and not "disabled")
            {
                System.Threading.Thread.Sleep(350);
                if (ActiveOrder.Status is not "cancelled" and not "disabled")
                {
                    System.Threading.Thread.Sleep(2500);
                    if (ActiveOrder.Status is "matched")
                    {
                        AddInfo("ReplaceOrder: Заявка отправителя: " + SenderName + " уже исполнилась.");
                        return false;
                    }
                    else if (ActiveOrder.Status is not "cancelled" and not "disabled")
                        AddInfo("ReplaceOrder: Не дождались отмены заявки отправителя: " + SenderName);
                }
            }
            if (SendOrder(Symbol, OrderType, ActiveOrder.BuySell == "B", Price, Quantity, Signal, Sender, Note, UseCredit)) return true;
        }
        return false;
    }

    public static void SubUnsub(bool Subscribe, string Board, string Seccode, bool Trades = true, bool Quotations = false, bool Quotes = false)
    {
        string Сommand = Subscribe ? "<command id=\"subscribe\">" : "<command id=\"unsubscribe\">";
        if (Trades) Сommand += "<alltrades><security><board>" + Board + "</board><seccode>" + Seccode + "</seccode></security></alltrades>";
        if (Quotations) Сommand += "<quotations><security><board>" + Board + "</board><seccode>" + Seccode + "</seccode></security></quotations>";
        if (Quotes) Сommand += "<quotes><security><board>" + Board + "</board><seccode>" + Seccode + "</seccode></security></quotes>";
        Сommand += "</command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("SubUnsub: " + Result);
    }
    public static void GetHistoryData(string Board, string Seccode, string Period, int Count, string Reset = "true")
    {
        string Сommand = "<command id=\"gethistorydata\"><security><board>" + Board + "</board><seccode>" + Seccode +
            "</seccode></security><period>" + Period + "</period><count>" + Count + "</count><reset>" + Reset + "</reset></command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetHistoryData: " + Result + "\n Command: " + Сommand);
    }
    public static void GetSecurityInfo(string Market, string Seccode)
    {
        string Сommand = "<command id=\"get_securities_info\"><security><market>" + Market +
            "</market><seccode>" + Seccode + "</seccode></security></command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetSecurityInfo: " + Result);
    }
    public static void GetClnSecPermissions(string Board, string Seccode, string Market)
    {
        ClientAccount Client = Clients.SingleOrDefault(x => x.Market == Market);
        if (Client == null) { AddInfo("GetClnSecPermissions: не найден клиент."); return; }
        string Сommand = "<command id=\"get_cln_sec_permissions\"><security><board>" + Board + "</board>" +
            "<seccode>" + Seccode + "</seccode></security><client>" + Client.ID + "</client></command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetClnSecPermissions: " + Result);
    }
    public static void GetFortsPositions()
    {
        string Result = ConnectorSendCommand("<command id=\"get_forts_positions\"/>");
        if (Result != "<result success=\"true\"/>") AddInfo("GetFortsPositions: " + Result);
    }
    public static void GetPortfolio(string Union)
    {
        string Сommand = "<command id=\"get_mc_portfolio\" union=\"" + Union + "\" currency=\"false\" asset=\"false\"" +
            " money=\"false\" depo=\"true\" registers=\"false\"/>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetPortfolio: " + Result);
    }
    #endregion

    #region Methods for interaction with the connector
    public static bool ConnectorInitialize(short LogLevel)
    {
        if (!Directory.Exists("Logs/Transaq")) Directory.CreateDirectory("Logs/Transaq");

        IntPtr pPath = Marshal.StringToHGlobalAnsi("Logs/Transaq");
        IntPtr Result = Initialize(pPath, LogLevel);
        Marshal.FreeHGlobal(pPath);

        if (Result.Equals(IntPtr.Zero))
        {
            FreeMemory(Result);
            return true;
        }
        else
        {
            string Res = Marshal.PtrToStringUTF8(Result);
            FreeMemory(Result);
            AddInfo("Initialization failed: " + Res);
            return false;
        }
    }
    public static bool ConnectorUnInitialize()
    {
        if (Connection == ConnectionState.Disconnected)
        {
            IntPtr Result = UnInitialize();
            if (!Result.Equals(IntPtr.Zero))
            {
                string Res = Marshal.PtrToStringUTF8(Result);
                FreeMemory(Result);
                AddInfo("UnInitialization failed: " + Res);
            }
            else
            {
                FreeMemory(Result);
                AddInfo("UnInitialization successful.");
                return true;
            }
        }
        else AddInfo("UnInitialization failed: не было разрыва соединения с сервером.");
        return false;
    }
    public static void ConnectorSetCallback()
    {
        if (SetCallback(CallbackDel)) DataProcessor.Start();
        else throw new Exception("Callback failed.");
    }

    private static string ConnectorSendCommand(string Command)
    {
        IntPtr Data = Marshal.StringToHGlobalAnsi(Command);
        string Result = SendCommandAndGetResult(Data, Command);
        Marshal.FreeHGlobal(Data);
        return Result;
    }
    private static string ConnectorSendCommand(string StartPartCMD, string EndPartCMD, System.Security.SecureString MiddlePartCMD)
    {
        // Формирование команды
        EndPartCMD += "\0";
        IntPtr StartPart = Marshal.StringToHGlobalAnsi(StartPartCMD);
        IntPtr EndPart = Marshal.StringToHGlobalAnsi(EndPartCMD);
        IntPtr Data = Marshal.AllocHGlobal(StartPartCMD.Length + MiddlePartCMD.Length + EndPartCMD.Length);
        IntPtr MiddlePart;
        unsafe
        {
            byte* DataBytes = (byte*)Data.ToPointer();
            byte* StartPartBytes = (byte*)StartPart.ToPointer();
            for (int i = 0; i < StartPartCMD.Length; i++) *DataBytes++ = *StartPartBytes++;

            MiddlePart = Marshal.SecureStringToGlobalAllocAnsi(MiddlePartCMD);
            byte* MiddlePartBytes = (byte*)MiddlePart.ToPointer();
            for (int i = 0; i < MiddlePartCMD.Length; i++) *DataBytes++ = *MiddlePartBytes++;

            byte* EndPartBytes = (byte*)EndPart.ToPointer();
            for (int i = 0; i < EndPartCMD.Length; i++) *DataBytes++ = *EndPartBytes++;
        }

        // Отправка команды, очистка памяти и возвращение результата
        string Result = SendCommandAndGetResult(Data, StartPartCMD);
        Marshal.FreeHGlobal(Data);
        Marshal.ZeroFreeGlobalAllocAnsi(MiddlePart);
        Marshal.FreeHGlobal(StartPart);
        Marshal.FreeHGlobal(EndPart);
        return Result;
    }
    private static string SendCommandAndGetResult(IntPtr Data, string Command)
    {
        string Result = "Empty";
        Task MyTask = Task.Run(() =>
        {
            IntPtr ResultIntPtr = SendCommand(Data);
            Result = Marshal.PtrToStringUTF8(ResultIntPtr);
            FreeMemory(ResultIntPtr);
        });
        try
        {
            if (!MyTask.Wait(2000))
            {
                SystemReadyToTrading = false;
                AddInfo("SendCommand: Превышено время ожидания ответа сервера. Торговля приостановлена.", false);
                if (!MyTask.Wait(WaitingTime))
                {
                    ServerAvailable = false;
                    if (Connection == ConnectionState.Connected)
                    {
                        Connection = ConnectionState.Connecting;
                        TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
                    }
                    AddInfo("SendCommand: Сервер не отвечает. Команда: " + Command, false);

                    if (!MyTask.Wait(WaitingTime * 15))
                    {
                        AddInfo("SendCommand: Бесконечное ожидание ответа сервера.", notify: true);
                        MyTask.Wait();
                    }
                    AddInfo("SendCommand: Ответ сервера получен.", false);
                    ServerAvailable = true;
                }
                else if (Connection == ConnectionState.Connected) SystemReadyToTrading = true;
            }
        }
        catch (Exception e) { AddInfo("SendCommand: Исключение отправки команды: " + e.Message); }
        finally { MyTask.Dispose(); }
        return Result;
    }
    #endregion

    #region Importing external methods
    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool SetCallback(CallBackDelegate Delegate);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr SendCommand(IntPtr Data);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool FreeMemory(IntPtr Data);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr Initialize(IntPtr Path, int LogLevel);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr UnInitialize();
    #endregion
}
