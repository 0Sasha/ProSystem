using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace ProSystem;

internal class TXmlConnector : Connector
{
    private readonly MainWindow Window;

    private delegate void AddInfoDel(string data, bool important = true, bool notify = false);
    private readonly AddInfoDel AddInfo;

    private delegate bool CallBackDel(IntPtr Data);
    private readonly CallBackDel CallbackDel;

    private bool scheduled;
    private int waitingTime = 18000;
    private ConnectionState connection;
    private Thread DataProcessor;

    private readonly ConcurrentQueue<string> DataQueue = new();

    private readonly XmlReaderSettings XS = new()
    {
        IgnoreWhitespace = true,
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Parse
    };
    private readonly string DTForm = "dd.MM.yyyy HH:mm:ss";

    private readonly StringComparison SC = StringComparison.Ordinal;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    public TradingSystem TradingSystem { get; set; }
    public List<ClientAccount> Clients { get; set; }
    public override ConnectionState Connection
    {
        get => connection;
        set
        {
            if (connection != value)
            {
                connection = value;
                if (connection == ConnectionState.Connected)
                {
                    Window.Dispatcher.Invoke(() =>
                    {
                        Window.ConnectBtn.Content = "Disconnect";
                        Window.StCon.Fill = Theme.Green;
                    });
                    Task.Run(() =>
                    {
                        TradingSystem.PrepareForTrading();
                        Window.Dispatcher.Invoke(() => Window.ShowDistributionInfo(null, null));
                    });
                }
                else Window.Dispatcher.Invoke(() =>
                {
                    if (connection == ConnectionState.Connecting)
                    {
                        Window.ConnectBtn.Content = "Disconnect";
                        Window.StCon.Fill = Theme.Orange;
                    }
                    else
                    {
                        Window.ConnectBtn.Content = "Connect";
                        Window.StCon.Fill = connection == ConnectionState.Disconnected ? Theme.Gray : Theme.Red;
                    }
                });
            }
        }
    }

    public TXmlConnector(MainWindow window)
    {
        Window = window;
        CallbackDel = CallBack;
        AddInfo = Window.AddInfo;
    }

    #region Methods for processing data
    private bool CallBack(IntPtr data)
    {
        DataQueue.Enqueue(Marshal.PtrToStringUTF8(data));
        FreeMemory(data);
        return true;
    }

    private void ProcessDataQueue()
    {
        while (true)
        {
            string data = null;
            try
            {
                while (!DataQueue.IsEmpty)
                {
                    if (DataQueue.TryDequeue(out data)) ProcessData(data);
                    else AddInfo("ProcessDataQueue: не удалось взять объект из очереди.");
                }
            }
            catch (Exception e)
            {
                AddInfo("ProcessDataQueue: Исключение: " + e.Message, notify: true);
                AddInfo("Трассировка стека: " + e.StackTrace);
                AddInfo("Данные: " + data, false);
                if (e.InnerException != null)
                {
                    AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                    AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
                }
            }
            System.Threading.Thread.Sleep(5);
        }
    }

    private string ProcessData(string data)
    {
        string section = null;
        using XmlReader xr = XmlReader.Create(new StringReader(data), XS);
        var tools = TradingSystem.Tools;

        while (xr.Read())
        {
            section = xr.Name;

            // Высокочастотные секции
            if (section == "alltrades")
            {
                while (xr.Read())
                {
                    if (!xr.ReadToFollowing("time")) { xr.Close(); return section; }
                    xr.Read();
                    var trade = new Trade(DateTime.ParseExact(xr.Value, DTForm, IC));

                    if (!xr.ReadToFollowing("price")) { AddInfo("alltrades: Нет price сделки."); continue; }
                    xr.Read(); trade.Price = double.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("quantity")) { AddInfo("alltrades: Нет quantity сделки."); continue; }
                    xr.Read(); trade.Quantity = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("seccode")) { AddInfo("alltrades: Нет seccode сделки."); continue; }
                    xr.Read();
                    var tool = tools.SingleOrDefault(x => x.MySecurity.Seccode == xr.Value);

                    if (tool != null)
                    {
                        tool.MySecurity.LastTrade = trade;
                        UpdateSecurityBars(tool.MySecurity);
                    }
                    else
                    {
                        tools.Single(x => x.BasicSecurity != null && x.BasicSecurity.Seccode == xr.Value)
                            .BasicSecurity.LastTrade = trade;
                        UpdateSecurityBars(tool.BasicSecurity);
                    }
                }
            }
            else if (section == "candles")
            {
                if (xr.GetAttribute("status") == "3")
                {
                    AddInfo("candles: Запрошенные данные недоступны. Запросите позже.");
                    xr.Close(); return section;
                }

                Security security;
                int i = Array.FindIndex(tools.ToArray(), x => x.MySecurity.Seccode == xr.GetAttribute("seccode"));
                if (i > -1) security = tools[i].MySecurity;
                else
                {
                    i = Array.FindIndex(tools.ToArray(), x => x.BasicSecurity != null && x.BasicSecurity.Seccode == xr.GetAttribute("seccode"));
                    if (i > -1) security = tools[i].BasicSecurity;
                    else
                    {
                        string sec = xr.GetAttribute("seccode"); xr.Read();
                        if (sec.Contains("USD")) USDRUB = double.Parse(xr.GetAttribute("close"), IC);
                        else if (sec.Contains("EUR")) EURRUB = double.Parse(xr.GetAttribute("close"), IC);
                        else AddInfo("candles: Неактуальный инструмент: " + sec);
                        xr.Close(); return section;
                    }
                }

                ProcessBars(xr, tools[i], security);
                xr.Close();
                return section;
            }
            // Базовые секции
            else if (section == "orders")
            {
                ProcessOrders(xr, TradingSystem.Orders);
                xr.Close();
                return section;
            }
            else if (section == "positions")
            {
                xr.Read();
                ProcessPositions(xr, xr.Name, TradingSystem.Portfolio);
                xr.Close();
                return section;
            }
            else if (section == "trades")
            {
                ProcessTrades(xr, TradingSystem.Trades);
                xr.Close();
                return section;
            }
            else if (section == "server_status")
            {
                if (xr.GetAttribute("connected") == "true")
                {
                    ServerAvailable = true;
                    if (xr.GetAttribute("recover") != "true")
                    {
                        Connection = ConnectionState.Connected;
                        AddInfo("Connected", !scheduled);
                        scheduled = false;
                    }
                    else
                    {
                        TriggerReconnection = DateTime.Now.AddSeconds(TradingSystem.Settings.SessionTM);
                        Connection = ConnectionState.Connecting;
                        AddInfo("Recover connection");
                    }
                }
                else if (xr.GetAttribute("connected") == "false")
                {
                    TradingSystem.ReadyToTrade = false;
                    ServerAvailable = true;

                    if (xr.GetAttribute("recover") != "true")
                    {
                        Connection = ConnectionState.Disconnected;
                        AddInfo("Disconnected", !scheduled);
                        scheduled = false;
                    }
                    else
                    {
                        Connection = ConnectionState.Connecting;
                        AddInfo("Recover");
                    }
                }
                else if (xr.GetAttribute("connected") == "error")
                {
                    TradingSystem.ReadyToTrade = false;
                    ServerAvailable = false;
                    BackupServer = !BackupServer;

                    Connection = ConnectionState.Disconnected;
                    xr.Read(); AddInfo("Server error: " + xr.Value + " BackupServer: " + !BackupServer, notify: true);
                }
            }
            else if (section == "mc_portfolio")
            {
                ProcessPortfolio(xr, TradingSystem.Portfolio);
                xr.Close();
                return section;
            }
            // Второстепенные секции
            else if (section == "sec_info_upd")
            {
                if (!xr.ReadToFollowing("seccode"))
                {
                    AddInfo("sec_info_upd: Нет seccode.");
                    xr.Close();
                    return section;
                }
                xr.Read();

                Tool MyTool;
                if (tools.SingleOrDefault(x => x.MySecurity.Seccode == xr.Value) == null) { xr.Close(); return section; }
                else MyTool = tools.Single(x => x.MySecurity.Seccode == xr.Value);

                string Name = "";
                while (xr.Read())
                {
                    if (xr.Name.Length > 0) Name = xr.Name;
                    else if (xr.HasValue)
                    {
                        if (Name == "buy_deposit") MyTool.MySecurity.BuyDeposit = double.Parse(xr.Value, IC);
                        else if (Name == "sell_deposit") MyTool.MySecurity.SellDeposit = double.Parse(xr.Value, IC);
                        else if (Name == "minprice") MyTool.MySecurity.MinPrice = double.Parse(xr.Value, IC);
                        else if (Name == "maxprice") MyTool.MySecurity.MaxPrice = double.Parse(xr.Value, IC);
                        else if (Name == "point_cost") MyTool.MySecurity.PointCost = double.Parse(xr.Value, IC);
                    }
                }
                xr.Close();
                return section;
            }
            else if (section == "sec_info")
            {
                if (!xr.ReadToFollowing("seccode"))
                {
                    AddInfo("sec_info: Нет seccode.");
                    xr.Close();
                    return section;
                }
                xr.Read();

                Tool MyTool;
                if (tools.SingleOrDefault(x => x.MySecurity.Seccode == xr.Value) == null) { xr.Close(); return section; }
                else MyTool = tools.Single(x => x.MySecurity.Seccode == xr.Value);

                if (!xr.ReadToFollowing("minprice"))
                {
                    AddInfo("sec_info: Нет minprice.");
                    xr.Close();
                    return section;
                }
                xr.Read(); MyTool.MySecurity.MinPrice = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("maxprice"))
                {
                    AddInfo("sec_info: Нет maxprice.");
                    xr.Close();
                    return section;
                }
                xr.Read(); MyTool.MySecurity.MaxPrice = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("buy_deposit"))
                {
                    AddInfo("sec_info: Нет buy_deposit.");
                    xr.Close();
                    return section;
                }
                xr.Read(); MyTool.MySecurity.BuyDeposit = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("sell_deposit"))
                {
                    AddInfo("sec_info: Нет sell_deposit.");
                    xr.Close();
                    return section;
                }
                xr.Read(); MyTool.MySecurity.SellDeposit = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("point_cost"))
                {
                    AddInfo("sec_info: Нет point_cost.");
                    xr.Close();
                    return section;
                }
                xr.Read(); MyTool.MySecurity.PointCost = double.Parse(xr.Value, IC);
                xr.Close(); return section;
            }
            else if (section == "cln_sec_permissions")
            {
                if (!xr.ReadToFollowing("seccode")) { AddInfo("sec_permissions: no seccode"); xr.Close(); return section; }
                xr.Read(); string Seccode = xr.Value;

                if (tools.SingleOrDefault(x => x.MySecurity.Seccode == Seccode) == null)
                { AddInfo("sec_permissions: неактуальный инструмент: " + Seccode); xr.Close(); return section; }
                var tool = tools.Single(x => x.MySecurity.Seccode == Seccode);

                if (!xr.ReadToFollowing("riskrate_long")) { AddInfo("sec_permissions: no riskrate_long"); xr.Close(); return section; }
                xr.Read(); tool.MySecurity.RiskrateLong = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("reserate_long")) { AddInfo("sec_permissions: no reserate_long"); xr.Close(); return section; }
                xr.Read(); tool.MySecurity.ReserateLong = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("riskrate_short")) { AddInfo("sec_permissions: no riskrate_short"); xr.Close(); return section; }
                xr.Read(); tool.MySecurity.RiskrateShort = double.Parse(xr.Value, IC);

                if (!xr.ReadToFollowing("reserate_short")) { AddInfo("sec_permissions: no reserate_short"); xr.Close(); return section; }
                xr.Read(); tool.MySecurity.ReserateShort = double.Parse(xr.Value, IC);

                Task.Run(() => UpdateSecurityRequirements(tool.MySecurity));

                /*if (!XR.ReadToFollowing("riskrate_longx")) { AddInfo("sec_permissions: no riskrate_longx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinRiskrateLong = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("reserate_longx")) { AddInfo("sec_permissions: no reserate_longx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinReserateLong = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("riskrate_shortx")) { AddInfo("sec_permissions: no riskrate_shortx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinRiskrateShort = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("reserate_shortx")) { AddInfo("sec_permissions: no reserate_shortx"); XR.Close(); return Section; }
                XR.Read(); MyTool.MySecurity.MinReserateShort = double.Parse(XR.Value, IC);*/

                xr.Close(); return section;
            }
            else if (section == "messages") // Текстовые сообщения
            {
                if (xr.ReadToFollowing("text")) { xr.Read(); AddInfo(xr.Value, TradingSystem.Settings.DisplayMessages); }
                break;
            }
            else if (section == "error") // Внутренние ошибки dll
            {
                xr.Read(); AddInfo(xr.Value);
                break;
            }
            else if (section == "securities")
            {
                while (xr.Read())
                {
                    if (xr.Name != "security" && !xr.ReadToFollowing("security")) { xr.Close(); return section; }
                    if (xr.GetAttribute("active") == "false") continue;

                    if (!xr.ReadToFollowing("seccode")) { AddInfo("Не найден seccode."); continue; }
                    xr.Read(); Securities.Add(new Security(xr.Value));

                    while (xr.Read())
                    {
                        if (xr.NodeType == XmlNodeType.EndElement)
                        {
                            if (xr.Name == "security") break;
                            continue;
                        }
                        if (xr.NodeType == XmlNodeType.Element)
                        {
                            if (xr.Name == "currency")
                            {
                                xr.Read();
                                Securities[^1].Currency = xr.Value;
                            }
                            else if (xr.Name == "board")
                            {
                                xr.Read();
                                Securities[^1].Board = xr.Value;
                            }
                            else if (xr.Name == "shortname")
                            {
                                xr.Read();
                                Securities[^1].ShortName = xr.Value;
                            }
                            else if (xr.Name == "decimals")
                            {
                                xr.Read();
                                Securities[^1].Decimals = int.Parse(xr.Value, IC);
                            }
                            else if (xr.Name == "market")
                            {
                                xr.Read();
                                Securities[^1].Market = xr.Value;
                            }
                            else if (xr.Name == "minstep")
                            {
                                xr.Read();
                                Securities[^1].MinStep = double.Parse(xr.Value, IC);
                            }
                            else if (xr.Name == "lotsize")
                            {
                                xr.Read();
                                Securities[^1].LotSize = int.Parse(xr.Value, IC);
                            }
                            else if (xr.Name == "point_cost")
                            {
                                xr.Read();
                                Securities[^1].PointCost = double.Parse(xr.Value, IC);
                            }
                        }

                        /*if (!XR.ReadToFollowing("currency")) { AddInfo(Securities[^1].Seccode + ": Не найден currency.", false); continue; }
                        XR.Read(); Securities[^1].Currency = XR.Value;

                        if (!XR.ReadToFollowing("board")) { AddInfo(Securities[^1].Seccode + ": Не найден board."); continue; }
                        XR.Read(); Securities[^1].Board = XR.Value;

                        if (!XR.ReadToFollowing("shortname")) { AddInfo(Securities[^1].Seccode + ": Не найден shortname."); continue; }
                        XR.Read(); Securities[^1].ShortName = XR.Value;

                        if (!XR.ReadToFollowing("decimals")) { AddInfo(Securities[^1].Seccode + ": Не найден decimals."); continue; }
                        XR.Read(); Securities[^1].Decimals = int.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("market")) { AddInfo(Securities[^1].Seccode + ": Не найден market."); continue; }
                        XR.Read(); Securities[^1].Market = XR.Value;

                        if (!XR.ReadToFollowing("minstep")) { AddInfo(Securities[^1].Seccode + ": Не найден minstep."); continue; }
                        XR.Read(); Securities[^1].MinStep = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("lotsize")) { AddInfo(Securities[^1].Seccode + ": Не найден lotsize."); continue; }
                        XR.Read(); Securities[^1].LotSize = int.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("point_cost")) { AddInfo(Securities[^1].Seccode + ": Не найден point_cost."); continue; }
                        XR.Read(); Securities[^1].PointCost = double.Parse(XR.Value, IC);*/
                    }
                }
            }
            else if (section == "client")
            {
                string id, market;
                if (xr.GetAttribute("remove") == "false")
                {
                    if (Clients.SingleOrDefault(x => x.ID == xr.GetAttribute("id")) == null)
                        id = xr.GetAttribute("id");
                    else { AddInfo("Клиент уже есть в коллекции."); xr.Close(); return section; }
                }
                else { Clients.Remove(Clients.Single(x => x.ID == xr.GetAttribute("id"))); xr.Close(); return section; }

                if (!xr.ReadToFollowing("market")) { AddInfo("client: no market"); xr.Close(); return section; };
                xr.Read(); market = xr.Value;

                if (!xr.ReadToFollowing("union")) { AddInfo("client: no union"); xr.Close(); return section; };
                xr.Read(); Clients.Add(new(id, market, xr.Value));

                xr.Close(); return section;
            }
            else if (section == "markets")
            {
                string ID = null;
                while (xr.Read())
                {
                    if (xr.HasAttributes) ID = xr.GetAttribute("id");
                    else if (xr.HasValue) Markets.Add(new Market(ID, xr.Value));
                }
                xr.Close(); return section;
            }
            else if (section == "candlekinds")
            {
                string ID = null;
                while (xr.Read())
                {
                    if (!xr.ReadToFollowing("id")) { xr.Close(); return section; }
                    xr.Read(); ID = xr.Value;

                    if (!xr.ReadToFollowing("period")) { AddInfo("candlekinds: no period"); xr.Close(); return section; }
                    xr.Read(); int Period = int.Parse(xr.Value, IC);

                    if (!xr.ReadToFollowing("name")) { AddInfo("candlekinds: no name"); xr.Close(); return section; }
                    xr.Read(); TimeFrames.Add(new TimeFrame(ID, Period, xr.Value));
                }
            }
            else if (section is "marketord" or "pits" or "boards" or "union" or "overnight" or "news_header") return section;
            else { AddInfo("ProcessData: Неизвестная секция: " + section); return section; }
            //else if (Section == "clientlimits" || Section == "quotes" || Section == "quotations") return Section;
        }
        xr.Close();
        return section;
    }

    private void ProcessBars(XmlReader xr, Tool tool, Security security)
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

                Task.Run(() => TradingSystem.ToolManager.UpdateBars(tool, security == tool.BasicSecurity));
                return;
            }
        }
        AddInfo("ProcessBars: Не найден EndElement");
    }

    private void ProcessOrders(XmlReader xr, ObservableCollection<Order> orders)
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

    private void ProcessTrades(XmlReader xr, ObservableCollection<Trade> trades)
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
            bool display = TradingSystem.Settings.DisplayNewTrades && trades.Count > 1 && (trades[^2].Seccode != trade.Seccode ||
                trades[^2].BuySell != trade.BuySell || trades[^2].DateTime < trade.DateTime.AddMinutes(-30));
            AddInfo("Новая сделка: " + trade.Seccode + "/" + trade.BuySell + "/" + trade.Price + "/" + trade.Quantity, display);
        }
    }

    private void ProcessPositions(XmlReader xr, string subsection, UnitedPortfolio portfolio)
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
                    pos = CreatePosition(xr.Value);
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

    private Position CreatePosition(string seccode)
    {
        var sec = Securities.SingleOrDefault(x => x.Seccode == seccode);
        if (sec != null)
        {
            var market = Markets.SingleOrDefault(x => x.ID == sec.Market);
            if (market != null) return new(seccode, sec.ShortName, sec.Market, market.Name);
            else throw new ArgumentException("Не найден рынок по Market актива.");
        }
        else throw new ArgumentException("Не найден актив по Seccode позиции.");
    }

    private void ProcessPortfolio(XmlReader xr, UnitedPortfolio portfolio)
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
                pos = CreatePosition(xr.Value);
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

    private void UpdateSecurityBars(Security security)
    {
        var bars = security.Bars;
        if (security.LastTrade.DateTime < security.Bars.DateTime[^1].AddMinutes(security.Bars.TF))
        {
            bars.Close[^1] = security.LastTrade.Price;
            if (security.LastTrade.Price > bars.High[^1]) bars.High[^1] = security.LastTrade.Price;
            else if (security.LastTrade.Price < bars.Low[^1]) bars.Low[^1] = security.LastTrade.Price;
            bars.Volume[^1] += security.LastTrade.Quantity;
        }
        else if (DateTime.Now > security.LastTrDT) // Открытие нового бара
        {
            security.LastTrDT = DateTime.Now.AddSeconds(10);
            Tool MyTool = TradingSystem.Tools.Single(x => x.MySecurity.Seccode == security.Seccode ||
                x.BasicSecurity != null && x.BasicSecurity.Seccode == security.Seccode);
            MyTool.TimeNextRecalc = DateTime.Now.AddSeconds(30);

            // Создание нового бара
            if (DateTime.Now.Date == bars.DateTime[^1].Date) bars.DateTime =
                    bars.DateTime.Concat(new DateTime[] { bars.DateTime[^1].AddMinutes(bars.TF) }).ToArray();
            else bars.DateTime =
                    bars.DateTime.Concat(new DateTime[] { DateTime.Now.Date.AddHours(DateTime.Now.Hour) }).ToArray();
            bars.Open = bars.Open.Concat(new double[] { security.LastTrade.Price }).ToArray();
            bars.High = bars.High.Concat(new double[] { security.LastTrade.Price }).ToArray();
            bars.Low = bars.Low.Concat(new double[] { security.LastTrade.Price }).ToArray();
            bars.Close = bars.Close.Concat(new double[] { security.LastTrade.Price }).ToArray();
            bars.Volume = bars.Volume.Concat(new double[] { security.LastTrade.Quantity }).ToArray();

            // Пересчёт скриптов и запрос оригинальных баров
            Task.Run(async () =>
            {
                await Task.Delay(250);
                Order LastExecuted = TradingSystem.Orders.ToArray()
                .LastOrDefault(x => x.Seccode == MyTool.MySecurity.Seccode && x.Status == "matched");

                if (LastExecuted != null && LastExecuted.DateTime.AddSeconds(3) > DateTime.Now)
                {
                    Window.AddInfo(MyTool.Name + ": Заявка исполнилась одновременно с открытием бара. Ожидание.", false);
                    await Task.Delay(2000);
                }
                else if (MyTool.MySecurity.Seccode == security.Seccode)
                {
                    Order[] Active = TradingSystem.Orders.ToArray()
                    .Where(x => x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
                    if (Active.Any(x => Math.Abs(x.Price - security.LastTrade.Price) < 0.00001))
                    {
                        Window.AddInfo(MyTool.Name + ": Цена активной заявки равна цене открытия бара. Ожидание.", false);
                        await Task.Delay(2000);
                    }
                }

                TradingSystem.ToolManager.Calculate(MyTool);
                MyTool.MainModel.InvalidatePlot(true);
                TradingSystem.ToolManager.RequestBars(MyTool);
            });
        }
    }

    private void UpdateSecurityRequirements(Security security)
    {
        security.MinStepCost = security.PointCost * security.MinStep * Math.Pow(10, security.Decimals) / 100;
        if (security.Bars == null) Thread.Sleep(5000);
        if (security.Bars != null)
        {
            security.LastTrade ??= new Trade()
            {
                Price = security.Bars.Close[^1],
                DateTime = security.Bars.DateTime[^1]
            };
            double LastPrice = security.LastTrade.DateTime > security.Bars.DateTime[^1] ?
                security.LastTrade.Price : security.Bars.Close[^1];
            double Value = LastPrice * security.MinStepCost / security.MinStep * security.LotSize / 100;

            security.InitReqLong = Math.Round((security.RiskrateLong + security.ReserateLong) * Value, 2);
            security.InitReqShort = Math.Round((security.RiskrateShort + security.ReserateShort) * Value, 2);
        }
        else Window.AddInfo("Не удалось обновить требования, потому что нет баров.");
    }
    #endregion

    #region Methods for sending commands
    public override void Connect(bool scheduled = false)
    {
        if (!Initialized)
        {
            AddInfo("Коннектор не инициализирован.");
            return;
        }

        var settings = TradingSystem.Settings;

        // Очистка данных
        Securities.Clear(); Markets.Clear();
        TimeFrames.Clear(); Clients.Clear();
        Window.Dispatcher.Invoke(() => TradingSystem.Orders.Clear());

        // Подготовка переменных
        waitingTime = settings.RequestTM * 1000 + 3000;
        Connection = ConnectionState.Connecting;

        // Формирование команды
        string RqDelaq = "20"; // Задает частоту обращений коннектора к серверу Transaq в миллисекундах. Минимум 10.
        string UnLimits = settings.SessionTM.ToString(IC); // Таймаут информирования о текущих показателях единого портфеля минимум один раз в N секунд.
        string Equity = "0"; // Таймаут информирования о текущей стоимости позиций один раз в N секунд за исключением позиций FORTS.

        string ServerIP = !BackupServer ? "tr1.finam.ru" : "tr2.finam.ru", ServerPort = "3900";
        string StartPart = "<command id=\"connect\"><login>" + settings.LoginConnector + "</login><password>";
        string EndPart = "</password><host>" + ServerIP + "</host><port>" + ServerPort + "</port><rqdelay>" + RqDelaq + "</rqdelay><session_timeout>" +
            settings.SessionTM.ToString(IC) + "</session_timeout><request_timeout>" + settings.RequestTM.ToString(IC) +
            "</request_timeout><push_u_limits>" + UnLimits + "</push_u_limits><push_pos_equity>" + Equity + "</push_pos_equity></command>";

        // Отправка команды
        this.scheduled = scheduled;
        string Result = ConnectorSendCommand(StartPart, EndPart, Window.Dispatcher.Invoke(() => Window.TxtPas.SecurePassword));
        GC.Collect();

        if (Result.StartsWith("<result success=\"true\"", SC) || Result.Contains("уже устанавливается")) return; // "уже установлено"
        else
        {
            Connection = ConnectionState.Disconnected;
            AddInfo(Result);
        }
    }

    public override void Disconnect(bool scheduled = false)
    {
        TradingSystem.ReadyToTrade = false;
        Connection = ConnectionState.Disconnecting;

        this.scheduled = scheduled;
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

    public override bool SendOrder(Security Symbol, OrderType OrderType, bool IsBuy,
        double Price, int Quantity, string Signal, Script Sender = null, string Note = null)
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
        string Credit = ""; // UseCredit ? "<usecredit/>" : 
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
            "</seccode></security><union>" + TradingSystem.Portfolio.Union + "</union><price>" + Price.ToString(IC) + "</price><quantity>" + Quantity.ToString(IC) +
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
                    if (Sender != null) Sender.Orders.Add(new Order(id, Sender.Name, Signal, Note));
                    else TradingSystem.SystemOrders.Add(new Order(id, SenderName, Signal, Note));
                });

                AddInfo("SendOrder: Заявка принята: " + SenderName + "/" + Symbol.Seccode + "/" +
                    BuySell + "/" + Price + "/" + Quantity, TradingSystem.Settings.DisplaySentOrders);

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

    public override bool CancelOrder(Order ActiveOrder)
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

    public override bool ReplaceOrder(Order ActiveOrder, Security Symbol, OrderType OrderType,
        double Price, int Quantity, string Signal, Script Sender = null, string Note = null)
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
            if (SendOrder(Symbol, OrderType, ActiveOrder.BuySell == "B", Price, Quantity, Signal, Sender, Note)) return true;
        }
        return false;
    }

    public override void SubscribeToTrades(Security security)
    {
        SubUnsub(true, security.Board, security.Seccode);
    }

    public override void UnsubscribeFromTrades(Security security)
    {
        SubUnsub(false, security.Board, security.Seccode);
    }

    private void SubUnsub(bool Subscribe, string Board, string Seccode, bool Trades = true, bool Quotations = false, bool Quotes = false)
    {
        string Сommand = Subscribe ? "<command id=\"subscribe\">" : "<command id=\"unsubscribe\">";
        if (Trades) Сommand += "<alltrades><security><board>" + Board + "</board><seccode>" + Seccode + "</seccode></security></alltrades>";
        if (Quotations) Сommand += "<quotations><security><board>" + Board + "</board><seccode>" + Seccode + "</seccode></security></quotations>";
        if (Quotes) Сommand += "<quotes><security><board>" + Board + "</board><seccode>" + Seccode + "</seccode></security></quotes>";
        Сommand += "</command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("SubUnsub: " + Result);
    }
    
    public override void OrderHistoricalData(Security symbol, TimeFrame tf, int count)
    {
        string Сommand = "<command id=\"gethistorydata\"><security><board>" + symbol.Board + "</board><seccode>" +
            symbol.Seccode + "</seccode></security><period>" + tf.ID + "</period><count>" + count +
            "</count><reset>true</reset></command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetHistoryData: " + Result + "\n Command: " + Сommand);
    }

    public override void OrderSecurityInfo(Security symbol)
    {
        GetSecurityInfo(symbol.Market, symbol.Seccode);
        GetClnSecPermissions(symbol.Board, symbol.Seccode, symbol.Market);
    }

    private void GetSecurityInfo(string Market, string Seccode)
    {
        string Сommand = "<command id=\"get_securities_info\"><security><market>" + Market +
            "</market><seccode>" + Seccode + "</seccode></security></command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetSecurityInfo: " + Result);
    }

    private void GetClnSecPermissions(string Board, string Seccode, string Market)
    {
        ClientAccount Client = Clients.SingleOrDefault(x => x.Market == Market);
        if (Client == null) { AddInfo("GetClnSecPermissions: не найден клиент."); return; }
        string Сommand = "<command id=\"get_cln_sec_permissions\"><security><board>" + Board + "</board>" +
            "<seccode>" + Seccode + "</seccode></security><client>" + Client.ID + "</client></command>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetClnSecPermissions: " + Result);
    }

    public override void OrderPortfolioInfo(UnitedPortfolio portfolio)
    {
        string Сommand = "<command id=\"get_mc_portfolio\" union=\"" + portfolio.Union +
            "\" currency=\"false\" asset=\"false\" money=\"false\" depo=\"true\" registers=\"false\"/>";

        string Result = ConnectorSendCommand(Сommand);
        if (Result != "<result success=\"true\"/>") AddInfo("GetPortfolio: " + Result);
    }
    #endregion

    #region Methods for interaction with the connector
    public override bool Initialize(int logLevel)
    {
        if (!Directory.Exists("Logs/Transaq")) Directory.CreateDirectory("Logs/Transaq");

        IntPtr pPath = Marshal.StringToHGlobalAnsi("Logs/Transaq");
        IntPtr Result = Initialize(pPath, logLevel);
        Marshal.FreeHGlobal(pPath);

        if (Result.Equals(IntPtr.Zero))
        {
            FreeMemory(Result);
            Initialized = true;
            if (SetCallback(CallbackDel))
            {
                DataProcessor = new(ProcessDataQueue) { IsBackground = true, Name = "DataProcessor" };
                DataProcessor.Start();
            }
            else throw new Exception("Callback failed.");
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

    public override bool Uninitialize()
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
                Initialized = false;
                AddInfo("UnInitialization successful.");
                return true;
            }
        }
        else AddInfo("UnInitialization failed: не было разрыва соединения с сервером.");
        return false;
    }

    public void ConnectorSetCallback()
    {
        if (SetCallback(CallbackDel)) DataProcessor.Start();
        else throw new Exception("Callback failed.");
    }

    private string ConnectorSendCommand(string Command)
    {
        if (Connection != ConnectionState.Connected) throw new Exception("There is no connection");
        IntPtr Data = Marshal.StringToHGlobalAnsi(Command);
        string Result = SendCommandAndGetResult(Data, Command);
        Marshal.FreeHGlobal(Data);
        return Result;
    }

    private string ConnectorSendCommand(string StartPartCMD, string EndPartCMD, System.Security.SecureString MiddlePartCMD)
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
    
    private string SendCommandAndGetResult(IntPtr Data, string Command)
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
                Window.TradingSystem.ReadyToTrade = false;
                AddInfo("SendCommand: Превышено время ожидания ответа сервера. Торговля приостановлена.", false);
                if (!MyTask.Wait(waitingTime))
                {
                    ServerAvailable = false;
                    if (Connection == ConnectionState.Connected)
                    {
                        Connection = ConnectionState.Connecting;
                        TriggerReconnection = DateTime.Now.AddSeconds(TradingSystem.Settings.SessionTM);
                    }
                    AddInfo("SendCommand: Сервер не отвечает. Команда: " + Command, false);

                    if (!MyTask.Wait(waitingTime * 15))
                    {
                        AddInfo("SendCommand: Бесконечное ожидание ответа сервера.", notify: true);
                        MyTask.Wait();
                    }
                    AddInfo("SendCommand: Ответ сервера получен.", false);
                    ServerAvailable = true;
                }
                else if (Connection == ConnectionState.Connected) Window.TradingSystem.ReadyToTrade = true;
            }
        }
        catch (Exception e) { AddInfo("SendCommand: Исключение отправки команды: " + e.Message); }
        finally { MyTask.Dispose(); }
        return Result;
    }
    #endregion

    #region Import external methods
    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool SetCallback(CallBackDel Delegate);

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
