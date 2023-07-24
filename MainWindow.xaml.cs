using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ProSystem.Services;
using static ProSystem.TXmlConnector;
using System.Collections;

namespace ProSystem;

public partial class MainWindow : Window
{
    #region Command and data processing
    private async void ChangeСonnection(object sender, RoutedEventArgs e)
    {
        if (Connection is ConnectionState.Connected or ConnectionState.Connecting) await Task.Run(() => Disconnect());
        else if (TxtLog.Text.Length > 0 && TxtPas.SecurePassword.Length > 0)
        {
            TriggerReconnection = DateTime.Now.AddSeconds(MySettings.SessionTM);
            await Task.Run(() => Connect());
        }
        else AddInfo("Введите логин и пароль.");
    }
    private void ClosingMainWindow(object sender, CancelEventArgs e)
    {
        MessageBoxResult Res =
            MessageBox.Show("Are you sure you want to exit?", "Closing", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (Res == MessageBoxResult.No || !SaveData(true))
        {
            e.Cancel = true;
            return;
        }

        CheckingInterval = 1000;
        System.Threading.Thread.Sleep(50);
        if (Connection != ConnectionState.Disconnected) Disconnect();

        if (!ConnectorInitialized || ConnectorUnInitialize()) Logger.StopLogging();
        else MessageBox.Show("UnInitialization failed.");
    }
    private void Test(object sender, RoutedEventArgs e)
    {

    }

    private void ClearOutdatedData()
    {
        int i, j, k;

        // Очистка устаревших сделок
        for (i = 0; i < Trades.Count; i++)
        {
            if (Trades[i].DateTime.Date < DateTime.Today.AddDays(-MySettings.ShelfLifeTrades)) continue;
            else break;
        }
        if (i > 0)
        {
            Trades.CollectionChanged -= UpdateTrades;
            Trades = new ObservableCollection<Trade>(Trades.ToArray()[i..]);
            Dispatcher.Invoke(() => TradesView.ItemsSource = Trades);
            Trades.CollectionChanged += UpdateTrades;
            AddInfo("ClearOutdatedData: удалены устаревшие сделки: " + i, false);
        }

        // Очистка устаревших заявок и сделок скриптов
        for (i = 0; i < Tools.Count; i++)
        {
            for (j = 0; j < Tools[i].Scripts.Length; j++)
            {
                for (k = 0; k < Tools[i].Scripts[j].Orders.Count; k++)
                {
                    if (Tools[i].Scripts[j].Orders[k].DateTime.Date < DateTime.Today.AddDays(-MySettings.ShelfLifeOrdersScripts)) continue;
                    else break;
                }
                if (k > 0)
                {
                    Tools[i].Scripts[j].Orders = new ObservableCollection<Order>(Tools[i].Scripts[j].Orders.ToArray()[k..]);
                    AddInfo("ClearOutdatedData: удалены устаревшие заявки скрипта: " + Tools[i].Scripts[j].Name);
                }

                for (k = 0; k < Tools[i].Scripts[j].Trades.Count; k++)
                {
                    if (Tools[i].Scripts[j].Trades[k].DateTime.Date < DateTime.Today.AddDays(-MySettings.ShelfLifeTradesScripts)) continue;
                    else break;
                }
                if (k > 0)
                {
                    Tools[i].Scripts[j].Trades = new ObservableCollection<Trade>(Tools[i].Scripts[j].Trades.ToArray()[k..]);
                    AddInfo("ClearOutdatedData: удалены устаревшие сделки скрипта: " + Tools[i].Scripts[j].Name);
                }
            }
        }
    }
    private bool SaveData(bool SaveInfoPanel = false)
    {
        AddInfo("SaveData: Сериализация", false);
        try
        {
            MySerializer.Serialize(Tools, "Tools");
            MySerializer.Serialize(Portfolio, "Portfolio");
            MySerializer.Serialize(MySettings, "Settings");
            MySerializer.Serialize(Trades, "Trades");
            if (SaveInfoPanel) Dispatcher.Invoke(() => File.WriteAllText(MySerializer.DataDirectory + "/Info.txt", TxtBox.Text));
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
            (MyNotifier as EmailNotifier).Email = MySettings.Email;
            (MyNotifier as EmailNotifier).Password = MySettings.EmailPassword;
            MyNotifier.Notify("Test notify");
            AddInfo("Тестовое уведомление отправлено.");
        }
        catch (Exception ex) { AddInfo("TakeLoginDetails: " + ex.Message); }
    }
    private static async Task<bool> ArchiveFiles(string directory, string partNameSourceFiles, string archName, bool deleteSourceFiles)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                var paths = Directory.GetFiles(directory).Where(x => x.Contains(partNameSourceFiles) && !x.Contains(".zip")).ToArray();
                if (paths.Length == 0)
                {
                    AddInfo("ArchiveFiles: Файлы не найдены. " + directory + "/" + partNameSourceFiles);
                    return false;
                }

                string newDir = directory + "/" + archName;
                if (File.Exists(newDir)) File.Delete(newDir);
                Directory.CreateDirectory(newDir);
                foreach (var path in paths) File.Copy(path, newDir + "/" + path.Replace(directory, ""));

                if (File.Exists(newDir + ".zip")) File.Delete(newDir + ".zip");
                await Task.Run(() => ZipFile.CreateFromDirectory(newDir, newDir + ".zip", CompressionLevel.SmallestSize, false));
                Directory.Delete(newDir, true);
                if (deleteSourceFiles) foreach (var path in paths) File.Delete(path);
                return true;
            }
            else
            {
                AddInfo("ArchiveFiles: Директория " + directory + " не существует");
                return false;
            }
        }
        catch (Exception ex)
        {
            AddInfo("ArchiveFiles: " + ex.Message);
            return false;
        }
    }
    public void UpdatePortfolio(object sender, PropertyChangedEventArgs e)
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

    public static void AddInfo(string data, bool important = true, bool notify = false)
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
        if (notify) MyNotifier.Notify(data);
    }

    private void ShowUsedMemory(object sender, RoutedEventArgs e) =>
        AddInfo("Использование памяти: " + (GC.GetTotalMemory(false) / 1000000).ToString(IC) + " МБ");
    private void ClearMemory(object sender, RoutedEventArgs e) => GC.Collect();
    #endregion

    #region Context menu
    private void AddToolContext(object sender, RoutedEventArgs e)
    {
        NewTool NewTool = new();
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
            if (Tools[i].Active) await Tools[i].ChangeActivity();
            NewTool NewTool = new(Tools[i]);
            NewTool.Show();
        }
    }
    private void UpdateToolbarContext(object sender, RoutedEventArgs e) => ToolsView.Items.Refresh();
    private void ReloadBarsToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            MessageBoxResult Res = MessageBox.Show("Are you sure you want to reload bars of " + Tools[i].Name + "?",
                "Reloading bars", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (Res == MessageBoxResult.No) return;
            Task.Run(() => Tools[i].ReloadBars());
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
            if (Tools[i].Active) await Tools[i].ChangeActivity();
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
        if (Connection == ConnectionState.Connected && OrdersView.SelectedItem != null)
        {
            Order SelectedOrder = (Order)OrdersView.SelectedItem;
            if (SelectedOrder.Status is "active" or "watching") await Task.Run(() =>
            {
                if (!CancelOrder(SelectedOrder)) AddInfo("Ошибка отмены заявки.");
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
                if (!Orders.Remove((Order)OrdersView.SelectedItem)) AddInfo("Не найдена заявка для удаления.");
            }
        }
        else if (OrdersInfo.SelectedItem != null)
        {
            Order MyOrder = (Order)OrdersInfo.SelectedItem;
            if (MyOrder.Sender == "System") SystemOrders.Remove(MyOrder);
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
                if (!Trades.Remove((Trade)TradesView.SelectedItem)) AddInfo("Не найдена сделка для удаления.");
            }
        }
        else if (TradesInfo.SelectedItem != null)
        {
            Trade MyTrade = (Trade)TradesInfo.SelectedItem;
            if (MyTrade.SenderOrder == "System") SystemTrades.Remove(MyTrade);
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
        Task.Run(() => MyPortfolioManager.UpdatePositions());
    }

    private void ClearInfo(object sender, RoutedEventArgs e) => TxtBox.Clear();
    #endregion

    #region Operations with tools
    public async void SaveTool(Tool MyTool, bool NewTool = true)
    {
        int k;
        if (NewTool)
        {
            Tools.Add(MyTool);
            TabsTools.Items.Add(GetTabItem(MyTool));
            MySettings.ToolsByPriority.Add(MyTool.Name);
            ToolsByPriorityView.Items.Refresh();
            k = Tools.Count - 1;
        }
        else
        {
            MyTool.MainModel.Series.Clear();
            MyTool.MainModel.Series.Add(new OxyPlot.Series.CandleStickSeries { });
            MyTool.MainModel.Annotations.Clear();

            k = Tools.IndexOf(MyTool);
            UpdateControlGrid(MyTool, ((TabsTools.Items[k] as TabItem).Content as Grid).Children.OfType<Grid>().Last().Children.OfType<Grid>().First());
            if (MyTool.Scripts.Length < 2)
            {
                ((TabsTools.Items[k] as TabItem).Content as Grid).Children.OfType<Grid>().Last().Children.OfType<Grid>().ToList()[1].Children.Clear();
                ((TabsTools.Items[k] as TabItem).Content as Grid).Children.OfType<Grid>().Last().Children.OfType<Grid>().ToList()[2].Children.Clear();
            }
        }
        for (int i = 0; i < Tools[k].Scripts.Length; i++) Tools[k].Scripts[i].Initialize(Tools[k], TabsTools.Items[k] as TabItem);

        if (Connection == ConnectionState.Connected)
        {
            await Task.Run(() =>
            {
                Tools[k].RequestBars();
                GetSecurityInfo(Tools[k].MySecurity.Market, Tools[k].MySecurity.Seccode);
                GetClnSecPermissions(Tools[k].MySecurity.Board, Tools[k].MySecurity.Seccode, Tools[k].MySecurity.Market);
                _ = Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(4000);
                    Tools[k].UpdateView(true);
                });
            });
        }
        else AddInfo("SaveTool: отсутствует соединение.");
        AddInfo("Saved tool: " + Tools[k].Name);
    }
    private async void ChangeActivityTool(object sender, RoutedEventArgs e)
    {
        Button MyButton = sender as Button;
        MyButton.IsEnabled = false;
        if (MyButton.Content == null) await (MyButton.DataContext as Tool).ChangeActivity();
        else await Tools[TabsTools.SelectedIndex].ChangeActivity();
        MyButton.IsEnabled = true;
    }
    private void UpdateViewTool(object sender, SelectionChangedEventArgs e)
    {
        Tool MyTool = (sender as ComboBox).DataContext as Tool;
        Task.Run(() => MyTool.UpdateView(true));
    }

    private TabItem GetTabItem(Tool tool)
    {
        return new TabItem()
        {
            Header = tool.Name,
            Width = 54,
            Height = 24,
            Content = GetGridTabTool(tool)
        };
    }
    private Grid GetGridTabTool(Tool MyTool)
    {
        Grid GlobalGrid = new();
        GlobalGrid.ColumnDefinitions.Add(new());
        GlobalGrid.ColumnDefinitions.Add(new() { Width = new GridLength(200), MinWidth = 100 });


        Grid PlotGrid = new();
        PlotGrid.RowDefinitions.Add(new() { MinHeight = 50, MaxHeight = 120 });
        PlotGrid.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star) });

        OxyPlot.SkiaSharp.Wpf.PlotView PlotView = new() { Visibility = Visibility.Hidden };
        PlotView.SetBinding(OxyPlot.Wpf.PlotViewBase.ModelProperty, new Binding() { Source = MyTool, Path = new PropertyPath("Model") });
        PlotView.SetBinding(OxyPlot.Wpf.PlotViewBase.ControllerProperty, new Binding() { Source = MyTool, Path = new PropertyPath("Controller") });

        OxyPlot.SkiaSharp.Wpf.PlotView MainPlotView = new();
        MainPlotView.SetBinding(OxyPlot.Wpf.PlotViewBase.ModelProperty, new Binding() { Source = MyTool, Path = new PropertyPath("MainModel") });
        MainPlotView.SetBinding(OxyPlot.Wpf.PlotViewBase.ControllerProperty, new Binding() { Source = MyTool, Path = new PropertyPath("Controller") });
        Grid.SetRowSpan(MainPlotView, 2);

        PlotGrid.Children.Add(PlotView);
        PlotGrid.Children.Add(MainPlotView);


        Grid ControlGrid = new();
        Grid.SetColumn(ControlGrid, 1);
        ControlGrid.RowDefinitions.Add(new() { Height = new GridLength(1.2, GridUnitType.Star) });
        ControlGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        ControlGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });

        Grid ControlGrid1 = new();
        UpdateControlGrid(MyTool, ControlGrid1);
        Grid ControlGrid2 = new();
        Grid.SetRow(ControlGrid2, 1);
        Grid ControlGrid3 = new();
        Grid.SetRow(ControlGrid3, 2);

        Border Border = new() { BorderBrush = MainDictionary.Dictionary.txtBorder, BorderThickness = new Thickness(1) };
        Border Border1 = new() { BorderBrush = MainDictionary.Dictionary.txtBorder, BorderThickness = new Thickness(1) };
        Grid.SetRow(Border1, 1);
        Border Border2 = new() { BorderBrush = MainDictionary.Dictionary.txtBorder, BorderThickness = new Thickness(1) };
        Grid.SetRow(Border2, 2);

        ControlGrid.Children.Add(ControlGrid1);
        ControlGrid.Children.Add(ControlGrid2);
        ControlGrid.Children.Add(ControlGrid3);
        ControlGrid.Children.Add(Border);
        ControlGrid.Children.Add(Border1);
        ControlGrid.Children.Add(Border2);


        GlobalGrid.Children.Add(PlotGrid);
        GlobalGrid.Children.Add(ControlGrid);
        return GlobalGrid;
    }
    public void UpdateControlGrid(Tool MyTool, Grid ControlGrid = null)
    {
        Button ActiveButton = new()
        {
            Content = MyTool.Active ? "Deactivate tool" : "Activate tool",
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(5, 5, 0, 0),
            Width = 90,
            Height = 20
        };
        ActiveButton.Click += new RoutedEventHandler(ChangeActivityTool);

        ComboBox BaseTFBox = new()
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(45, 30, 0, 0),
            Width = 50,
            ItemsSource = MyTimeFrames
        };
        BaseTFBox.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
            new Binding() { Source = MyTool, Path = new PropertyPath("BaseTF"), Mode = BindingMode.TwoWay });

        List<string> MyScripts = new();
        foreach (Script Script in MyTool.Scripts) MyScripts.Add(Script.Name);
        MyScripts.Add("AllScripts");
        MyScripts.Add("Nothing");
        ComboBox ScriptsBox = new()
        {
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(105, 30, 0, 0),
            Width = 90,
            ItemsSource = MyScripts,
            SelectedValue = "AllScripts",
            DataContext = MyTool
        };
        ScriptsBox.SelectionChanged += UpdateViewTool;

        Border BorderState = new()
        {
            Margin = new Thickness(0, 55, 0, 0),
            Height = 10,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = Theme.Orange
        };
        MyTool.BorderState = BorderState;

        if (ControlGrid == null)
            ControlGrid = (((Window.TabsTools.Items[Tools.IndexOf(MyTool)] as TabItem).Content as Grid).Children[1] as Grid).Children[0] as Grid;
        ControlGrid.Children.Clear();
        ControlGrid.Children.Add(ActiveButton);
        ControlGrid.Children.Add(GetTextBlock("BaseTF", 5, 33));
        ControlGrid.Children.Add(BaseTFBox);
        ControlGrid.Children.Add(ScriptsBox);

        ControlGrid.Children.Add(BorderState);
        ControlGrid.Children.Add(GetCheckBox(MyTool, "Stop trading", "StopTrading", 5, 70));
        ControlGrid.Children.Add(GetCheckBox(MyTool, "Normalization", "UseNormalization", 5, 110));
        ControlGrid.Children.Add(GetCheckBox(MyTool, "Trade share", "TradeShare", 5, 130));

        ControlGrid.Children.Add(GetCheckBox(MyTool, "Basic security", "ShowBasicSecurity", 105, 70));
        ControlGrid.Children.Add(GetTextBlock("Wait limit", 105, 110));
        ControlGrid.Children.Add(GetTextBox(MyTool, "WaitingLimit", 165, 110));
        ControlGrid.Children.Add(GetCheckBox(MyTool, "Shift balance", "UseShiftBalance", 105, 130));

        if (MyTool.TradeShare)
        {
            ControlGrid.Children.Add(GetTextBlock("Share fund", 5, 150));
            ControlGrid.Children.Add(GetTextBox(MyTool, "ShareOfFunds", 65, 150));

            ControlGrid.Children.Add(GetTextBlock("Min lots", 5, 170));
            ControlGrid.Children.Add(GetTextBox(MyTool, "MinNumberOfLots", 65, 170));

            ControlGrid.Children.Add(GetTextBlock("Max lots", 105, 170));
            ControlGrid.Children.Add(GetTextBox(MyTool, "MaxNumberOfLots", 165, 170));
        }
        else
        {
            ControlGrid.Children.Add(GetTextBlock("Num lots", 5, 150));
            ControlGrid.Children.Add(GetTextBox(MyTool, "NumberOfLots", 65, 150));
        }
        if (MyTool.UseShiftBalance)
        {
            ControlGrid.Children.Add(GetTextBlock("Base balance", 105, 150));
            ControlGrid.Children.Add(GetTextBox(MyTool, "BaseBalance", 165, 150));
        }

        TextBlock MainBlInfo = GetTextBlock("Main info", 5, 190);
        MyTool.MainBlockInfo = MainBlInfo;
        ControlGrid.Children.Add(MainBlInfo);

        TextBlock BlInfo = GetTextBlock("Info", 105, 190);
        MyTool.BlockInfo = BlInfo;
        ControlGrid.Children.Add(BlInfo);
    }

    public static TextBlock GetTextBlock(string Property, double Left, double Top)
    {
        return new()
        {
            Text = Property.Length > 14 ? Property[0..14] : Property,
            Margin = new Thickness(Left, Top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }
    public static TextBox GetTextBox(object SourceBinding, string Property, double Left, double Top)
    {
        TextBox Box = new()
        {
            Width = 30,
            Margin = new Thickness(Left, Top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Binding Binding = new() { Source = SourceBinding, Path = new PropertyPath(Property), Mode = BindingMode.TwoWay };
        Box.SetBinding(TextBox.TextProperty, Binding);
        return Box;
    }
    public static CheckBox GetCheckBox(object SourceBinding, string NameBox, string Property, double Left, double Top)
    {
        CheckBox CheckBox = new()
        {
            Content = NameBox.Length > 14 ? NameBox[0..14] : NameBox,
            Margin = new Thickness(Left, Top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Binding Binding = new() { Source = SourceBinding, Path = new PropertyPath(Property), Mode = BindingMode.TwoWay };
        CheckBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Binding);
        return CheckBox;
    }
    #endregion

    #region View
    private void ComboBoxToolChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0) ComboBoxScript.ItemsSource = (e.AddedItems[0] as Tool).Scripts;
    }
    private void ShowScriptInfo(object sender, RoutedEventArgs e)
    {
        if (ComboBoxTool.SelectedIndex > -1 && ComboBoxScript.SelectedIndex > -1)
        {
            OrdersInfo.ItemsSource =
                (ComboBoxTool.SelectedItem as Tool).Scripts.SingleOrDefault(x => x == (Script)ComboBoxScript.SelectedItem).Orders;
            OrdersInfo.Items.Refresh();

            TradesInfo.ItemsSource =
                (ComboBoxTool.SelectedItem as Tool).Scripts.SingleOrDefault(x => x == (Script)ComboBoxScript.SelectedItem).Trades;
            TradesInfo.Items.Refresh();
        }
    }
    private void ShowSystemInfo(object sender, RoutedEventArgs e)
    {
        OrdersInfo.ItemsSource = SystemOrders;
        OrdersInfo.Items.Refresh();

        TradesInfo.ItemsSource = SystemTrades;
        TradesInfo.Items.Refresh();
    }
    private void ShowDistributionInfo(object sender, RoutedEventArgs e)
    {
        if (Tools.Count < 1 || Portfolio.Saldo < 1 || Portfolio.Positions == null) return;

        DistributionPlot.Model = Tools.GetPlot(Portfolio.Positions, Portfolio.Saldo,
            (string)ComboBoxDistrib.SelectedItem, (bool)OnlyPosCheckBox.IsChecked,
            (bool)ExcludeBaseCheckBox.IsChecked);
        DistributionPlot.Controller ??= Tool.GetController();

        PortfolioPlot.Model = Portfolio.GetPlot();
        PortfolioPlot.Controller ??= Tool.GetController();
    }

    private void ResizeInfoPanel(object sender, RoutedEventArgs e)
    {
        if ((int)RowInfo.Height.Value == 2) RowInfo.Height = new GridLength(0.5, GridUnitType.Star);
        else if ((int)RowInfo.Height.Value == 1) RowInfo.Height = new GridLength(2, GridUnitType.Star);
        else RowInfo.Height = new GridLength(1, GridUnitType.Star);
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
    #endregion
}
