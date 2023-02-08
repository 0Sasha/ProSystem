using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ProSystem.Services;
using static ProSystem.TXmlConnector;
namespace ProSystem;

public partial class MainWindow : Window
{
    #region Fields
    // Индикаторы и единый поток проверки условий
    private static int CheckingInterval = 1;
    private static bool BackupAddressServer;
    private static DateTime TriggerRequestInfo;
    private static DateTime TriggerRecalculation;
    private static DateTime TriggerUpdatingModels;
    private static DateTime TriggerCheckingPortfolio;
    private static DateTime TriggerCheckingCondition;
    private static ConnectionState ConnectionSt = ConnectionState.Disconnected;
    private static readonly System.Threading.Thread ThreadCheckingConditions = 
        new(CheckCondition) { IsBackground = true, Name = "CheckerCondition" };

    // Доступные активы, рынки, таймфреймы и клиентские счета
    public static readonly List<Security> AllSecurities = new();
    public static readonly List<Market> Markets = new();
    public static readonly List<TimeFrame> TimeFrames = new();
    public static readonly List<ClientAccount> Clients = new();

    // Прочие поля
    public static readonly int[] MyTimeFrames = new int[] { 1, 5, 15, 30, 60, 120, 240, 360, 720 };
    public static readonly string[] MyConnectors = new string[] { "TXmlConnector" };
    public static readonly string[] MyAlgorithms = new string[]
    {
        "RSI", "StochRSI", "MFI", "DeMarker", "Stochastic",
        "CMO", "CMF", "RVI", "CCI", "DPO", "FRC", "OBV", "AD", "SumLine",
        "CHO", "ROC", "MACD", "MA", "Channel", "CrossEMA",
        "ATRS", "PARS"
    };
    public static readonly StringComparison SC = StringComparison.Ordinal;
    public static readonly System.Globalization.CultureInfo IC = System.Globalization.CultureInfo.InvariantCulture;
    #endregion

    #region Properties
    public static MainWindow Window { get; private set; }
    public static Serializer MySerializer { get; set; } = new DCSerializer("Data", (info) => AddInfo(info, true, true));
    public static UnitedPortfolio Portfolio { get; set; } = new();
    public static Settings MySettings { get; set; } = new();
    public static bool ConnectorInitialized { get; set; }
    public static ConnectionState Connection
    {
        get => ConnectionSt;
        set
        {
            if (ConnectionSt != value)
            {
                ConnectionSt = value;
                if (ConnectionSt == ConnectionState.Connected)
                {
                    Window.Dispatcher.Invoke(() =>
                    {
                        Window.ConnectBtn.Content = "Disconnect";
                        Window.StCon.Fill = Colors.Green;
                    });
                    Task.Run(() =>
                    {
                        Window.PrepareToTrading();
                        Window.Dispatcher.Invoke(() => Window.ShowDistributionInfo(null, null));
                    });
                }
                else Window.Dispatcher.Invoke(() =>
                {
                    if (ConnectionSt == ConnectionState.Connecting)
                    {
                        Window.ConnectBtn.Content = "Disconnect";
                        Window.StCon.Fill = Colors.Orange;
                    }
                    else
                    {
                        Window.ConnectBtn.Content = "Connect";
                        Window.StCon.Fill =
                        ConnectionSt == ConnectionState.Disconnected ? Colors.Gray : Colors.Red;
                    }
                });
            }
        }
    }
    public static bool BackupServer
    {
        get => BackupAddressServer;
        set
        {
            BackupAddressServer = value;
            Window.Dispatcher.Invoke(() => Window.BackupServerCheck.IsChecked = value);
        }
    }
    public static bool SystemReadyToTrading { get; set; }
    public static bool ServerAvailable { get; set; }
    public static DateTime TriggerReconnection { get; set; } = DateTime.Now.AddMinutes(3);

    public static ObservableCollection<Tool> Tools { get; set; } = new();
    public static ObservableCollection<Trade> Trades { get; set; } = new();
    public static ObservableCollection<Order> Orders { get; } = new();
    public static ObservableCollection<Order> SystemOrders { get; } = new();
    public static ObservableCollection<Trade> SystemTrades { get; } = new();

    public static double USDRUB { get; set; }
    public static double EURRUB { get; set; }
    #endregion

    #region Launch
    public MainWindow()
    {
        InitializeComponent();
        Window = this;
        Logger.StartLogging(true);

        // Восстановление данных и проверка настроек
        DeserializeData();
        MySettings.Check(Tools);

        // Привязка данных и восстановление вкладок инструментов
        BindData();
        RestoreToolTabs();
        Portfolio.PropertyChanged += SaveData;
        Tools.CollectionChanged += UpdateTools;
        Orders.CollectionChanged += UpdateOrders;
        Trades.CollectionChanged += UpdateTrades;

        // Инициализация коннектора и запуск единого потока обработки входящих данных
        if (File.Exists("txmlconnector64.dll"))
        {
            ConnectorSetCallback();
            if (ConnectorInitialize(MySettings.LogLevelConnector)) ConnectorInitialized = true;
        }
        else AddInfo("Не найден коннектор txmlconnector64.dll");

        // Запуск единого потока проверки состояния системы
        ThreadCheckingConditions.Start();
    }
    private void DeserializeData()
    {
        try
        {
            MySettings = (Settings)MySerializer.Deserialize("Settings", MySettings.GetType());
            Tools = new ObservableCollection<Tool>((IEnumerable<Tool>)MySerializer.Deserialize("Tools", Tools.GetType()));
            Portfolio = (UnitedPortfolio)MySerializer.Deserialize("Portfolio", Portfolio.GetType());
            Trades = new ObservableCollection<Trade>((IEnumerable<Trade>)MySerializer.Deserialize("Trades", Trades.GetType()));
        }
        catch (Exception ex) { AddInfo("Serializer: " + ex.Message); }
        if (File.Exists(MySerializer.DataDirectory + "/Info.txt"))
        {
            try
            {
                string Info = File.ReadAllText(MySerializer.DataDirectory + "/Info.txt");
                if (Info != "") TxtBox.Text = "Начало восстановленного фрагмента.\n" + Info + "\nКонец восстановленного фрагмента.";
            }
            catch (Exception ex) { AddInfo("Исключение чтения Info." + ex.Message); }
        }
    }
    private void BindData()
    {
        IntervalUpdateTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ModelUpdateInterval"), Mode = BindingMode.TwoWay });
        IntervalRecalcTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("RecalcInterval"), Mode = BindingMode.TwoWay });
        ScheduleCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ScheduledConnection"), Mode = BindingMode.TwoWay });

        DisplaySentOrdersCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("DisplaySentOrders"), Mode = BindingMode.TwoWay });
        DisplayNewTradesCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("DisplayNewTrades"), Mode = BindingMode.TwoWay });
        DisplayMessagesCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("DisplayMessages"), Mode = BindingMode.TwoWay });
        DisplaySpecialInfoCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("DisplaySpecialInfo"), Mode = BindingMode.TwoWay });

        TxtLog.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("LoginConnector"), Mode = BindingMode.TwoWay });
        ConnectorLogLevelTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("LogLevelConnector"), Mode = BindingMode.TwoWay });
        RequestTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("RequestTM"), Mode = BindingMode.TwoWay });
        SessionTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("SessionTM"), Mode = BindingMode.TwoWay });

        AverageEquityTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = Portfolio,
            Path = new PropertyPath("AverageEquity"),
            Mode = BindingMode.OneWay,
            StringFormat = "### ### ### УЕ"
        });
        CurShareInitReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = Portfolio,
            Path = new PropertyPath("ShareInitReqs"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareInitReqsBaseTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = Portfolio,
            Path = new PropertyPath("ShareInitReqsBaseAssets"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        PotShareInitReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = Portfolio,
            Path = new PropertyPath("PotentialShareInitReqs"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareMinReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = Portfolio,
            Path = new PropertyPath("ShareMinReqs"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareBaseAssetsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = Portfolio,
            Path = new PropertyPath("ShareBaseAssets"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });


        ToleranceEquityTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings, Path = new PropertyPath("ToleranceEquity"),
            Mode = BindingMode.TwoWay, StringFormat = "#'%'"
        });
        TolerancePositionTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("TolerancePosition"), Mode = BindingMode.TwoWay });

        OptShareBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings, Path = new PropertyPath("OptShareBaseAssets"),
            Mode = BindingMode.TwoWay, StringFormat = "#'%'"
        });
        ToleranceBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings, Path = new PropertyPath("ToleranceBaseAssets"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });

        MaxShareInitReqsPositionTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings, Path = new PropertyPath("MaxShareInitReqsPosition"),
            Mode = BindingMode.TwoWay, StringFormat = "#'%'"
        });
        MaxShareInitReqsToolTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings, Path = new PropertyPath("MaxShareInitReqsTool"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareInitReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings, Path = new PropertyPath("MaxShareInitReqsPortfolio"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareMinReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings, Path = new PropertyPath("MaxShareMinReqsPortfolio"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });

        ShelflifeTradesTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ShelfLifeTrades"), Mode = BindingMode.TwoWay });
        ShelflifeOrdersScriptsTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ShelfLifeOrdersScripts"), Mode = BindingMode.TwoWay });
        ShelflifeTradesScriptsTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ShelfLifeTradesScripts"), Mode = BindingMode.TwoWay });


        ToolsView.ItemsSource = Tools;
        OrdersView.ItemsSource = Orders;
        TradesView.ItemsSource = Trades;
        PortfolioView.ItemsSource = Portfolio.AllPositions;
        ComboBoxTool.ItemsSource = Tools;
        ComboBoxDistrib.ItemsSource = new string[] { "All tools", "First part", "Second part" };
        ComboBoxDistrib.SelectedIndex = 1;
        BoxConnectors.ItemsSource = MyConnectors;
        BoxConnectors.SelectedIndex = 0;
        ToolsByPriorityView.ItemsSource = MySettings.ToolsByPriority;
    }
    private void RestoreToolTabs()
    {
        for (int i = 0; i < Tools.Count; i++)
        {
            if (MySettings.ToolsByPriority[i] != Tools[i].Name)
            {
                Tool SourceTool = Tools[i];
                int x = Array.FindIndex(Tools.ToArray(), x => x.Name == MySettings.ToolsByPriority[i]);
                Tools[i] = Tools[x];
                Tools[x] = SourceTool;
            }
            TabsTools.Items.Add(GetTabItem(Tools[i]));
            Tools[i].Initialize(TabsTools.Items[i] as TabItem);
        }
    }
    #endregion

    #region Core
    private void PrepareToTrading()
    {
        if (SystemReadyToTrading)
        {
            GetPortfolio(Clients[0].Union);
            for (int i = 0; i < Tools.Count; i++) Tools[i].RequestBars();
            AddInfo("PrepareToTrading: Bars updated.", false);
            return;
        }

        // Запрос информации
        RequestInfo();
        GetHistoryData("CETS", "USD000UTSTOM", "1", 1);
        GetHistoryData("CETS", "EUR_RUB__TOM", "1", 1);
        for (int i = 0, x; i < Tools.Count; i++)
        {
            if (Connection != ConnectionState.Connected) return;
            for (int j = 0; j < Tools[i].Scripts.Length; j++)
            {
                for (int k = 0; k < Tools[i].Scripts[j].MyOrders.Count; k++)
                {
                    if (Tools[i].Scripts[j].MyOrders[k].DateTime.Date >= DateTime.Now.Date.AddDays(-3) ||
                        Tools[i].Scripts[j].MyOrders[k].Status == "active" || Tools[i].Scripts[j].MyOrders[k].Status == "watching")
                    {
                        // Поиск заявки скрипта в коллекции всех заявок торговой сессии
                        if (Tools[i].Scripts[j].MyOrders[k].OrderNo == 0)
                            x = Array.FindIndex(Orders.ToArray(), x => x.TrID == Tools[i].Scripts[j].MyOrders[k].TrID);
                        else x = Array.FindIndex(Orders.ToArray(), x => x.OrderNo == Tools[i].Scripts[j].MyOrders[k].OrderNo);

                        // Обновление свойств заявки из коллекции всех заявок и приведение обеих заявок к одному объекту
                        if (x > -1)
                        {
                            if (Orders[x].Status == Tools[i].Scripts[j].MyOrders[k].Status)
                                Orders[x].DateTime = Tools[i].Scripts[j].MyOrders[k].DateTime;
                            Orders[x].Sender = Tools[i].Scripts[j].MyOrders[k].Sender;
                            Orders[x].Signal = Tools[i].Scripts[j].MyOrders[k].Signal;
                            Orders[x].Note = Tools[i].Scripts[j].MyOrders[k].Note;
                            Dispatcher.Invoke(() => Tools[i].Scripts[j].MyOrders[k] = Orders[x]);
                        }
                        else if (Tools[i].Scripts[j].MyOrders[k].Status == "watching" || Tools[i].Scripts[j].MyOrders[k].Status == "active" &&
                            DateTime.Today.DayOfWeek != DayOfWeek.Saturday && DateTime.Today.DayOfWeek != DayOfWeek.Sunday)
                        {
                            Tools[i].Scripts[j].MyOrders[k].Status = "lost";
                            Tools[i].Scripts[j].MyOrders[k].DateTime = DateTime.Now.AddDays(-2);
                            AddInfo("PrepareToTrading: " + Tools[i].Scripts[j].Name + ": Активная заявка не актуальна. Статус обновлён.");
                        }
                    }
                }
            }

            if (Tools[i].MySecurity.Bars == null || Tools[i].BasicSecurity != null && Tools[i].BasicSecurity.Bars == null)
            {
                Tools[i].Active = false;
                AddInfo("PrepareToTrading: Инструмент деактивирован, потому что не пришли бары. Попробуйте активировать позже.");
            }
            else if (Tools[i].Active)
            {
                SubUnsub(true, Tools[i].MySecurity.Board, Tools[i].MySecurity.Seccode);
                if (Tools[i].BasicSecurity != null) SubUnsub(true, Tools[i].BasicSecurity.Board, Tools[i].BasicSecurity.Seccode);
            }

            Tools[i].RequestBars();
            GetSecurityInfo(Tools[i].MySecurity.Market, Tools[i].MySecurity.Seccode);
        }

        // Очистка устаревших данных
        if (DateTime.Now < DateTime.Today.AddHours(7)) ClearOutdatedData();
        System.Threading.Thread.Sleep(1000);

        if (Connection != ConnectionState.Connected) return;
        SystemReadyToTrading = true;
        AddInfo("PrepareToTrading: SystemReadyToTrading.", false);
    }
    private static void CheckCondition()
    {
        while (true)
        {
            if (DateTime.Now > TriggerCheckingCondition)
            {
                TriggerCheckingCondition = DateTime.Now.AddSeconds(CheckingInterval);

                if (Connection == ConnectionState.Connected)
                {
                    // Проверка требований единого портфеля
                    if (SystemReadyToTrading && DateTime.Now > TriggerCheckingPortfolio) CheckPortfolio();

                    // Пересчёт скриптов и обновление графиков
                    if (SystemReadyToTrading && DateTime.Now > TriggerRecalculation)
                    {
                        TriggerRecalculation = DateTime.Now.AddSeconds(MySettings.RecalcInterval);
                        if (TriggerRecalculation.Second < 10) TriggerRecalculation = TriggerRecalculation.AddSeconds(10);
                        else if (TriggerRecalculation.Second > 55) TriggerRecalculation = TriggerRecalculation.AddSeconds(-4);

                        TriggerUpdatingModels = DateTime.Now.AddSeconds(MySettings.ModelUpdateInterval);
                        foreach (Tool MyTool in Tools)
                        {
                            if (MyTool.Active)
                            {
                                if (DateTime.Now > MyTool.TimeNextRecalc) MyTool.Calculate();
                                MyTool.UpdateView(false);
                            }
                        }
                    }
                    else if (DateTime.Now > TriggerUpdatingModels)
                    {
                        TriggerUpdatingModels = DateTime.Now.AddSeconds(MySettings.ModelUpdateInterval);
                        foreach (Tool MyTool in Tools) if (MyTool.Active) MyTool.UpdateView(false);
                    }

                    // Запрос информации
                    if (DateTime.Now > TriggerRequestInfo) Task.Run(RequestInfo);

                    // Переподключение по расписанию
                    if (MySettings.ScheduledConnection && DateTime.Now.Minute == 50)
                    {
                        if (DateTime.Now < DateTime.Today.AddMinutes(400)) //DateTime.Now.Hour == 18 && DateTime.Now.Second < 3
                        {
                            Task TaskDisconnect = Task.Run(() => Disconnect(true));
                            if (!TaskDisconnect.Wait(300000))
                                AddInfo("CheckConditions: Превышено время ожидания TaskDisconnect.", notify: true);
                        }
                    }
                }
                else if (Connection == ConnectionState.Connecting)
                {
                    if (DateTime.Now > TriggerReconnection)
                    {
                        AddInfo("Переподключение по таймауту.");
                        Task TaskDisconnect = Task.Run(() => Disconnect());
                        if (!TaskDisconnect.Wait(300000)) AddInfo("CheckConditions: Превышено время ожидания TaskDisconnect.", notify: true);
                        else if (!MySettings.ScheduledConnection)
                        {
                            TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
                            Task TaskConnect = Task.Run(() => Connect());
                            if (!TaskConnect.Wait(300000)) AddInfo("CheckConditions: Превышено время ожидания TaskConnect.", notify: true);
                        }
                    }
                }
                else if (DateTime.Now > DateTime.Today.AddMinutes(400))
                {
                    if (MySettings.ScheduledConnection && (ServerAvailable || DateTime.Now > TriggerReconnection) &&
                        Window.Dispatcher.Invoke(() => Window.TxtLog.Text.Length > 0 && Window.TxtPas.SecurePassword.Length > 0))
                    {
                        TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
                        bool scheduled = DateTime.Now.Minute == 40 && DateTime.Now.Hour == 6;
                        Task TaskConnect = Task.Run(() => Connect(scheduled));
                        if (!TaskConnect.Wait(300000)) AddInfo("CheckConditions: Превышено время ожидания TaskConnect.", notify: true);
                    }
                    else if (!MySettings.ScheduledConnection && DateTime.Now.Minute is 0 or 30)
                        MySettings.ScheduledConnection = true;
                }
                else if (DateTime.Now.Hour == 1 && DateTime.Now.Minute == 0 && DateTime.Now.Second < 15)
                {
                    BackupServer = false;
                    Task.Run(() =>
                    {
                        Logger.StopLogging();
                        Logger.StartLogging();
                        Portfolio.UpdateEquity(DateTime.Today.AddDays(-1));
                        Portfolio.CheckEquity(MySettings.ToleranceEquity);
                        Portfolio.ClearOldPositions();
                        _ = ArchiveFiles("Logs/Transaq",
                            DateTime.Now.AddDays(-1).ToString("yyyyMMdd"), DateTime.Now.AddDays(-1).ToString("yyyyMMdd") + " archive", true);
                        _ = ArchiveFiles("Data", ".xml", "Data", false);
                    });
                    System.Threading.Thread.Sleep(18000);
                }
            }
            else System.Threading.Thread.Sleep(10);
        }
    }

    private static void CheckPortfolio()
    {
        bool CancelActiveOrders(string Seccode)
        {
            Order[] ActiveOrders = Orders.ToArray().Where(x => x.Seccode == Seccode && x.Status is "active" or "watching").ToArray();
            if (ActiveOrders.Length == 0) return true;
            else
            {
                foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);
                System.Threading.Thread.Sleep(500);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

                System.Threading.Thread.Sleep(1000);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

                System.Threading.Thread.Sleep(1500);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

                System.Threading.Thread.Sleep(2000);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;
                return false;
            }
        }
        bool ClosePositionByMarket(Security Symbol, Position MyPosition)
        {
            if (SendOrder(Symbol, OrderType.Market,
                (int)MyPosition.Saldo < 0, 100, (int)Math.Abs(MyPosition.Saldo), "ClosingPositionByMarket"))
            {
                System.Threading.Thread.Sleep(500);
                if ((int)MyPosition.Saldo != 0)
                {
                    System.Threading.Thread.Sleep(1000);
                    if ((int)MyPosition.Saldo != 0) System.Threading.Thread.Sleep(1500);
                    if ((int)MyPosition.Saldo != 0) System.Threading.Thread.Sleep(2000);
                    if ((int)MyPosition.Saldo != 0) System.Threading.Thread.Sleep(5000);
                }
                if ((int)MyPosition.Saldo == 0) return true;
                else AddInfo("CheckPortfolio: Заявка отправлена, но позиция всё ещё не закрыта: " + Symbol.Seccode);
            }
            return false;
        }

        TriggerCheckingPortfolio = DateTime.Now.AddSeconds(330 - DateTime.Now.Second);
        if (DateTime.Now > DateTime.Today.AddMinutes(840) && DateTime.Now < DateTime.Today.AddMinutes(845)) return;

        int UpperBorder = Portfolio.AverageEquity + Portfolio.AverageEquity / 100 * MySettings.ToleranceEquity;
        int LowerBorder = Portfolio.AverageEquity - Portfolio.AverageEquity / 100 * MySettings.ToleranceEquity;
        if (Portfolio.Saldo < LowerBorder || Portfolio.Saldo > UpperBorder)
        {
            AddInfo("CheckPortfolio: Стоимость портфеля за пределами допустимого отклонения.", notify: true);
            return;
        }
        try
        {
            Tool[] MyTools = Tools.ToArray();
            foreach (Position MyPosition in Portfolio.Positions.ToArray())
            {
                if ((int)MyPosition.Saldo != 0 &&
                    MyTools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == MyPosition.Seccode) == null)
                    AddInfo("CheckPortfolio: обнаружен независимый актив: " + MyPosition.Seccode, notify: true);
            }

            double MaxMinReqs = Portfolio.Saldo / 100 * MySettings.MaxShareMinReqsPortfolio;
            double MaxInitReqs = Portfolio.Saldo / 100 * MySettings.MaxShareInitReqsPortfolio;
            if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs)
            {
                Portfolio.UpdateSharesAndCheck(MyTools, MySettings);
                return;
            }

            // Запрос информации
            GetPortfolio(Clients[0].Union);
            System.Threading.Thread.Sleep(5000);

            // Повторная проверка объёма требований
            MaxMinReqs = Portfolio.Saldo / 100 * MySettings.MaxShareMinReqsPortfolio;
            MaxInitReqs = Portfolio.Saldo / 100 * MySettings.MaxShareInitReqsPortfolio;
            if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;

            // Балансировка портфеля
            AddInfo("CheckPortfolio: Требования портфеля превысили нормы: " +
                MySettings.MaxShareMinReqsPortfolio.ToString(IC) + "%/" +
                MySettings.MaxShareInitReqsPortfolio.ToString(IC) + "% MinReqs/InitReqs: " +
                Math.Round(Portfolio.MinReqs / Portfolio.Saldo * 100, 2).ToString(IC) + "%/" +
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2).ToString(IC) + "%", notify: true);
            if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }

            // Поиск и закрытие неизвестных позиций, независящих от активных инструментов
            foreach (Position MyPosition in Portfolio.Positions.ToArray())
            {
                if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }
                if ((int)MyPosition.Saldo != 0 &&
                    MyTools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == MyPosition.Seccode) == null)
                {
                    // Отмена соответствующих заявок
                    if (!CancelActiveOrders(MyPosition.Seccode))
                    {
                        AddInfo("CheckPortfolio: Не удалось отменить заявки перед закрытием неизвестной позиции: " + 
                            MyPosition.Seccode);
                        continue;
                    }

                    // Закрытие неизвестной позиции по рынку
                    Security Symbol = AllSecurities.Single(x => x.Seccode == MyPosition.Seccode && x.Market == MyPosition.Market);
                    if (ClosePositionByMarket(Symbol, MyPosition))
                    {
                        CancelActiveOrders(MyPosition.Seccode);
                        AddInfo("CheckPortfolio: Закрыта неизвестная позиция: " + MyPosition.Seccode);
                        System.Threading.Thread.Sleep(2000);

                        GetPortfolio(Clients[0].Union);
                        System.Threading.Thread.Sleep(3000);
                        if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;
                    }
                    else AddInfo("CheckPortfolio: Не удалось закрыть неизвестную позицию: " + MyPosition.Seccode);
                }
            }

            // Проверка объёмов открытых позиций активных инструментов
            double MaxShare = Portfolio.Saldo / 100 * MySettings.MaxShareInitReqsTool;
            foreach (Position MyPosition in Portfolio.Positions.ToArray())
            {
                if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }
                if ((int)MyPosition.Saldo != 0)
                {
                    Tool MyTool = MyTools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == MyPosition.Seccode);
                    if (MyTool == null) continue;

                    bool Long = (int)MyPosition.Saldo > 0;
                    int Vol = (int)Math.Abs(MyPosition.Saldo);
                    if (Long && Vol * MyTool.MySecurity.InitReqLong > MaxShare ||
                        !Long && Vol * MyTool.MySecurity.InitReqShort > MaxShare)
                    {
                        AddInfo("CheckPortfolio: Позиция превышает MaxShareInitReqsTool: " + MyPosition.Seccode);

                        // Отмена соответствующих заявок
                        if (!CancelActiveOrders(MyPosition.Seccode))
                        {
                            AddInfo("CheckPortfolio: Не удалось отменить заявки перед закрытием позиции.");
                            continue;
                        }

                        // Приостановка торговли, закрытие позиции по рынку и отключение инструмента
                        bool SourceStopTrading = MyTool.StopTrading;
                        MyTool.StopTrading = true;
                        if (ClosePositionByMarket(MyTool.MySecurity, MyPosition))
                        {
                            // Проверка отсутствия соответствующих заявок
                            if (!CancelActiveOrders(MyPosition.Seccode))
                            {
                                AddInfo("CheckPortfolio: Позиция закрыта, но не удалось отменить заявки. Инструмент не отключен.");
                                continue;
                            }

                            // Отключение инструмента
                            if (MyTool.Active) Window.Dispatcher.Invoke(() => MyTool.ChangeActivity());
                            AddInfo("CheckPortfolio: Позиция закрыта, заявок нет. Инструмент отключен: " + MyTool.Name);
                            System.Threading.Thread.Sleep(2000);

                            // Проверка требований портфеля
                            GetPortfolio(Clients[0].Union);
                            System.Threading.Thread.Sleep(5000);
                            if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;
                        }
                        else
                        {
                            MyTool.StopTrading = SourceStopTrading;
                            AddInfo("CheckPortfolio: Не удалось закрыть позицию по рынку.");
                        }
                    }
                }
            }

            // Отключение наименее приоритетных активных инструментов
            for (int i = MySettings.ToolsByPriority.Count - 1; i > 0; i--)
            {
                GetPortfolio(Clients[0].Union);
                System.Threading.Thread.Sleep(5000);
                MaxMinReqs = Portfolio.Saldo / 100 * MySettings.MaxShareMinReqsPortfolio;
                MaxInitReqs = Portfolio.Saldo / 100 * MySettings.MaxShareInitReqsPortfolio;
                if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;
                if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }

                Tool MyTool = MyTools.SingleOrDefault(x => x.Active && x.Name == MySettings.ToolsByPriority[i]);
                if (MyTool != null)
                {
                    AddInfo("CheckPortfolio: Отключение наименее приоритетного инструмента: " + MyTool.Name);

                    // Отмена соответствующих заявок
                    if (!CancelActiveOrders(MyTool.MySecurity.Seccode))
                    {
                        AddInfo("CheckPortfolio: Не удалось отменить заявки наименее приоритетного активного инструмента.");
                        continue;
                    }

                    // Закрытие позиции по рынку, если она существует
                    bool SourceStopTrading = MyTool.StopTrading;
                    MyTool.StopTrading = true;
                    Position MyPosition = Portfolio.Positions.ToArray().SingleOrDefault(x => x.Seccode == MyTool.MySecurity.Seccode);
                    if (MyPosition != null && (int)MyPosition.Saldo != 0)
                    {
                        if (ClosePositionByMarket(MyTool.MySecurity, MyPosition))
                        {
                            if (!CancelActiveOrders(MyPosition.Seccode))
                            {
                                AddInfo("CheckPortfolio: Позиция закрыта, но не удалось отменить заявки. Инструмент активен.");
                                continue;
                            }
                        }
                        else
                        {
                            AddInfo("CheckPortfolio: Не удалось закрыть позицию наименее приоритетного активного инструмента. Инструмент активен.");
                            MyTool.StopTrading = SourceStopTrading;
                            continue;
                        }
                    }

                    // Отключение инструмента
                    if (MyTool.Active) Window.Dispatcher.Invoke(() => MyTool.ChangeActivity());
                    AddInfo("CheckPortfolio: Позиция закрыта, заявок нет. Наименее приоритетный инструмент отключен: " + MyTool.Name);
                }
            }
        }
        catch (Exception e)
        {
            AddInfo("CheckPortfolio исключение: " + e.Message);
            AddInfo("Трассировка стека: " + e.StackTrace);
            if (e.InnerException != null)
            {
                AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
            }
        }
    }
    private static void RequestInfo()
    {
        TriggerRequestInfo = DateTime.Now.AddMinutes(95 - DateTime.Now.Minute).AddSeconds(-DateTime.Now.Second);

        GetPortfolio(Clients[0].Union);
        foreach (Tool MyTool in Tools.ToArray())
        {
            GetClnSecPermissions(MyTool.MySecurity.Board, MyTool.MySecurity.Seccode, MyTool.MySecurity.Market);
            if (!MyTool.Active) MyTool.RequestBars();
        }
    }
    #endregion
}
