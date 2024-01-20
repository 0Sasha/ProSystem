using ProSystem.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace ProSystem;

public partial class MainWindow : Window
{
    private readonly string DataDirectory = "Data";

    public Logger Logger { get; init; }
    public Serializer Serializer { get; init; }
    public INotifier Notifier { get; init; }

    public Settings Settings { get; init; }
    public Portfolio Portfolio { get; init; }
    public ObservableCollection<Tool> Tools { get; init; }
    public ObservableCollection<Trade> Trades { get; init; }

    public TradingSystem TradingSystem { get; init; }
    public Connector Connector { get => TradingSystem.Connector; }
    public IToolManager ToolManager { get => TradingSystem.ToolManager; }

    public MainWindow()
    {
        InitializeComponent();

        Logger = new(AddInfo);
        Serializer = new JsonSerializer(DataDirectory, AddInfo);

        Settings = Serializer.TryDeserialize<Settings>();
        Portfolio = Serializer.TryDeserialize<Portfolio>();
        Tools = new ObservableCollection<Tool>(Serializer.TryDeserialize<List<Tool>>(nameof(Tools)));
        Trades = new ObservableCollection<Trade>(Serializer.TryDeserialize<List<Trade>>(nameof(Trades)));

        Settings.Prepare(Tools, AddInfo);
        Notifier = new EmailNotifier(Settings, AddInfo);
        PrepareCommonControls(Settings.Connector);

        TradingSystem = new(this, Settings, Portfolio, Tools, Trades);
        BindTradingSystem(TradingSystem);
        TradingSystem.Start();
    }

    public NetworkCredential GetCredential() =>
        Dispatcher.Invoke(() => new NetworkCredential(TxtLog.Text, TxtPas.SecurePassword));

    public async Task SaveTool(Tool tool, bool newTool = true)
    {
        if (newTool)
        {
            Tools.Add(tool);
            ToolManager.Initialize(tool);
            TabsTools.Items.Add(tool.Tab);
            TradingSystem.Settings.ToolsByPriority.Add(tool.Name);
            ToolsByPriorityView.Items.Refresh();
            tool.PropertyChanged += UpdateTool;
        }
        else
        {
            tool.MainModel?.Series.Clear();
            tool.MainModel?.Series.Add(new OxyPlot.Series.CandleStickSeries { });
            tool.MainModel?.Annotations.Clear();

            TradingSystem.ToolManager.UpdateControlPanel(tool, true);
        }

        if (TradingSystem.Connector.Connection == ConnectionState.Connected)
        {
            await TradingSystem.Connector.RequestBarsAsync(tool);
            await TradingSystem.Connector.OrderSecurityInfoAsync(tool.Security);
            _ = Task.Run(() =>
            {
                Thread.Sleep(4000);
                TradingSystem.ToolManager.UpdateView(tool, true);
            });
        }
        else AddInfo("SaveTool: отсутствует соединение.");
        AddInfo("Saved tool: " + tool.Name);
    }

    public async Task ResetAsync()
    {
        Logger.Stop();
        Logger.Start();
        await FileManager.ArchiveFiles(DataDirectory, ".json", "Data", false);
    }

    private void PrepareCommonControls(string connector)
    {
        TxtBox.Text += RestoreInfo();
        BoxConnectors.ItemsSource = new[] { connector };
        BoxConnectors.SelectedIndex = 0;
        ComboBoxDistrib.ItemsSource = new[] { "All tools", "First part", "Second part" };
        ComboBoxDistrib.SelectedIndex = 0;
    }

    private string RestoreInfo()
    {
        if (File.Exists(DataDirectory + "/Info.txt"))
        {
            try
            {
                var info = File.ReadAllText(DataDirectory + "/Info.txt");
                if (info != string.Empty) return "\nStart...\n" + info + "\nEnd...";
            }
            catch (Exception ex) { AddInfo("RestoreInfo: " + ex.Message); }
        }
        return string.Empty;
    }

    private void BindTradingSystem(TradingSystem tradingSystem)
    {
        BindSettings(tradingSystem.Settings);
        BindPortfolio(tradingSystem.Portfolio);

        ToolsView.ItemsSource = tradingSystem.Tools;
        ComboBoxTool.ItemsSource = tradingSystem.Tools;
        OrdersView.ItemsSource = tradingSystem.Orders;
        TradesView.ItemsSource = tradingSystem.Trades;
        PortfolioView.ItemsSource = tradingSystem.Portfolio.AllPositions;
        ToolsByPriorityView.ItemsSource = tradingSystem.Settings.ToolsByPriority;
        BackupServerCheck.SetBinding(ToggleButton.IsCheckedProperty, new Binding()
        {
            Source = tradingSystem.Connector,
            Path = new PropertyPath(nameof(Connector.BackupServer)),
            Mode = BindingMode.TwoWay
        });

        tradingSystem.Portfolio.PropertyChanged += UpdatePortfolio;
        tradingSystem.Portfolio.PropertyChanged += SaveData;
        tradingSystem.Tools.CollectionChanged += UpdateTools;
        tradingSystem.Orders.CollectionChanged += UpdateOrders;
        tradingSystem.Trades.CollectionChanged += UpdateTrades;
        tradingSystem.Connector.PropertyChanged += UpdateConnection;
        foreach (var tool in tradingSystem.Tools) tool.PropertyChanged += UpdateTool;

        RestoreToolTabsAndInitialize();
    }

    private void BindSettings(Settings settings)
    {
        ScheduleCheck.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding()
            {
                Source = settings,
                Path = new PropertyPath(nameof(Settings.ScheduledConnection)),
                Mode = BindingMode.TwoWay
            });

        DisplaySentOrdersCheck.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding()
            {
                Source = settings,
                Path = new PropertyPath(nameof(Settings.DisplaySentOrders)),
                Mode = BindingMode.TwoWay
            });
        DisplayNewTradesCheck.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding()
            {
                Source = settings,
                Path = new PropertyPath(nameof(Settings.DisplayNewTrades)),
                Mode = BindingMode.TwoWay
            });
        DisplaySpecialInfoCheck.SetBinding(ToggleButton.IsCheckedProperty,
            new Binding()
            {
                Source = settings,
                Path = new PropertyPath(nameof(Settings.DisplaySpecialInfo)),
                Mode = BindingMode.TwoWay
            });

        TxtLog.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.LoginConnector)),
            Mode = BindingMode.TwoWay
        });

        ToleranceEquityTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.ToleranceEquity)),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        TolerancePositionTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.TolerancePosition)),
            Mode = BindingMode.TwoWay
        });

        OptShareBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.OptShareBaseAssets)),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        ToleranceBaseAssetsTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.ToleranceBaseAssets)),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });

        MaxShareInitReqsPositionTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.MaxShareInitReqsPosition)),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareInitReqsToolTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.MaxShareInitReqsTool)),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareInitReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.MaxShareInitReqsPortfolio)),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
        MaxShareMinReqsPortfolioTxt.SetBinding(TextBox.TextProperty, new Binding()
        {
            Source = settings,
            Path = new PropertyPath(nameof(Settings.MaxShareMinReqsPortfolio)),
            Mode = BindingMode.TwoWay,
            StringFormat = "#'%'"
        });
    }

    private void BindPortfolio(Portfolio portfolio)
    {
        AverageEquityTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath(nameof(Portfolio.AverageEquity)),
            Mode = BindingMode.OneWay,
            StringFormat = "### ### ###"
        });
        CurShareInitReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath(nameof(Portfolio.ShareInitReqs)),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareInitReqsBaseTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath(nameof(Portfolio.ShareInitReqsBaseAssets)),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        PotShareInitReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath(nameof(Portfolio.PotentialShareInitReqs)),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareMinReqsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath(nameof(Portfolio.ShareMinReqs)),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
        CurShareBaseAssetsTxt.SetBinding(TextBlock.TextProperty, new Binding()
        {
            Source = portfolio,
            Path = new PropertyPath(nameof(Portfolio.ShareBaseAssets)),
            Mode = BindingMode.OneWay,
            StringFormat = "#.##'%'"
        });
    }

    private void RestoreToolTabsAndInitialize()
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
            ToolManager.Initialize(Tools[i]);
            TabsTools.Items.Add(Tools[i].Tab);
        }
    }

    internal void AddInfo(string data, bool important = true, bool notify = false)
    {
        if (data == string.Empty)
        {
            data = "AddInfo: data was empty";
            important = true;
        }

        Logger.WriteLog(data);
        if (important)
        {
            Dispatcher.Invoke(() =>
            {
                TxtBox.AppendText("\n" + DateTime.Now.ToString("dd.MM HH:mm:ss") + ": " + data);
                TxtBox.ScrollToEnd();
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
            Serializer.Serialize(TradingSystem.Tools.ToList(), nameof(TradingSystem.Tools));
            Serializer.Serialize(TradingSystem.Portfolio, nameof(TradingSystem.Portfolio));
            Serializer.Serialize(TradingSystem.Settings, nameof(TradingSystem.Settings));
            Serializer.Serialize(TradingSystem.Trades.ToList(), nameof(TradingSystem.Trades));
            if (SaveInfoPanel) Dispatcher.Invoke(() => File.WriteAllText(DataDirectory + "/Info.txt", TxtBox.Text));
            return true;
        }
        catch (Exception ex) { AddInfo("SaveData: " + ex.Message); return false; }
    }
    private void SaveData(object? sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            if (SaveData(true)) AddInfo("Данные сохранены.");
            else AddInfo("Данные не сохранены.");
        });
    }
    private void SaveData(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TradingSystem.Portfolio.InitReqs)) Task.Run(() => SaveData());
    }
    private void ResizeControlPanel(object? sender, RoutedEventArgs e)
    {
        if (TabsTools.SelectedIndex > -1)
        {
            ColumnDefinition Column = ((Grid)TabsTools.SelectedContent).ColumnDefinitions[1];
            if (Column.ActualWidth < 200) Column.Width = new GridLength(200);
            else Column.Width = new GridLength(100);
        }
    }
    private void NotifierTest(object? sender, RoutedEventArgs e)
    {
        try { Notifier.Notify("Test data"); }
        catch (Exception ex) { AddInfo("NotifierTest: " + ex.Message); }
    }
    private void Test(object? sender, RoutedEventArgs e)
    {
        try
        {


        }
        catch { }

    }
    #endregion

    #region Context menu
    private void AddToolContext(object? sender, RoutedEventArgs e)
    {
        NewTool NewTool = new(this);
        NewTool.Show();
    }
    private void OpenTabToolContext(object? sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            TabsTools.SelectedIndex = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            MainTabs.SelectedIndex = 3;
        }
    }
    private async void ChangeToolContext(object? sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            if (Tools[i].Active) await ToolManager.ChangeActivityAsync(Tools[i]);
            NewTool NewTool = new(this, Tools[i]);
            NewTool.Show();
        }
    }
    private void UpdateToolbarContext(object? sender, RoutedEventArgs e) => ToolsView.Items.Refresh();
    private void ReloadBarsToolContext(object? sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            MessageBoxResult Res = MessageBox.Show("Are you sure you want to reload bars of " + Tools[i].Name + "?",
                "Reloading bars", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (Res == MessageBoxResult.No) return;
            Task.Run(() => ToolManager.ReloadBarsAsync(Tools[i]));
        }
    }
    private void WriteSourceBarsToolContext(object? sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            Tools[i].Security?.SourceBars?.Write(Tools[i].Security.Seccode);
            Tools[i].BasicSecurity?.SourceBars?.Write(Tools[i].Name + " basic");
        }
    }
    private async void RemoveToolContext(object? sender, RoutedEventArgs e)
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

    private void ChangePriorityTool(object? sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender, nameof(sender));
        var header = (string)((MenuItem)sender).Header;
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

    private async void CancelOrderContext(object? sender, RoutedEventArgs e)
    {
        if (TradingSystem.Connector.Connection == ConnectionState.Connected && OrdersView.SelectedItem != null)
        {
            Order order = (Order)OrdersView.SelectedItem;
            if (Connector.OrderIsActive(order)) await Task.Run(async () =>
            {
                if (!await Connector.CancelOrderAsync(order)) AddInfo("Ошибка отмены заявки.");
            });
            else AddInfo("Заявка не активна.");
        }
    }
    private void RemoveOrderContext(object? sender, RoutedEventArgs e)
    {
        if (((TabItem)MainTabs.SelectedItem).Header.ToString() == "Portfolio")
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
                            if (MyScript.Orders.Remove(MyOrder)) return;
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
    private void RemoveTradeContext(object? sender, RoutedEventArgs e)
    {
        if (((TabItem)MainTabs.SelectedItem).Header.ToString() == "Portfolio")
        {
            if (TradesView.SelectedItem != null)
            {
                if (!TradingSystem.Trades.Remove((Trade)TradesView.SelectedItem)) AddInfo("Не найдена сделка для удаления.");
            }
        }
        else if (TradesInfo.SelectedItem != null)
        {
            Trade MyTrade = (Trade)TradesInfo.SelectedItem;
            if (MyTrade.OrderSender == "System") TradingSystem.SystemTrades.Remove(MyTrade);
            else if (MyTrade.Seccode != null)
            {
                try
                {
                    Tools.Single(x => x.Security.Seccode == MyTrade.Seccode).Scripts.Single
                        (x => x.Name == MyTrade.OrderSender).Trades.Remove(MyTrade);
                }
                catch (Exception ex) { AddInfo("Исключение во время попытки удаления сделки: " + ex.Message); }
            }
            else
            {
                foreach (Tool MyTool in Tools)
                {
                    if (MyTool.Scripts.SingleOrDefault(x => x.Name == MyTrade.OrderSender) != null)
                    {
                        if (MyTool.Scripts.Single(x => x.Name == MyTrade.OrderSender).Trades.Remove(MyTrade)) return;
                        else break;
                    }
                }
                AddInfo("Не найдена сделка для удаления.");
            }
        }
    }
    private void UpdatePortfolioViewContext(object? sender, RoutedEventArgs e)
    {
        OrdersView.Items.Refresh();
        TradesView.Items.Refresh();
        Task.Run(() => TradingSystem.PortfolioManager.UpdatePositions());
    }

    private void ClearInfo(object? sender, RoutedEventArgs e) => TxtBox.Clear();
    #endregion

    #region View
    private void UpdateConnection(object? sender, PropertyChangedEventArgs e)
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
    private void UpdatePortfolio(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TradingSystem.Portfolio.Positions))
        {
            Dispatcher.Invoke(() =>
            {
                PortfolioView.ItemsSource = Portfolio.AllPositions;
                PortfolioView.ScrollIntoView(this);
            });
        }
    }
    private void UpdateTool(object? sender, PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        if (e.PropertyName == nameof(Tool.ShowBasicSecurity))
            Task.Run(() => TradingSystem.ToolManager.UpdateView((Tool)sender, true));
        else if (e.PropertyName is nameof(Tool.TradeShare) or nameof(Tool.UseShiftBalance))
            TradingSystem.ToolManager.UpdateControlPanel((Tool)sender, false);
    }
    private void UpdateTools(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ToolsView.Items.Refresh();
        Task.Run(() => SaveData());
    }
    private void UpdateOrders(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OrdersView.Items.Refresh();
        if (OrdersView.Items.Count > 0) OrdersView.ScrollIntoView(OrdersView.Items[^1]);
    }
    private void UpdateTrades(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TradesView.Items.Refresh();
        if (TradesView.Items.Count > 0) TradesView.ScrollIntoView(TradesView.Items[^1]);
    }

    private async void ChangeСonnection(object? sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        var button = (Button)sender;
        button.IsEnabled = false;
        if (Connector.Connection is ConnectionState.Connected or ConnectionState.Connecting)
            await Connector.DisconnectAsync();
        else if (TxtLog.Text.Length > 0 && TxtPas.SecurePassword.Length > 0)
            await Connector.ConnectAsync(TxtLog.Text, TxtPas.SecurePassword);
        else AddInfo("Type login and password");
        button.IsEnabled = true;
    }
    private async void ClosingMainWindow(object? sender, CancelEventArgs e)
    {
        var Res = MessageBox.Show("Are you sure you want to exit?",
            "Closing", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (Res == MessageBoxResult.No || !SaveData(true))
        {
            e.Cancel = true;
            return;
        }

        await TradingSystem.StopAsync();
        Logger.Stop();
    }
    private async void ChangeActivityTool(object? sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        var button = (Button)sender;
        button.IsEnabled = false;
        await ToolManager.ChangeActivityAsync((Tool)button.DataContext);
        button.IsEnabled = true;
    }
    private void ComboBoxToolChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0)
        {
            var added = e.AddedItems[0];
            if (added != null) ComboBoxScript.ItemsSource = ((Tool)added).Scripts;
        }
    }

    private void ShowScriptInfo(object? sender, RoutedEventArgs e)
    {
        if (ComboBoxTool.SelectedIndex > -1 && ComboBoxScript.SelectedIndex > -1)
        {
            OrdersInfo.ItemsSource = ((Tool)ComboBoxTool.SelectedItem)
                .Scripts.SingleOrDefault(x => x == (Script)ComboBoxScript.SelectedItem)?.Orders;
            OrdersInfo.Items.Refresh();

            TradesInfo.ItemsSource = ((Tool)ComboBoxTool.SelectedItem)
                .Scripts.SingleOrDefault(x => x == (Script)ComboBoxScript.SelectedItem)?.Trades;
            TradesInfo.Items.Refresh();
        }
    }
    private void ShowSystemInfo(object? sender, RoutedEventArgs e)
    {
        OrdersInfo.ItemsSource = TradingSystem.SystemOrders;
        OrdersInfo.Items.Refresh();

        TradesInfo.ItemsSource = TradingSystem.SystemTrades;
        TradesInfo.Items.Refresh();
    }
    private void ShowDistributionInfo(object? sender, RoutedEventArgs? e)
    {
        if (Tools.Count < 1 || Portfolio.Saldo < 1 || Portfolio.Positions == null) return;
        ArgumentNullException.ThrowIfNull(OnlyPosCheckBox.IsChecked);
        ArgumentNullException.ThrowIfNull(ExcludeBaseCheckBox.IsChecked);

        DistributionPlot.Model = Tools.GetPlot(Portfolio.Positions, Portfolio.Saldo,
            (string)ComboBoxDistrib.SelectedItem, (bool)OnlyPosCheckBox.IsChecked,
            (bool)ExcludeBaseCheckBox.IsChecked);
        DistributionPlot.Controller ??= Plot.GetController();

        PortfolioPlot.Model = Portfolio.GetPlot();
        PortfolioPlot.Controller ??= Plot.GetController();
    }

    private void ResizeInfoPanel(object? sender, RoutedEventArgs e)
    {
        if ((int)RowInfo.Height.Value == 2) RowInfo.Height = new GridLength(0.5, GridUnitType.Star);
        else if ((int)RowInfo.Height.Value == 1) RowInfo.Height = new GridLength(2, GridUnitType.Star);
        else RowInfo.Height = new GridLength(1, GridUnitType.Star);
    }
    #endregion
}
