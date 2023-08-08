using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProSystem.Services;

namespace ProSystem;

public partial class MainWindow : Window
{
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;
    
    public static MainWindow Window { get; private set; }
    public ISerializer Serializer { get; set; }
    public INotifier Notifier { get; set; }

    public TradingSystem TradingSystem { get; private set; }
    public Connector Connector { get => TradingSystem.Connector; }
    public Settings Settings { get => TradingSystem.Settings; }
    public UnitedPortfolio Portfolio { get => TradingSystem.Portfolio; }
    public ObservableCollection<Tool> Tools { get => TradingSystem.Tools; }
    public IToolManager ToolManager { get => TradingSystem.ToolManager; }

    public MainWindow()
    {
        Window = this;
        InitializeComponent();
        Logger.StartLogging(true);

        Serializer = new DCSerializer("Data", (info) => AddInfo(info, true, true));
        RestoreInfo();

        TradingSystem = new(this, typeof(TXmlConnector), GetPortfolio(), GetSettings(), GetTools(), GetTrades());

        Settings.Check(Tools);
        if (Settings.EmailPassword != null) Notifier = new EmailNotifier(587,
            "smtp.gmail.com", Settings.Email, Settings.EmailPassword, (info) => AddInfo(info));

        BindData();
        RestoreToolTabs();

        TradingSystem.Start();
    }

    public async Task RelogAsync()
    {
        Logger.StopLogging();
        Logger.StartLogging();
        await Logger.ArchiveFiles("Logs/Transaq", DateTime.Now.AddDays(-1).ToString("yyyyMMdd"),
            DateTime.Now.AddDays(-1).ToString("yyyyMMdd") + " archive", true);
        await Logger.ArchiveFiles("Data", ".xml", "Data", false);
    }

    private Settings GetSettings()
    {
        try
        {
            return (Settings)Serializer.Deserialize("Settings", typeof(Settings));
        }
        catch (Exception ex)
        {
            AddInfo("Serializer: " + ex.Message);
            return new Settings();
        }
    }

    private Tool[] GetTools()
    {
        try
        {
            return (Tool[])Serializer.Deserialize("Tools", typeof(Tool[]));
        }
        catch (Exception ex)
        {
            AddInfo("Serializer: " + ex.Message);
            return Array.Empty<Tool>();
        }
    }

    private UnitedPortfolio GetPortfolio()
    {
        try
        {
            return (UnitedPortfolio)Serializer.Deserialize("Portfolio", typeof(UnitedPortfolio));
        }
        catch (Exception ex)
        {
            AddInfo("Serializer: " + ex.Message);
            return new();
        }
    }

    private Trade[] GetTrades()
    {
        try
        {
            return (Trade[])Serializer.Deserialize("Trades", typeof(Trade[]));
        }
        catch (Exception ex)
        {
            AddInfo("Serializer: " + ex.Message);
            return Array.Empty<Trade>();
        }
    }

    private void RestoreInfo()
    {
        if (File.Exists(Serializer.DataDirectory + "/Info.txt"))
        {
            try
            {
                var info = File.ReadAllText(Serializer.DataDirectory + "/Info.txt");
                if (info != "") TxtBox.Text = "Начало фрагмента.\n" + info + "\nКонец фрагмента.";
            }
            catch (Exception ex) { AddInfo("Исключение чтения Info." + ex.Message); }
        }
    }

    private void BindData()
    {
        BindData(TradingSystem.Settings);
        BindData(TradingSystem.Portfolio);

        ToolsView.ItemsSource = TradingSystem.Tools;
        OrdersView.ItemsSource = TradingSystem.Orders;
        TradesView.ItemsSource = TradingSystem.Trades;
        PortfolioView.ItemsSource = TradingSystem.Portfolio.AllPositions;
        ToolsByPriorityView.ItemsSource = TradingSystem.Settings.ToolsByPriority;

        ComboBoxTool.ItemsSource = TradingSystem.Tools;
        ComboBoxDistrib.ItemsSource = new string[] { "All tools", "First part", "Second part" };
        ComboBoxDistrib.SelectedIndex = 1;
        BoxConnectors.ItemsSource = new string[] { nameof(TXmlConnector) };
        BoxConnectors.SelectedIndex = 0;

        TradingSystem.Portfolio.PropertyChanged += UpdatePortfolio;
        TradingSystem.Portfolio.PropertyChanged += SaveData;
        TradingSystem.Tools.CollectionChanged += UpdateTools;
        TradingSystem.Orders.CollectionChanged += UpdateOrders;
        TradingSystem.Trades.CollectionChanged += UpdateTrades;
        Connector.PropertyChanged += UpdateConnection;
        foreach (var tool in TradingSystem.Tools) tool.PropertyChanged += UpdateTool;
    }

    private void BindData(Settings settings)
    {
        IntervalUpdateTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("ModelUpdateInterval"), Mode = BindingMode.TwoWay });
        IntervalRecalcTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("RecalcInterval"), Mode = BindingMode.TwoWay });
        ScheduleCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = settings, Path = new PropertyPath("ScheduledConnection"), Mode = BindingMode.TwoWay });

        DisplaySentOrdersCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = settings, Path = new PropertyPath("DisplaySentOrders"), Mode = BindingMode.TwoWay });
        DisplayNewTradesCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = settings, Path = new PropertyPath("DisplayNewTrades"), Mode = BindingMode.TwoWay });
        DisplayMessagesCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = settings, Path = new PropertyPath("DisplayMessages"), Mode = BindingMode.TwoWay });
        DisplaySpecialInfoCheck.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new Binding() { Source = settings, Path = new PropertyPath("DisplaySpecialInfo"), Mode = BindingMode.TwoWay });

        TxtLog.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("LoginConnector"), Mode = BindingMode.TwoWay });
        ConnectorLogLevelTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("LogLevelConnector"), Mode = BindingMode.TwoWay });
        RequestTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("RequestTM"), Mode = BindingMode.TwoWay });
        SessionTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("SessionTM"), Mode = BindingMode.TwoWay });

        ToleranceEquityTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath("ToleranceEquity"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        TolerancePositionTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("TolerancePosition"), Mode = BindingMode.TwoWay });

        OptShareBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath("OptShareBaseAssets"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        ToleranceBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath("ToleranceBaseAssets"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });

        MaxShareInitReqsPositionTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath("MaxShareInitReqsPosition"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareInitReqsToolTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath("MaxShareInitReqsTool"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareInitReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath("MaxShareInitReqsPortfolio"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareMinReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath("MaxShareMinReqsPortfolio"),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });

        ShelflifeTradesTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("ShelfLifeTrades"), Mode = BindingMode.TwoWay });
        ShelflifeOrdersScriptsTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("ShelfLifeOrdersScripts"), Mode = BindingMode.TwoWay });
        ShelflifeTradesScriptsTxt.SetBinding(TextBox.TextProperty,
            new Binding() { Source = settings, Path = new PropertyPath("ShelfLifeTradesScripts"), Mode = BindingMode.TwoWay });
    }

    private void BindData(UnitedPortfolio portfolio)
    {
        AverageEquityTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath("AverageEquity"),
            Mode = BindingMode.OneWay,
            StringFormat = "### ### ### УЕ"
        });
        CurShareInitReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath("ShareInitReqs"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareInitReqsBaseTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath("ShareInitReqsBaseAssets"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        PotShareInitReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath("PotentialShareInitReqs"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareMinReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath("ShareMinReqs"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareBaseAssetsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath("ShareBaseAssets"),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
    }

    private void RestoreToolTabs()
    {
        for (int i = 0; i < Tools.Count; i++)
        {
            if (Settings.ToolsByPriority[i] != Tools[i].Name)
            {
                var initTool = Tools[i];
                int x = Array.FindIndex(Tools.ToArray(), x => x.Name == Settings.ToolsByPriority[i]);
                Tools[i] = Tools[x];
                Tools[x] = initTool;
            }
            ToolManager.CreateTab(Tools[i]);
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

    public async Task SaveTool(Tool tool, bool newTool = true)
    {
        if (newTool)
        {
            Tools.Add(tool);
            TradingSystem.ToolManager.CreateTab(tool);
            TradingSystem.Settings.ToolsByPriority.Add(tool.Name);
            ToolsByPriorityView.Items.Refresh();
            tool.PropertyChanged += UpdateTool;
        }
        else
        {
            tool.MainModel.Series.Clear();
            tool.MainModel.Series.Add(new OxyPlot.Series.CandleStickSeries { });
            tool.MainModel.Annotations.Clear();

            int i = Tools.IndexOf(tool);
            TradingSystem.ToolManager.UpdateControlGrid(tool);
            if (tool.Scripts.Length < 2)
            {
                ((TabsTools.Items[i] as TabItem).Content as Grid)
                    .Children.OfType<Grid>().Last().Children.OfType<Grid>().ToList()[1].Children.Clear();
                ((TabsTools.Items[i] as TabItem).Content as Grid)
                    .Children.OfType<Grid>().Last().Children.OfType<Grid>().ToList()[2].Children.Clear();
            }
        }

        if (Window.TradingSystem.Connector.Connection == ConnectionState.Connected)
        {
            await TradingSystem.ToolManager.RequestBarsAsync(tool);
            await TradingSystem.Connector.OrderSecurityInfoAsync(tool.Security);
            _ = Task.Run(() =>
            {
                System.Threading.Thread.Sleep(4000);
                TradingSystem.ToolManager.UpdateView(tool, true);
            });
        }
        else AddInfo("SaveTool: отсутствует соединение.");
        AddInfo("Saved tool: " + tool.Name);
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
        if (e.PropertyName == nameof(TradingSystem.Portfolio.InitReqs)) Task.Run(() => SaveData());
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
            Settings.Email = details[0];
            Settings.EmailPassword = details[1];
            (Notifier as EmailNotifier).Email = Settings.Email;
            (Notifier as EmailNotifier).Password = Settings.EmailPassword;
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
            Tools[i].Security.SourceBars.Write(Tools[i].Security.ShortName);
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
            Settings.ToolsByPriority.Remove(Tools[i].Name);
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
                (Settings.ToolsByPriority[i], Settings.ToolsByPriority[i - 1]) =
                    (Settings.ToolsByPriority[i - 1], Settings.ToolsByPriority[i]);
                n--;
                i--;
            }
        }
        else if (header.StartsWith("Downgrade") && i > -1 && i < Settings.ToolsByPriority.Count - n)
        {
            while (n > 0)
            {
                (Settings.ToolsByPriority[i], Settings.ToolsByPriority[i + 1]) =
                    (Settings.ToolsByPriority[i + 1], Settings.ToolsByPriority[i]);
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
            if (SelectedOrder.Status is "active" or "watching") await Task.Run(async () =>
            {
                if (!await TradingSystem.Connector.CancelOrderAsync(SelectedOrder)) AddInfo("Ошибка отмены заявки.");
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
                    Tool MyTool = Tools.Single(x => x.Security.Seccode == MyOrder.Seccode);
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
                    Tools.Single(x => x.Security.Seccode == MyTrade.Seccode).Scripts.Single
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
        Task.Run(() => TradingSystem.PortfolioManager.UpdatePositions());
    }

    private void ClearInfo(object sender, RoutedEventArgs e) => TxtBox.Clear();
    #endregion

    #region View
    private void UpdateConnection(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Connector.Connection))
        {
            if (Connector.Connection == ConnectionState.Connected)
            {
                Dispatcher.Invoke(() =>
                {
                    ConnectBtn.Content = "Disconnect";
                    StCon.Fill = Theme.Green;
                });
                Task.Run(async () =>
                {
                    await TradingSystem.PrepareForTrading();
                    Dispatcher.Invoke(() => ShowDistributionInfo(null, null));
                });
            }
            else Dispatcher.Invoke(() =>
            {
                if (Connector.Connection == ConnectionState.Connecting)
                {
                    ConnectBtn.Content = "Disconnect";
                    StCon.Fill = Theme.Orange;
                }
                else
                {
                    ConnectBtn.Content = "Connect";
                    StCon.Fill = Connector.Connection == ConnectionState.Disconnected ? Theme.Gray : Theme.Red;
                }
            });
        }
    }
    private void UpdatePortfolio(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TradingSystem.Portfolio.Positions))
        {
            Window.Dispatcher.Invoke(() =>
            {
                Window.PortfolioView.ItemsSource = Portfolio.AllPositions;
                Window.PortfolioView.ScrollIntoView(this);
            });
        }
    }
    private void UpdateTool(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Tool.ShowBasicSecurity))
        {
            TradingSystem.ToolManager.UpdateView(sender as Tool, true);
        }
        else if (e.PropertyName is nameof(Tool.TradeShare) or nameof(Tool.UseShiftBalance))
        {
            TradingSystem.ToolManager.UpdateControlGrid(sender as Tool);
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

    private async void ChangeСonnection(object sender, RoutedEventArgs e)
    {
        if (Connector.Connection is ConnectionState.Connected or ConnectionState.Connecting)
            await Connector.DisconnectAsync();
        else if (TxtLog.Text.Length > 0 && TxtPas.SecurePassword.Length > 0)
            await Connector.ConnectAsync(TxtLog.Text, TxtPas.SecurePassword);
        else AddInfo("Type login and password");
    }
    private async void ClosingMainWindow(object sender, CancelEventArgs e)
    {
        var Res = MessageBox.Show("Are you sure you want to exit?",
            "Closing", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (Res == MessageBoxResult.No || !SaveData(true))
        {
            e.Cancel = true;
            return;
        }

        await TradingSystem.StopAsync();
        Logger.StopLogging();
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
    private void ShowDistributionInfo(object sender, RoutedEventArgs e)
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
