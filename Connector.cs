using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static ProSystem.MainWindow;
namespace ProSystem;

internal static class TXmlConnector
{
    #region Fields
    private delegate bool CallBackDelegate(IntPtr Data);
    private static readonly CallBackDelegate CallbackDel = new(CallBack);

    private static Tool MyTool;
    private static Trade MyTrade;
    private static Order MyOrder;
    private static Order[] MyOrders;
    private static int WaitingTime = 18000;
    private static DateTime TriggerDataProcessing = DateTime.Now;

    private static readonly List<double> sOpen = new();
    private static readonly List<double> sHigh = new();
    private static readonly List<double> sLow = new();
    private static readonly List<double> sClose = new();
    private static readonly List<double> sVolume = new();
    private static readonly List<DateTime> sDateTime = new();

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
        bool ShowInfo = false;
        while (true)
        {
            string Data = null;
            try
            {
                if (DataQueue.Count > 24 && DateTime.Now > TriggerDataProcessing && Connection == ConnectionState.Connected)
                {
                    if (DateTime.Now < DateTime.Today.AddMinutes(840) || DateTime.Now > DateTime.Today.AddMinutes(1140) ||
                        DateTime.Now > DateTime.Today.AddMinutes(845) && DateTime.Now < DateTime.Today.AddMinutes(1125))
                    {
                        ShowInfo = true;
                        string[] Queue = DataQueue.ToArray();
                        AddInfo("ProcessDataQueue: данных в очереди: " + DataQueue.Count + " Данные: " +
                            Queue[0][0..12] + "/" + Queue[1][0..12] + "//" + Queue[23][0..12] + "/" + Queue[24][0..12] + "//" + Queue[^1][0..12], false);
                    }
                    TriggerDataProcessing = DateTime.Now.AddSeconds(15);
                }
                while (!DataQueue.IsEmpty)
                {
                    if (DataQueue.TryDequeue(out Data))
                    {
                        string Section = ProcessData(Data);
                        if (Section == "positions") Task.Run(() => Window.UpdatePortfolio());
                    }
                    else AddInfo("ProcessDataQueue: не удалось взять объект из очереди.");
                }
                if (ShowInfo)
                {
                    ShowInfo = false;
                    AddInfo("ProcessDataQueue: Обработка очереди данных завершена.", false);
                }
            }
            catch (Exception e)
            {
                AddInfo("ProcessDataQueue: Исключение: " + e.Message, SendEmail: true);
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
            // Определение секции
            Section = XR.Name;

            // Обработка
            // Высокочастотные секции
            if (Section == "alltrades")
            {
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
                // Проверка статуса
                if (XR.GetAttribute("status") == "3")
                {
                    AddInfo("candles: Запрошенные данные недоступны. Запросите позже.");
                    XR.Close(); return Section;
                }

                // Идентификация инструмента
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

                // Проверка ТФ
                int TF = TimeFrames.Single(x => x.ID == XR.GetAttribute("period")).Period / 60;
                if (MySecurity.SourceBars == null || MySecurity.SourceBars.TF != TF) MySecurity.SourceBars = new Bars(TF);

                // Подготовка коллекций
                sDateTime.Clear(); sOpen.Clear(); sHigh.Clear();
                sLow.Clear(); sClose.Clear(); sVolume.Clear();

                // Обработка данных
                bool Filter = MySecurity.Market == "1";
                while (XR.Read())
                {
                    if (Filter && XR.HasAttributes && sDateTime.Count > 0 &&
                        sDateTime[^1].Date != DateTime.ParseExact(XR.GetAttribute("date"), DTForm, IC).Date &&
                        double.Parse(XR.GetAttribute("high"), IC) - double.Parse(XR.GetAttribute("low"), IC) < 0.00001) XR.Read();
                    if (XR.HasAttributes) // Чтение и запись данных
                    {
                        sDateTime.Add(DateTime.ParseExact(XR.GetAttribute("date"), DTForm, IC));
                        sOpen.Add(double.Parse(XR.GetAttribute("open"), IC));
                        sHigh.Add(double.Parse(XR.GetAttribute("high"), IC));
                        sLow.Add(double.Parse(XR.GetAttribute("low"), IC));
                        sClose.Add(double.Parse(XR.GetAttribute("close"), IC));
                        sVolume.Add(double.Parse(XR.GetAttribute("volume"), IC));
                    }
                    else if (XR.NodeType == XmlNodeType.EndElement)
                    {
                        // Оценка соответствия данных и их запись
                        if (MySecurity.SourceBars.DateTime == null) // Исходные данные отсутсвуют
                        {
                            MySecurity.SourceBars = new Bars(MySecurity.SourceBars.TF)
                            {
                                DateTime = sDateTime.ToArray(),
                                Open = sOpen.ToArray(),
                                High = sHigh.ToArray(),
                                Low = sLow.ToArray(),
                                Close = sClose.ToArray(),
                                Volume = sVolume.ToArray()
                            };
                        }
                        else if (sDateTime.Count == 0) // Новые данные отсутствуют
                        {
                            XR.Close();
                            return Section;
                        }
                        else if (MySecurity.SourceBars.DateTime[0] > sDateTime[^1]) // Исходные данные свежее полученных
                        {
                            MySecurity.SourceBars.DateTime = sDateTime.Concat(MySecurity.SourceBars.DateTime).ToArray();
                            MySecurity.SourceBars.Open = sOpen.Concat(MySecurity.SourceBars.Open).ToArray();
                            MySecurity.SourceBars.High = sHigh.Concat(MySecurity.SourceBars.High).ToArray();
                            MySecurity.SourceBars.Low = sLow.Concat(MySecurity.SourceBars.Low).ToArray();
                            MySecurity.SourceBars.Close = sClose.Concat(MySecurity.SourceBars.Close).ToArray();
                            MySecurity.SourceBars.Volume = sVolume.Concat(MySecurity.SourceBars.Volume).ToArray();
                        }
                        else if (MySecurity.SourceBars.DateTime[^1] <= sDateTime[^1]) // Полученные данные свежее исходных
                        {
                            // Поиск первого общего бара
                            int y = Array.FindIndex(MySecurity.SourceBars.DateTime, x => x == sDateTime[0]);
                            if (y == -1) y = Array.FindIndex(MySecurity.SourceBars.DateTime, x => x == sDateTime[1]);

                            // Проверка наличия общего бара
                            if (y > -1) // Есть общие бары
                            {
                                MySecurity.SourceBars.DateTime = MySecurity.SourceBars.DateTime[0..y].Concat(sDateTime).ToArray();
                                MySecurity.SourceBars.Open = MySecurity.SourceBars.Open[0..y].Concat(sOpen).ToArray();
                                MySecurity.SourceBars.High = MySecurity.SourceBars.High[0..y].Concat(sHigh).ToArray();
                                MySecurity.SourceBars.Low = MySecurity.SourceBars.Low[0..y].Concat(sLow).ToArray();
                                MySecurity.SourceBars.Close = MySecurity.SourceBars.Close[0..y].Concat(sClose).ToArray();
                                MySecurity.SourceBars.Volume = MySecurity.SourceBars.Volume[0..y].Concat(sVolume).ToArray();
                            }
                            else MySecurity.SourceBars = new Bars(MySecurity.SourceBars.TF) // Отсутствует общий бар
                            {
                                DateTime = sDateTime.ToArray(),
                                Open = sOpen.ToArray(),
                                High = sHigh.ToArray(),
                                Low = sLow.ToArray(),
                                Close = sClose.ToArray(),
                                Volume = sVolume.ToArray()
                            };
                        }

                        // Обработка данных
                        Task.Run(() => UpdateBars(Tools[i], MySecurity == Tools[i].BasicSecurity));

                        XR.Close();
                        return Section;
                    }
                }
                AddInfo("candles: Не найден EndElement");
                XR.Close();
                return Section;
            }
            // Базовые секции
            else if (Section == "orders") // Идентификаторы заявок: transactionid и oderno
            {
                while (XR.Read())
                {
                    if (!XR.HasAttributes && !XR.ReadToFollowing("order")) { XR.Close(); return Section; }

                    // Первичная идентификация
                    int TrID = int.Parse(XR.GetAttribute("transactionid"), IC);
                    MyOrders = Orders.ToArray();
                    if (MyOrders.Where(x => x.TrID == TrID).Count() > 1)
                    {
                        AddInfo("orders: Найдено несколько заявок с одинаковым TrID. Удаление лишних.", SendEmail: true);
                        Window.Dispatcher.Invoke(() =>
                        {
                            while (MyOrders.Where(x => x.TrID == TrID).Count() > 1)
                            {
                                Orders.Remove(MyOrders.First(x => x.TrID == TrID));
                                MyOrders = Orders.ToArray();
                            }
                        });
                    }

                    MyOrder = MyOrders.SingleOrDefault(x => x.TrID == TrID); // Проверка наличия заявки с данным TrID
                    if (MyOrder == null) MyOrder = new Order(TrID); // Создание новой заявки с данным TrID

                    // Вторичная идентификация
                    if (!XR.ReadToFollowing("orderno"))
                    {
                        AddInfo("orders: Не найден orderno заявки: " + MyOrder.TrID);
                        continue;
                    }
                    XR.Read();

                    if (XR.Value == "0") MyOrder.OrderNo = 0;
                    else if (MyOrders.SingleOrDefault(x => x.OrderNo == long.Parse(XR.Value, IC)) == null) MyOrder.OrderNo = long.Parse(XR.Value, IC);
                    else // Заявка с данным биржевым номером уже есть в коллекции, обновление TrID и её фиксация
                    {
                        MyOrders.Single(x => x.OrderNo == long.Parse(XR.Value, IC)).TrID = TrID;
                        MyOrder = MyOrders.Single(x => x.TrID == TrID); // Фиксация существующей заявки
                    }

                    if (!XR.ReadToFollowing("seccode"))
                    {
                        AddInfo("orders: Не найден seccode заявки: " + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); MyOrder.Seccode = XR.Value;

                    if (!XR.ReadToFollowing("status"))
                    {
                        AddInfo("orders: Не найден status заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); MyOrder.Status = XR.Value;

                    if (!XR.ReadToFollowing("buysell"))
                    {
                        AddInfo("orders: Не найден buysell заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); MyOrder.BuySell = XR.Value;

                    XR.Read(); XR.Read();
                    if (XR.Name == "time")
                    {
                        XR.Read(); MyOrder.Time = DateTime.ParseExact(XR.Value, DTForm, IC);
                        XR.Read(); XR.Read();
                        if (XR.Name == "accepttime")
                        {
                            XR.Read();
                            MyOrder.AcceptTime = DateTime.ParseExact(XR.Value, DTForm, IC);
                        }
                    }
                    else if (XR.Name == "accepttime")
                    {
                        XR.Read();
                        MyOrder.AcceptTime = DateTime.ParseExact(XR.Value, DTForm, IC);
                    }
                    else if (MyOrder.Status == "active" && MyOrder.OrderNo == 0) { }

                    if (!XR.ReadToFollowing("balance"))
                    {
                        AddInfo("orders: Не найден balance заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); MyOrder.Balance = int.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("price"))
                    {
                        AddInfo("orders: Не найдена price заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); MyOrder.Price = double.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("quantity"))
                    {
                        AddInfo("orders: Не найдено quantity заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); MyOrder.Quantity = int.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("withdrawtime"))
                    {
                        AddInfo("orders: Не найдено withdrawtime заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); if (XR.Value != "0") MyOrder.WithdrawTime = DateTime.ParseExact(XR.Value, DTForm, IC);

                    if (!XR.ReadToFollowing("condition"))
                    {
                        AddInfo("orders: Не найдено condition заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); MyOrder.Condition = XR.Value;

                    if (MyOrder.Condition != "None")
                    {
                        if (!XR.ReadToFollowing("conditionvalue"))
                        {
                            AddInfo("orders: Не найдено conditionvalue заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                            continue;
                        }
                        XR.Read(); MyOrder.ConditionValue = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("validafter"))
                        {
                            AddInfo("orders: Не найдено validafter заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                            continue;
                        }
                        XR.Read(); if (XR.Value != "0") MyOrder.ValidAfter = DateTime.ParseExact(XR.Value, DTForm, IC);

                        if (!XR.ReadToFollowing("validbefore"))
                        {
                            AddInfo("orders: Не найдено validbefore заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                            continue;
                        }
                        XR.Read(); if (XR.Value != "") MyOrder.ValidBefore = DateTime.ParseExact(XR.Value, DTForm, IC);
                    }
                    if (!XR.ReadToFollowing("result"))
                    {
                        AddInfo("orders: Не найден result заявки: " + MyOrder.Seccode + "/" + MyOrder.TrID);
                        continue;
                    }
                    XR.Read(); if (XR.HasValue)
                    {
                        if (XR.Value.StartsWith("{37}", SC) || XR.Value.StartsWith("{42}", SC))
                            AddInfo(MyOrder.Seccode + "/" + MyOrder.TrID + ": OrderReply: " + XR.Value, false);
                        else AddInfo(MyOrder.Seccode + "/" + MyOrder.TrID + ": OrderReply: " + XR.Value);
                    }

                    int i = Array.FindIndex(Orders.ToArray(), x => x.TrID == MyOrder.TrID);
                    Window.Dispatcher.Invoke(() =>
                    {
                        if (i > -1) Orders[i] = MyOrder;
                        else Orders.Add(MyOrder);
                    });
                }
            }
            else if (Section == "positions")
            {
                XR.Read();
                string Subsection = XR.Name, Value;
                while (XR.Read())
                {
                    if (Subsection is "sec_position" or "forts_position")
                    {
                        if (!XR.ReadToFollowing("seccode")) { XR.Close(); return Section; }
                        XR.Read(); Value = XR.Value;

                        if (Positions.SingleOrDefault(x => x.Seccode == Value) == null) Positions.Add(new Position(Value));
                        Position MyPosition = Positions.Single(x => x.Seccode == Value);

                        if (!XR.ReadToFollowing("market")) { AddInfo("Не найден market позиции"); continue; }
                        XR.Read(); MyPosition.Market = XR.Value;

                        if (Subsection == "forts_position")
                        {
                            if (!XR.ReadToFollowing("startnet")) { AddInfo("Не найден startnet позиции"); continue; }
                            XR.Read(); MyPosition.SaldoIn = int.Parse(XR.Value, IC);

                            if (!XR.ReadToFollowing("totalnet")) { AddInfo("Не найден totalnet позиции"); continue; }
                            XR.Read(); MyPosition.Saldo = int.Parse(XR.Value, IC);

                            if (!XR.ReadToFollowing("varmargin")) { AddInfo("Не найдена varmargin позиции"); continue; }
                            XR.Read(); MyPosition.PL = double.Parse(XR.Value, IC);
                        }
                        else
                        {
                            if (!XR.ReadToFollowing("saldoin")) { AddInfo("Не найдено saldoin позиции"); continue; }
                            XR.Read(); MyPosition.SaldoIn = int.Parse(XR.Value, IC);

                            if (!XR.ReadToFollowing("saldo")) { AddInfo("Не найдено saldo позиции"); continue; }
                            XR.Read(); MyPosition.Saldo = int.Parse(XR.Value, IC);

                            if (!XR.ReadToFollowing("amount")) { AddInfo("Не найдено amount позиции"); continue; }
                            XR.Read(); MyPosition.Amount = double.Parse(XR.Value, IC);

                            if (!XR.ReadToFollowing("equity")) { AddInfo("Не найдено equity позиции"); continue; }
                            XR.Read(); MyPosition.Equity = double.Parse(XR.Value, IC);
                        }
                    }
                    else if (Subsection == "united_limits")
                    {
                        if (!XR.ReadToFollowing("equity")) { XR.Close(); return Section; }
                        XR.Read(); Portfolio.Saldo = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("requirements")) { AddInfo("Нет requirements портфеля."); XR.Close(); return Section; }
                        XR.Read(); Portfolio.InitReqs = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("free")) { AddInfo("Нет free портфеля."); XR.Close(); return Section; }
                        XR.Read(); Portfolio.Free = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("vm")) { AddInfo("Нет vm портфеля."); XR.Close(); return Section; }
                        XR.Read(); Portfolio.VarMargin = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("finres")) { AddInfo("Нет finres портфеля."); XR.Close(); return Section; }
                        XR.Read(); Portfolio.FinRes = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("go")) { AddInfo("Нет go портфеля."); XR.Close(); return Section; }
                        XR.Read(); Portfolio.GO = double.Parse(XR.Value, IC);
                        XR.Close(); return Section;
                    }
                    else if (Subsection == "money_position")
                    {
                        if (!XR.ReadToFollowing("shortname")) { XR.Close(); return Section; }
                        XR.Read(); Value = XR.Value;
                        if (MoneyPositions.SingleOrDefault(x => x.ShortName == XR.Value) == null)
                            MoneyPositions.Add(new Position() { ShortName = Value });

                        if (!XR.ReadToFollowing("saldoin")) { AddInfo("Не найдено saldoin позиции"); continue; }
                        XR.Read(); MoneyPositions.Single(x => x.ShortName == Value).SaldoIn = double.Parse(XR.Value, IC);

                        if (!XR.ReadToFollowing("saldo")) { AddInfo("Не найдено saldo позиции"); continue; }
                        XR.Read(); MoneyPositions.Single(x => x.ShortName == Value).Saldo = double.Parse(XR.Value, IC);
                    }
                    else { AddInfo("Пришла неизвестная позиция:" + Subsection); XR.Close(); return Section; }
                }
            }
            else if (Section == "trades")
            {
                while (XR.Read())
                {
                    if (!XR.ReadToFollowing("tradeno")) { XR.Close(); return Section; }
                    XR.Read();

                    if (Trades.SingleOrDefault(x => x.TradeNo == long.Parse(XR.Value, IC)) != null) continue;
                    else MyTrade = new Trade(long.Parse(XR.Value, IC));

                    if (!XR.ReadToFollowing("orderno")) { AddInfo("Нет orderno моей сделки."); continue; }
                    XR.Read(); MyTrade.OrderNo = long.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("seccode")) { AddInfo("Нет seccode моей сделки."); continue; }
                    XR.Read(); MyTrade.Seccode = XR.Value;

                    if (!XR.ReadToFollowing("buysell")) { AddInfo("Нет buysell моей сделки."); continue; }
                    XR.Read(); MyTrade.BuySell = XR.Value;

                    if (!XR.ReadToFollowing("time")) { AddInfo("Нет time моей сделки."); continue; }
                    XR.Read(); MyTrade.DateTime = DateTime.ParseExact(XR.Value, DTForm, IC);

                    if (!XR.ReadToFollowing("price")) { AddInfo("Нет price моей сделки."); continue; }
                    XR.Read(); MyTrade.Price = double.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("quantity")) { AddInfo("Нет quantity моей сделки."); continue; }
                    XR.Read(); MyTrade.Quantity = int.Parse(XR.Value, IC);

                    Window.Dispatcher.Invoke(() => Trades.Add(MyTrade));
                    AddInfo("Новая сделка: " + MyTrade.Seccode + "/" + MyTrade.BuySell + "/" +
                        MyTrade.Price + "/" + MyTrade.Quantity, MySettings.DisplayNewTrades);
                }
            }
            else if (Section == "server_status")
            {
                if (XR.GetAttribute("connected") == "true")
                {
                    ServerAvailable = true;
                    if (XR.GetAttribute("recover") != "true")
                    {
                        Connection = ConnectionState.Connected;
                        AddInfo("Connected");
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
                        AddInfo("Disconnected");
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
                    XR.Read(); AddInfo("Server error: " + XR.Value + " BackupServer: " + !BackupServer, SendEmail: true);
                }
            }
            else if (Section == "mc_portfolio")
            {
                Portfolio.Union = XR.GetAttribute("union");
                if (!XR.ReadToFollowing("open_equity")) { AddInfo("Нет open_equity портфеля."); XR.Close(); return Section; }
                XR.Read(); Portfolio.SaldoIn = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("equity")) { AddInfo("Нет equity портфеля."); XR.Close(); return Section; }
                XR.Read(); Portfolio.Saldo = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("pl")) { AddInfo("Нет pl портфеля."); XR.Close(); return Section; }
                XR.Read(); Portfolio.PL = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("init_req")) { AddInfo("Нет init_req портфеля."); XR.Close(); return Section; }
                XR.Read(); Portfolio.InitReqs = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("maint_req")) { AddInfo("Нет maint_req портфеля."); XR.Close(); return Section; }
                XR.Read(); Portfolio.MinReqs = double.Parse(XR.Value, IC);

                if (!XR.ReadToFollowing("unrealized_pnl")) { AddInfo("Нет unrealized_pnl портфеля."); XR.Close(); return Section; }
                XR.Read(); Portfolio.UnrealPL = double.Parse(XR.Value, IC);

                string Value;
                while (XR.Read())
                {
                    if (!XR.ReadToFollowing("seccode")) { XR.Close(); return Section; }
                    XR.Read(); Value = XR.Value;
                    if (Positions.SingleOrDefault(x => x.Seccode == Value) == null) Positions.Add(new Position(Value));

                    if (!XR.ReadToFollowing("open_balance")) { AddInfo("Не найден open_balance позиции"); continue; }
                    XR.Read(); Positions.Single(x => x.Seccode == Value).SaldoIn = int.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("balance")) { AddInfo("Не найден balance позиции"); continue; }
                    XR.Read(); Positions.Single(x => x.Seccode == Value).Saldo = int.Parse(XR.Value, IC);

                    if (!XR.ReadToFollowing("pl")) { AddInfo("Нет pl позиции."); continue; }
                    XR.Read(); Positions.Single(x => x.Seccode == Value).PL = double.Parse(XR.Value, IC);
                }
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

                Task.Run(() => MyTaskTool.MySecurity.UpdateRequirements());
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
    #endregion

    #region Methods for sending commands
    public static void Connect()
    {
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
        string Result = ConnectorSendCommand(StartPart, EndPart, Window.Dispatcher.Invoke(() => Window.TxtPas.SecurePassword));
        GC.Collect();

        if (Result.StartsWith("<result success=\"true\"", SC) || Result.Contains("уже устанавливается")) return; // "уже установлено"
        else
        {
            Connection = ConnectionState.Disconnected;
            AddInfo(Result);
        }
    }
    public static void Disconnect()
    {
        SystemReadyToTrading = false;
        Connection = ConnectionState.Disconnecting;

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
        IScript Sender = null, string Note = null, bool UseCredit = false)
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
                Window.Dispatcher.Invoke(() =>
                {
                    if (Sender != null) Sender.MyOrders.Add(new Order(int.Parse(XR.GetAttribute("transactionid"), IC), Sender.Name, Signal, Note));
                    else SystemOrders.Add(new Order(int.Parse(XR.GetAttribute("transactionid"), IC), SenderName, Signal, Note));
                });

                AddInfo("SendOrder: Заявка принята: " + SenderName + "/" + Symbol.Seccode + "/" +
                    BuySell + "/" + Price + "/" + Quantity, MySettings.DisplaySentOrders);
                //if (Sender != null) Task.Run(() => Sender.UpdateOrdersAndPosition());

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
        IScript Sender = null, string Note = null, bool UseCredit = false)
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
        if (Result != "<result success=\"true\"/>") AddInfo("GetHistoryData: " + Result);
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
                        AddInfo("SendCommand: Бесконечное ожидание ответа сервера.", SendEmail: true);
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
