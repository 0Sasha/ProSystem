using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static ProSystem.TXmlConnector;
namespace ProSystem;

public partial class MainWindow : Window
{
    #region Fields
    // Индикаторы и единый поток проверки условий
    private static int CheckingInterval = 1;
    private static bool BackupAddressServer;
    private static DateTime TriggerRequestInfo;
    private static DateTime TriggerSerialization;
    private static DateTime TriggerRecalculation;
    private static DateTime TriggerUpdatingModels;
    private static DateTime TriggerCheckingPortfolio;
    private static DateTime TriggerCheckingCondition;
    private static ConnectionState ConnectionSt = ConnectionState.Disconnected;
    private static readonly System.Threading.Thread ThreadCheckingConditions = 
        new(CheckCondition) { IsBackground = true, Name = "CheckerCondition" };

    // Портфель, позиции и заявки
    public static readonly UnitedPortfolio Portfolio = new();
    public static readonly List<Position> Positions = new();
    public static readonly List<Position> MoneyPositions = new();
    public static readonly ObservableCollection<Order> Orders = new();
    public static readonly ObservableCollection<Order> SystemOrders = new();
    public static readonly ObservableCollection<Trade> SystemTrades = new();

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
                        Window.StCon.Fill = System.Windows.Media.Brushes.Green;
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
                        Window.StCon.Fill = System.Windows.Media.Brushes.Yellow;
                    }
                    else
                    {
                        Window.ConnectBtn.Content = "Connect";
                        Window.StCon.Fill =
                        ConnectionSt == ConnectionState.Disconnected ? System.Windows.Media.Brushes.WhiteSmoke : System.Windows.Media.Brushes.Red;
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
    public static DateTime TriggerNotification { get; set; }

    public static ObservableCollection<Tool> Tools { get; set; } = new();
    public static ObservableCollection<Trade> Trades { get; set; } = new();

    public static double USDRUB { get; set; }
    public static double EURRUB { get; set; }
    #endregion

    #region Core
    public MainWindow()
    {
        InitializeComponent();
        Window = this;
        Logger.StartLogging();

        // Подписка на необработанные исключения
        AppDomain CurrentDomain = AppDomain.CurrentDomain;
        CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(WriteLogUnhandledException);
        TaskScheduler.UnobservedTaskException += new EventHandler<UnobservedTaskExceptionEventArgs>(WriteLogTaskException);

        // Восстановление и проверка настроек
        DeserializeData();
        MySettings.CheckSettings();

        // Привязка данных и восстановление вкладок инструментов
        BindData();
        RestoreToolTabs();
        Orders.CollectionChanged += UpdateOrders;
        Trades.CollectionChanged += UpdateTrades;
        Tools.CollectionChanged += UpdateTools;

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
        if (!Directory.Exists("Data")) return;

        var Formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
        try
        {
            using Stream MyStream = new FileStream("Data/Settings.bin", FileMode.Open, FileAccess.Read, FileShare.Read);
            MySettings = (Settings)Formatter.Deserialize(MyStream);
        }
        catch (Exception e) { AddInfo("Исключение десериализации Settings." + e.Message); } // Settings
        try
        {
            using Stream MyStream = new FileStream("Data/Tools.bin", FileMode.Open, FileAccess.Read, FileShare.Read);
            Tools = new ObservableCollection<Tool>((IEnumerable<Tool>)Formatter.Deserialize(MyStream));
        }
        catch (Exception e) { AddInfo("Исключение десериализации Tools." + e.Message); } // Tools
        try
        {
            using Stream MyStream = new FileStream("Data/Trades.bin", FileMode.Open, FileAccess.Read, FileShare.Read);
            Trades = new ObservableCollection<Trade>((IEnumerable<Trade>)Formatter.Deserialize(MyStream));
        }
        catch (Exception e) { AddInfo("Исключение десериализации Trades." + e.Message); } // Trades
        try
        {
            string Info = File.ReadAllText("Data/Info.txt");
            if (Info != "") TxtBox.Text = "Начало восстановленного фрагмента.\n" + Info + "\nКонец восстановленного фрагмента.";
        }
        catch (Exception e) { AddInfo("Исключение чтения Info." + e.Message); } // Info
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

        AverageEquityTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("AverageValueEquity"), Mode = BindingMode.OneWay });
        ToleranceEquityTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ToleranceEquity"), Mode = BindingMode.TwoWay });
        TolerancePositionTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("TolerancePosition"), Mode = BindingMode.TwoWay });

        OptShareBaseBalTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("OptShareBaseBalances"), Mode = BindingMode.TwoWay });
        ToleranceBaseBalTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ToleranceBaseBalances"), Mode = BindingMode.TwoWay });

        MaxShareInitReqsPositionTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("MaxShareInitReqsPosition"), Mode = BindingMode.TwoWay });
        MaxShareInitReqsToolTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("MaxShareInitReqsTool"), Mode = BindingMode.TwoWay });
        MaxShareInitReqsPortfolioTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("MaxShareInitReqsPortfolio"), Mode = BindingMode.TwoWay });
        MaxShareMinReqsPortfolioTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("MaxShareMinReqsPortfolio"), Mode = BindingMode.TwoWay });

        ShelflifeTradesTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ShelfLifeTrades"), Mode = BindingMode.TwoWay });
        ShelflifeOrdersScriptsTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ShelfLifeOrdersScripts"), Mode = BindingMode.TwoWay });
        ShelflifeTradesScriptsTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("ShelfLifeTradesScripts"), Mode = BindingMode.TwoWay });


        OrdersView.ItemsSource = Orders;
        TradesView.ItemsSource = Trades;
        ToolsView.ItemsSource = Tools;
        ComboBoxTool.ItemsSource = Tools;
        ComboBoxDistrib.ItemsSource = new string[] { "All tools", "First part", "Second part" };
        ComboBoxDistrib.SelectedIndex = 0;
        BoxConnectors.ItemsSource = MyConnectors;
        BoxConnectors.SelectedIndex = 0;
        ToolsByPriorityView.ItemsSource = MySettings.ToolsByPriority;
        ComboBox.ItemsSource = new string[] { "SendEmail", "Test" };
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
            TabsTools.Items.Add(new TabItem()
            {
                Header = Tools[i].Name,
                Width = 48,
                Height = 18,
                Content = GetGridTabTool(Tools[i])
            });
            Tools[i].Initialize(TabsTools.Items[i] as TabItem);
        }
    }

    private void PrepareToTrading()
    {
        if (SystemReadyToTrading)
        {
            GetPortfolio(Clients[0].Union);
            for (int i = 0; i < Tools.Count; i++) RequestBars(Tools[i]);
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

            RequestBars(Tools[i]);
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
                                UpdateModels(MyTool);
                            }
                        }
                    }
                    else if (DateTime.Now > TriggerUpdatingModels)
                    {
                        TriggerUpdatingModels = DateTime.Now.AddSeconds(MySettings.ModelUpdateInterval);
                        foreach (Tool MyTool in Tools) if (MyTool.Active) UpdateModels(MyTool);
                    }

                    // Запрос информации
                    if (DateTime.Now > TriggerRequestInfo) Task.Run(RequestInfo);

                    // Переподключение по расписанию
                    if (MySettings.ScheduledConnection && DateTime.Now.Minute == 50)
                    {
                        if (DateTime.Now.Hour == 18 && DateTime.Now.Second < 3 || DateTime.Now < DateTime.Today.AddMinutes(400))
                        {
                            Task TaskDisconnect = Task.Run(() => Disconnect());
                            if (!TaskDisconnect.Wait(300000)) AddInfo("CheckConditions: Превышено время ожидания TaskDisconnect.", SendEmail: true);
                        }
                    }
                }
                else if (Connection == ConnectionState.Connecting)
                {
                    if (DateTime.Now > TriggerReconnection)
                    {
                        AddInfo("Переподключение по таймауту.");
                        Task TaskDisconnect = Task.Run(() => Disconnect());
                        if (!TaskDisconnect.Wait(300000)) AddInfo("CheckConditions: Превышено время ожидания TaskDisconnect.", SendEmail: true);
                        else if (!MySettings.ScheduledConnection)
                        {
                            TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
                            Task TaskConnect = Task.Run(() => Connect());
                            if (!TaskConnect.Wait(300000)) AddInfo("CheckConditions: Превышено время ожидания TaskConnect.", SendEmail: true);
                        }
                    }
                }
                else if (DateTime.Now > DateTime.Today.AddMinutes(400))
                {
                    if (MySettings.ScheduledConnection && (ServerAvailable || DateTime.Now > TriggerReconnection) &&
                        Window.Dispatcher.Invoke(() => Window.TxtLog.Text.Length > 0 && Window.TxtPas.SecurePassword.Length > 0))
                    {
                        TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
                        Task TaskConnect = Task.Run(() => Connect());
                        if (!TaskConnect.Wait(300000)) AddInfo("CheckConditions: Превышено время ожидания TaskConnect.", SendEmail: true);
                    }
                    else if (!MySettings.ScheduledConnection && DateTime.Now.Minute is 0 or 30)
                        MySettings.ScheduledConnection = true;
                }
                else if (DateTime.Now.Hour == 1 && DateTime.Now.Minute == 0 && DateTime.Now.Second < 2)
                {
                    Logger.StopLogging();
                    Logger.StartLogging();
                    BackupServer = false;

                    MySettings.LastValueEquity = (DateTime.Today.AddDays(-1), (int)Portfolio.Saldo);
                    int Range = MySettings.AverageValueEquity / 100 * MySettings.ToleranceEquity;
                    if (Portfolio.Saldo < MySettings.AverageValueEquity - Range ||
                        Portfolio.Saldo > MySettings.AverageValueEquity + Range)
                        AddInfo("Стоимость портфеля за пределами допустимого отклонения.", SendEmail: true);
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

        int UpperBorder = MySettings.AverageValueEquity + MySettings.AverageValueEquity / 100 * MySettings.ToleranceEquity;
        int LowerBorder = MySettings.AverageValueEquity - MySettings.AverageValueEquity / 100 * MySettings.ToleranceEquity;
        if (Portfolio.Saldo < LowerBorder || Portfolio.Saldo > UpperBorder)
        {
            AddInfo("CheckPortfolio: Стоимость портфеля за пределами допустимого отклонения.", SendEmail: true);
            return;
        }
        try
        {
            Tool[] MyTools = Tools.ToArray();
            foreach (Position MyPosition in Positions.ToArray())
            {
                if ((int)MyPosition.Saldo != 0 &&
                    MyTools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == MyPosition.Seccode) == null)
                    AddInfo("CheckPortfolio: обнаружен независимый актив: " + MyPosition.Seccode, SendEmail: true);
            }

            double MaxMinReqs = Portfolio.Saldo / 100 * MySettings.MaxShareMinReqsPortfolio;
            double MaxInitReqs = Portfolio.Saldo / 100 * MySettings.MaxShareInitReqsPortfolio;
            if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs)
            {
                CheckReqsTools();
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
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2).ToString(IC) + "%", SendEmail: true);
            if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }

            // Поиск и закрытие неизвестных позиций, независящих от активных инструментов
            foreach (Position MyPosition in Positions.ToArray())
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
            foreach (Position MyPosition in Positions.ToArray())
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
                    Position MyPosition = Positions.ToArray().SingleOrDefault(x => x.Seccode == MyTool.MySecurity.Seccode);
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
    private static void CheckReqsTools()
    {
        double maxReqs = 0;
        double maxReqsBaseBalance = 0;
        foreach (var tool in Tools.ToArray())
        {
            if (tool.Active)
            {
                if (tool.TradeShare) maxReqs += Portfolio.Saldo / 100 * tool.ShareOfFunds;
                else maxReqs += tool.NumberOfLots * Math.Max(tool.MySecurity.InitReqLong, tool.MySecurity.InitReqShort);
                
                if (tool.UseShiftBalance)
                {
                    maxReqsBaseBalance += tool.BaseBalance *
                        (tool.MySecurity.LastTrade.Price / tool.MySecurity.MinStep * tool.MySecurity.MinStepCost);
                    maxReqs += Math.Abs(tool.BaseBalance) * Math.Max(tool.MySecurity.InitReqLong, tool.MySecurity.InitReqShort);
                }
            }
        }

        double curShare = Math.Round(maxReqsBaseBalance / Portfolio.Saldo * 100, 2);
        Window.Dispatcher.Invoke(() => Window.CurShareBaseBalTxt.Text = curShare.ToString());

        if (curShare > MySettings.OptShareBaseBalances + MySettings.ToleranceBaseBalances ||
            curShare < MySettings.OptShareBaseBalances - MySettings.ToleranceBaseBalances)
            AddInfo("CheckReqsTools: Доля базовых активов за пределами допустимого отклонения: " + curShare + "%", SendEmail: true);

        if (maxReqs > Portfolio.Saldo / 100 * MySettings.MaxShareInitReqsPortfolio)
            AddInfo("CheckReqsTools: Потенциальные требования портфеля превышают норму: " +
                MySettings.MaxShareInitReqsPortfolio.ToString(IC) + "%. PotentialInitReqs: " +
                Math.Round(maxReqs / (Portfolio.Saldo / 100), 2) + "%", SendEmail: true);
    }
    private static void RequestInfo()
    {
        TriggerRequestInfo = DateTime.Now.AddMinutes(95 - DateTime.Now.Minute).AddSeconds(-DateTime.Now.Second);

        GetPortfolio(Clients[0].Union);
        foreach (Tool MyTool in Tools.ToArray())
        {
            GetClnSecPermissions(MyTool.MySecurity.Board, MyTool.MySecurity.Seccode, MyTool.MySecurity.Market);
            if (!MyTool.Active) RequestBars(MyTool);
        }
    }

    public static void RequestBars(Tool MyTool)
    {
        string IdTF;
        if (TimeFrames.Count > 0) IdTF = TimeFrames.Last(x => x.Period / 60 <= MyTool.BaseTF).ID;
        else { AddInfo("RequestBars: пустой массив таймфреймов."); return; }

        int Count = 25;
        if (MyTool.BasicSecurity != null)
        {
            if (MyTool.BasicSecurity.SourceBars == null || MyTool.BasicSecurity.SourceBars.Close.Length < 500 ||
                MyTool.BasicSecurity.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
                MyTool.BaseTF != MyTool.BasicSecurity.SourceBars.TF && MyTool.BaseTF != MyTool.BasicSecurity.Bars.TF) Count = 10000;

            GetHistoryData(MyTool.BasicSecurity.Board, MyTool.BasicSecurity.Seccode, IdTF, Count);
        }

        Count = 25;
        if (MyTool.MySecurity.SourceBars == null || MyTool.MySecurity.SourceBars.Close.Length < 500 ||
            MyTool.MySecurity.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
            MyTool.BaseTF != MyTool.MySecurity.SourceBars.TF && MyTool.BaseTF != MyTool.MySecurity.Bars.TF) Count = 10000;

        GetHistoryData(MyTool.MySecurity.Board, MyTool.MySecurity.Seccode, IdTF, Count);
    }
    public static void UpdateBars(Tool MyTool, bool UpdateBasicSecurity)
    {
        if (UpdateBasicSecurity)
        {
            if (MyTool.BasicSecurity.SourceBars.TF == MyTool.BaseTF) MyTool.BasicSecurity.Bars = MyTool.BasicSecurity.SourceBars;
            else MyTool.BasicSecurity.Bars = Bars.Compress(MyTool.BasicSecurity.SourceBars, MyTool.BaseTF);
        }
        else
        {
            if (MyTool.MySecurity.SourceBars.TF == MyTool.BaseTF) MyTool.MySecurity.Bars = MyTool.MySecurity.SourceBars;
            else MyTool.MySecurity.Bars = Bars.Compress(MyTool.MySecurity.SourceBars, MyTool.BaseTF);
        }
    }

    private static void UpdateModels(Tool MyTool)
    {
        try
        {
            MyTool.UpdateModel();
            if (MyTool.Model != null) MyTool.UpdateMiniModel();
        }
        catch (Exception e)
        {
            AddInfo("UpdateModels: " + MyTool.Name + ": Исключение: " + e.Message);
            AddInfo("Трассировка стека: " + e.StackTrace);
            if (e.InnerException != null)
            {
                AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
            }
        }
    }
    #endregion
}
