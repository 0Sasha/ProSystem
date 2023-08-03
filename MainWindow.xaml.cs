using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProSystem.Services;

namespace ProSystem;

public partial class MainWindow : Window
{
    #region Fields
    // Индикаторы и единый поток проверки условий
    private static int CheckingInterval = 1;
    private static DateTime TriggerRecalculation;
    private static DateTime TriggerUpdatingModels;
    private static DateTime TriggerCheckingPortfolio;
    private static DateTime TriggerCheckingCondition;
    private readonly System.Threading.Thread ThreadCheckingConditions;

    // Прочие поля
    public static readonly string[] MyConnectors = new string[] { "TXmlConnector" };
    public static System.Globalization.CultureInfo IC { get; } = System.Globalization.CultureInfo.InvariantCulture;
    #endregion

    #region Properties
    public static MainWindow Window { get; private set; }
    public TradingSystem TradingSystem { get; private set; }
    public Connector Connector { get => TradingSystem.Connector; }
    public ObservableCollection<Tool> Tools { get => TradingSystem.Tools; }

    public static ISerializer Serializer { get; set; }
    public static INotifier Notifier { get; set; }
    public static IScriptManager ScriptManager { get; set; }
    public static IPortfolioManager PortfolioManager { get; set; }
    public static IToolManager ToolManager { get; set; }
    public static UnitedPortfolio Portfolio { get; set; } = new();
    public static Settings MySettings { get; set; } = new();
    #endregion
    
    public MainWindow()
    {
        Window = this;
        InitializeComponent();
        Logger.StartLogging(true);
        ThreadCheckingConditions = new(CheckCondition) { IsBackground = true, Name = "CheckerCondition" };

        // Восстановление данных и проверка настроек
        Serializer = new DCSerializer("Data", (info) => AddInfo(info, true, true));
        DeserializeData();

        MySettings.Check(Tools);
        if (MySettings.EmailPassword != null) Notifier = new EmailNotifier(587,
            "smtp.gmail.com", MySettings.Email, MySettings.EmailPassword, (info) => AddInfo(info));
        PortfolioManager = new PortfolioManager(Portfolio, (info) => AddInfo(info, true, true));
        ScriptManager = new ScriptManager(Window, CancelOrder, (info) => AddInfo(info));
        ToolManager = new ToolManager(this, ScriptManager);

        // Привязка данных и восстановление вкладок инструментов
        BindData();
        RestoreToolTabs();
        Portfolio.PropertyChanged += UpdatePortfolio;
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
        try { MySettings = (Settings)Serializer.Deserialize("Settings", MySettings.GetType()); }
        catch (Exception ex) { AddInfo("Serializer: " + ex.Message); }

        try { Tools = new((IEnumerable<Tool>)Serializer.Deserialize("Tools", Tools.GetType())); }
        catch (Exception ex) { AddInfo("Serializer: " + ex.Message); }

        try { Portfolio = (UnitedPortfolio)Serializer.Deserialize("Portfolio", Portfolio.GetType()); }
        catch (Exception ex) { AddInfo("Serializer: " + ex.Message); }

        try { Trades = new((IEnumerable<Trade>)Serializer.Deserialize("Trades", Trades.GetType())); }
        catch (Exception ex) { AddInfo("Serializer: " + ex.Message); }

        if (File.Exists(Serializer.DataDirectory + "/Info.txt"))
        {
            try
            {
                string Info = File.ReadAllText(Serializer.DataDirectory + "/Info.txt");
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
            Source = MySettings,
            Path = new PropertyPath("ToleranceEquity"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        TolerancePositionTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = MySettings, Path = new PropertyPath("TolerancePosition"), Mode = BindingMode.TwoWay });

        OptShareBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings,
            Path = new PropertyPath("OptShareBaseAssets"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        ToleranceBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings,
            Path = new PropertyPath("ToleranceBaseAssets"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });

        MaxShareInitReqsPositionTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings,
            Path = new PropertyPath("MaxShareInitReqsPosition"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareInitReqsToolTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings,
            Path = new PropertyPath("MaxShareInitReqsTool"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareInitReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings,
            Path = new PropertyPath("MaxShareInitReqsPortfolio"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareMinReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = MySettings,
            Path = new PropertyPath("MaxShareMinReqsPortfolio"),
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
            ToolManager.CreateTab(Tools[i]);
        }
    }

    private async Task CheckPortfolio()
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
                PortfolioManager.UpdateShares(MyTools);
                PortfolioManager.CheckShares(MySettings);
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
                            if (MyTool.Active) await ToolManager.ChangeActivityAsync(MyTool);
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
                    if (MyTool.Active) await ToolManager.ChangeActivityAsync(MyTool);
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

    
    
    public void AddInfo(string data, bool important = true, bool notify = false)
    {
        Logger.WriteLogSystem(data);
        if (important)
        {
            Window.Dispatcher.Invoke(() =>
            {
                Window.TxtBox.AppendText("\n" + DateTime.Now.ToString("dd.MM HH:mm:ss", IC) + ": " + data);
                Window.TxtBox.ScrollToEnd();
            });
        }
        if (notify) Notifier.Notify(data);
    }


    #region Menu
    private bool SaveData(bool SaveInfoPanel = false)
    {
        AddInfo("SaveData: Сериализация", false);
        try
        {
            Serializer.Serialize(TradingSystem.Tools, "Tools");
            Serializer.Serialize(TradingSystem.Portfolio, "Portfolio");
            Serializer.Serialize(TradingSystem.Settings, "Settings");
            Serializer.Serialize(TradingSystem.Trades, "Trades");
            if (SaveInfoPanel) Dispatcher.Invoke(() => File.WriteAllText(Serializer.DataDirectory + "/Info.txt", TxtBox.Text));
            return true;
        }
        catch (Exception ex) { AddInfo("SaveData: " + ex.Message); return false; }
    }
    private void SaveData(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            if (SaveData(true)) AddInfo("Данные сохранены.");
            else AddInfo("Данные не сохранены.");
        });
    }
    private void SaveData(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "InitReqs") Task.Run(() => SaveData());
    }
    private void ShowUsedMemory(object sender, RoutedEventArgs e) =>
        AddInfo("Использование памяти: " + (GC.GetTotalMemory(false) / 1000000).ToString(IC) + " МБ");
    private void ClearMemory(object sender, RoutedEventArgs e) => GC.Collect();
    private void TakeLoginDetails(object sender, RoutedEventArgs e)
    {
        if (!File.Exists("Data/mail.txt"))
        {
            AddInfo("mail.txt не найден");
            return;
        }
        try
        {
            string[] details = File.ReadAllLines("Data/mail.txt");
            MySettings.Email = details[0];
            MySettings.EmailPassword = details[1];
            (Notifier as EmailNotifier).Email = MySettings.Email;
            (Notifier as EmailNotifier).Password = MySettings.EmailPassword;
            Notifier.Notify("Test notify");
            AddInfo("Тестовое уведомление отправлено.");
        }
        catch (Exception ex) { AddInfo("TakeLoginDetails: " + ex.Message); }
    }
    private void ResizeControlPanel(object sender, RoutedEventArgs e)
    {
        if (TabsTools.SelectedIndex > -1)
        {
            ColumnDefinition Column = (TabsTools.SelectedContent as Grid).ColumnDefinitions[1];
            if (Column.ActualWidth < 200) Column.Width = new GridLength(200);
            else Column.Width = new GridLength(100);
        }
    }
    private void Test(object sender, RoutedEventArgs e)
    {

    }
    #endregion

    #region ListViewies
    private void UpdatePortfolio(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Positions")
        {
            Window.Dispatcher.Invoke(() =>
            {
                Window.PortfolioView.ItemsSource = Portfolio.AllPositions;
                Window.PortfolioView.ScrollIntoView(this);
            });
        }
    }
    private void UpdateTools(object sender, NotifyCollectionChangedEventArgs e)
    {
        ToolsView.Items.Refresh();
        Task.Run(() => SaveData());
    }
    private void UpdateOrders(object sender, NotifyCollectionChangedEventArgs e)
    {
        OrdersView.Items.Refresh();
        if (OrdersView.Items.Count > 0) OrdersView.ScrollIntoView(OrdersView.Items[^1]);
    }
    private void UpdateTrades(object sender, NotifyCollectionChangedEventArgs e)
    {
        TradesView.Items.Refresh();
        if (TradesView.Items.Count > 0) TradesView.ScrollIntoView(TradesView.Items[^1]);
    }
    #endregion

    #region Context menu
    private void AddToolContext(object sender, RoutedEventArgs e)
    {
        NewTool NewTool = new(this);
        NewTool.Show();
    }
    private void OpenTabToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            TabsTools.SelectedIndex = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            MainTabs.SelectedIndex = 3;
        }
    }
    private async void ChangeToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            if (Tools[i].Active) await ToolManager.ChangeActivityAsync(Tools[i]);
            NewTool NewTool = new(this, Tools[i]);
            NewTool.Show();
        }
    }
    private void UpdateToolbarContext(object sender, RoutedEventArgs e) => ToolsView.Items.Refresh();
    private async void ReloadBarsToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            MessageBoxResult Res = MessageBox.Show("Are you sure you want to reload bars of " + Tools[i].Name + "?",
                "Reloading bars", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (Res == MessageBoxResult.No) return;
            await ToolManager.ReloadBarsAsync(Tools[i]);
        }
    }
    private void WriteSourceBarsToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            Tools[i].MySecurity.SourceBars.Write(Tools[i].MySecurity.ShortName);
            Tools[i].BasicSecurity?.SourceBars.Write(Tools[i].BasicSecurity.ShortName);
        }
    }
    private async void RemoveToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            // Подтверждение удаления инструмента
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            MessageBoxResult Res =
                MessageBox.Show("Are you sure you want to remove " + Tools[i].Name + "?", "Removing", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (Res == MessageBoxResult.No) return;

            // Деактивация инструмента и удаление его вкладки
            string ToolName = Tools[i].Name;
            if (Tools[i].Active) await ToolManager.ChangeActivityAsync(Tools[i]);
            TabsTools.Items.Remove(TabsTools.Items[i]);

            // Удаление инструмента
            MySettings.ToolsByPriority.Remove(Tools[i].Name);
            ToolsByPriorityView.Items.Refresh();
            if (Tools.Remove(Tools[i])) AddInfo("Удалён инструмент: " + ToolName);
            else AddInfo("Не получилось удалить инструмент: " + ToolName);
        }
    }

    private void ChangePriorityTool(object sender, RoutedEventArgs e)
    {
        string header = (string)(sender as MenuItem).Header;
        int i = ToolsByPriorityView.SelectedIndex;
        int n = int.Parse(header[^1].ToString());

        if (header.StartsWith("Raise") && i >= n)
        {
            while (n > 0)
            {
                (MySettings.ToolsByPriority[i], MySettings.ToolsByPriority[i - 1]) =
                    (MySettings.ToolsByPriority[i - 1], MySettings.ToolsByPriority[i]);
                n--;
                i--;
            }
        }
        else if (header.StartsWith("Downgrade") && i > -1 && i < MySettings.ToolsByPriority.Count - n)
        {
            while (n > 0)
            {
                (MySettings.ToolsByPriority[i], MySettings.ToolsByPriority[i + 1]) =
                    (MySettings.ToolsByPriority[i + 1], MySettings.ToolsByPriority[i]);
                n--;
                i++;
            }
        }
        ToolsByPriorityView.Items.Refresh();
    }

    private async void CancelOrderContext(object sender, RoutedEventArgs e)
    {
        if (TradingSystem.Connector.Connection == ConnectionState.Connected && OrdersView.SelectedItem != null)
        {
            Order SelectedOrder = (Order)OrdersView.SelectedItem;
            if (SelectedOrder.Status is "active" or "watching") await Task.Run(() =>
            {
                if (!TradingSystem.Connector.CancelOrder(SelectedOrder)) AddInfo("Ошибка отмены заявки.");
            });
            else AddInfo("Заявка не активна.");
        }
    }
    private void RemoveOrderContext(object sender, RoutedEventArgs e)
    {
        if ((MainTabs.SelectedItem as TabItem).Header.ToString() == "Portfolio")
        {
            if (OrdersView.SelectedItem != null)
            {
                if (!TradingSystem.Orders.Remove((Order)OrdersView.SelectedItem)) AddInfo("Не найдена заявка для удаления.");
            }
        }
        else if (OrdersInfo.SelectedItem != null)
        {
            Order MyOrder = (Order)OrdersInfo.SelectedItem;
            if (MyOrder.Sender == "System") TradingSystem.SystemOrders.Remove(MyOrder);
            else if (MyOrder.Seccode != null)
            {
                try
                {
                    Tool MyTool = Tools.Single(x => x.MySecurity.Seccode == MyOrder.Seccode);
                    if (MyOrder.Sender != null) MyTool.Scripts.Single(x => x.Name == MyOrder.Sender).Orders.Remove(MyOrder);
                    else
                    {
                        foreach (Script MyScript in MyTool.Scripts)
                        {
                            if (MyScript.Orders.Contains(MyOrder))
                            {
                                MyScript.Orders.Remove(MyOrder);
                                return;
                            }
                        }
                        AddInfo("Не найдена заявка для удаления.");
                    }
                }
                catch (Exception ex) { AddInfo("Исключение во время попытки удаления заявки: " + ex.Message); }
            }
            else
            {
                foreach (Tool MyTool in Tools)
                {
                    if (MyTool.Scripts.SingleOrDefault(x => x.Name == MyOrder.Sender) != null)
                    {
                        if (MyTool.Scripts.Single(x => x.Name == MyOrder.Sender).Orders.Remove(MyOrder)) return;
                        else break;
                    }
                }
                AddInfo("Не найдена заявка для удаления.");
            }
        }
    }
    private void RemoveTradeContext(object sender, RoutedEventArgs e)
    {
        if ((MainTabs.SelectedItem as TabItem).Header.ToString() == "Portfolio")
        {
            if (TradesView.SelectedItem != null)
            {
                if (!TradingSystem.Trades.Remove((Trade)TradesView.SelectedItem)) AddInfo("Не найдена сделка для удаления.");
            }
        }
        else if (TradesInfo.SelectedItem != null)
        {
            Trade MyTrade = (Trade)TradesInfo.SelectedItem;
            if (MyTrade.SenderOrder == "System") TradingSystem.SystemTrades.Remove(MyTrade);
            else if (MyTrade.Seccode != null)
            {
                try
                {
                    Tools.Single(x => x.MySecurity.Seccode == MyTrade.Seccode).Scripts.Single
                        (x => x.Name == MyTrade.SenderOrder).Trades.Remove(MyTrade);
                }
                catch (Exception ex) { AddInfo("Исключение во время попытки удаления сделки: " + ex.Message); }
            }
            else
            {
                foreach (Tool MyTool in Tools)
                {
                    if (MyTool.Scripts.SingleOrDefault(x => x.Name == MyTrade.SenderOrder) != null)
                    {
                        if (MyTool.Scripts.Single(x => x.Name == MyTrade.SenderOrder).Trades.Remove(MyTrade)) return;
                        else break;
                    }
                }
                AddInfo("Не найдена сделка для удаления.");
            }
        }
    }
    private void UpdatePortfolioViewContext(object sender, RoutedEventArgs e)
    {
        OrdersView.Items.Refresh();
        TradesView.Items.Refresh();
        Task.Run(() => PortfolioManager.UpdatePositions());
    }

    private void ClearInfo(object sender, RoutedEventArgs e) => TxtBox.Clear();
    #endregion

    #region View
    private async void ChangeСonnection(object sender, RoutedEventArgs e)
    {
        if (Connector.Connection is ConnectionState.Connected or ConnectionState.Connecting)
            await Task.Run(() => Connector.Disconnect(false));
        else if (TxtLog.Text.Length > 0 && TxtPas.SecurePassword.Length > 0)
        {
            Connector.TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
            await Task.Run(() => Connector.Connect(false));
        }
        else AddInfo("Введите логин и пароль.");
    }
    private void ClosingMainWindow(object sender, CancelEventArgs e)
    {
        var Res = MessageBox.Show("Are you sure you want to exit?",
            "Closing", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (Res == MessageBoxResult.No || !SaveData(true))
        {
            e.Cancel = true;
            return;
        }

        CheckingInterval = 1000;
        System.Threading.Thread.Sleep(50);
        if (Connector.Connection != ConnectionState.Disconnected)
            Connector.Disconnect(true);

        if (!Connector.Initialized || Connector.Uninitialize()) Logger.StopLogging();
        else MessageBox.Show("UnInitialization failed.");
    }
    internal async void ChangeActivityTool(object sender, RoutedEventArgs e)
    {
        Button MyButton = sender as Button;
        MyButton.IsEnabled = false;
        if (MyButton.Content == null) await ToolManager.ChangeActivityAsync(MyButton.DataContext as Tool);
        else await ToolManager.ChangeActivityAsync(Tools[TabsTools.SelectedIndex]);
        MyButton.IsEnabled = true;
    }
    private void ComboBoxToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0) ComboBoxScript.ItemsSource = (e.AddedItems[0] as Tool).Scripts;
    }

    private void ShowScriptInfo(object sender, RoutedEventArgs e)
    {
        if (ComboBoxTool.SelectedIndex > -1 && ComboBoxScript.SelectedIndex > -1)
        {
            OrdersInfo.ItemsSource = (ComboBoxTool.SelectedItem as Tool)
                .Scripts.SingleOrDefault(x => x == (Script)ComboBoxScript.SelectedItem).Orders;
            OrdersInfo.Items.Refresh();

            TradesInfo.ItemsSource = (ComboBoxTool.SelectedItem as Tool)
                .Scripts.SingleOrDefault(x => x == (Script)ComboBoxScript.SelectedItem).Trades;
            TradesInfo.Items.Refresh();
        }
    }
    private void ShowSystemInfo(object sender, RoutedEventArgs e)
    {
        OrdersInfo.ItemsSource = TradingSystem.SystemOrders;
        OrdersInfo.Items.Refresh();

        TradesInfo.ItemsSource = TradingSystem.SystemTrades;
        TradesInfo.Items.Refresh();
    }
    internal void ShowDistributionInfo(object sender, RoutedEventArgs e)
    {
        if (Tools.Count < 1 || Portfolio.Saldo < 1 || Portfolio.Positions == null) return;

        DistributionPlot.Model = Tools.GetPlot(Portfolio.Positions, Portfolio.Saldo,
            (string)ComboBoxDistrib.SelectedItem, (bool)OnlyPosCheckBox.IsChecked,
            (bool)ExcludeBaseCheckBox.IsChecked);
        DistributionPlot.Controller ??= PlotExtensions.GetController();

        PortfolioPlot.Model = Portfolio.GetPlot();
        PortfolioPlot.Controller ??= PlotExtensions.GetController();
    }

    private void ResizeInfoPanel(object sender, RoutedEventArgs e)
    {
        if ((int)RowInfo.Height.Value == 2) RowInfo.Height = new GridLength(0.5, GridUnitType.Star);
        else if ((int)RowInfo.Height.Value == 1) RowInfo.Height = new GridLength(2, GridUnitType.Star);
        else RowInfo.Height = new GridLength(1, GridUnitType.Star);
    }
    #endregion
}
