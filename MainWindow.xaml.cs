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
    private async void ClosingMainWindow(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Подтверждение выхода и сохранение данных
        MessageBoxResult Res = MessageBox.Show("Are you sure you want to exit?", "Closing", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (Res == MessageBoxResult.No || !SaveData(true))
        {
            e.Cancel = true;
            return;
        }

        // Подготовка потока проверки условий
        CheckingInterval = 1000;
        System.Threading.Thread.Sleep(2000);

        // Отсоединение
        if (Connection != ConnectionState.Disconnected) await Task.Run(() => Disconnect());

        // Выход
        if (!Window.ConnectorInitialized || ConnectorUnInitialize()) { Logger.StopLogging(); return; }
        else MessageBox.Show("UnInitialization failed.");
    }
    private void SendCommand(object sender, RoutedEventArgs e)
    {
        if (ComboBox.Text == "SendEmail") SendEmail("Проблема на сервере.");
        else if (ComboBox.Text == "Test")
        {

        }
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
                for (k = 0; k < Tools[i].Scripts[j].MyOrders.Count; k++)
                {
                    if (Tools[i].Scripts[j].MyOrders[k].DateTime.Date < DateTime.Today.AddDays(-MySettings.ShelfLifeOrdersScripts)) continue;
                    else break;
                }
                if (k > 0)
                {
                    Tools[i].Scripts[j].MyOrders = new ObservableCollection<Order>(Tools[i].Scripts[j].MyOrders.ToArray()[k..]);
                    AddInfo("ClearOutdatedData: удалены устаревшие заявки скрипта: " + Tools[i].Scripts[j].Name);
                }

                for (k = 0; k < Tools[i].Scripts[j].MyTrades.Count; k++)
                {
                    if (Tools[i].Scripts[j].MyTrades[k].DateTime.Date < DateTime.Today.AddDays(-MySettings.ShelfLifeTradesScripts)) continue;
                    else break;
                }
                if (k > 0)
                {
                    Tools[i].Scripts[j].MyTrades = new ObservableCollection<Trade>(Tools[i].Scripts[j].MyTrades.ToArray()[k..]);
                    AddInfo("ClearOutdatedData: удалены устаревшие сделки скрипта: " + Tools[i].Scripts[j].Name);
                }
            }
        }
    }
    private bool SaveData(bool SaveInfoPanel = false)
    {
        AddInfo("SaveData: Сериализация", false);
        bool Result = true;
        var Formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();

        if (Directory.Exists("Data"))
        {
            try
            {
                File.Copy("Data/Tools.bin", "Data/Tools copy.bin", true);
                using Stream MyStream = new FileStream("Data/Tools.bin", FileMode.Create, FileAccess.Write, FileShare.None);
                Formatter.Serialize(MyStream, Tools);
                File.Delete("Data/Tools copy.bin");
            }
            catch (Exception e)
            {
                AddInfo("Исключение сериализации инструментов: " + e.Message, SendEmail: true);
                if (File.Exists("Data/Tools copy.bin"))
                {
                    System.Threading.Thread.Sleep(3000);
                    try
                    {
                        File.Move("Data/Tools copy.bin", "Data/Tools.bin", true);
                        AddInfo("Исходный файл восстановлен.");
                    }
                    catch { AddInfo("Исходный файл не восстановлен."); }
                }
                else AddInfo("Исходный файл не восстановлен.");
                Result = false;
            } // Tools
            try
            {
                File.Copy("Data/Settings.bin", "Data/Settings copy.bin", true);
                using Stream MyStream = new FileStream("Data/Settings.bin", FileMode.Create, FileAccess.Write, FileShare.None);
                Formatter.Serialize(MyStream, MySettings);
                File.Delete("Data/Settings copy.bin");
            }
            catch (Exception e)
            {
                AddInfo("Исключение сериализации настроек: " + e.Message, SendEmail: true);
                if (File.Exists("Data/Settings copy.bin"))
                {
                    System.Threading.Thread.Sleep(3000);
                    try
                    {
                        File.Move("Data/Settings copy.bin", "Data/Settings.bin", true);
                        AddInfo("Исходный файл восстановлен.");
                    }
                    catch { AddInfo("Исходный файл не восстановлен."); }
                }
                else AddInfo("Исходный файл не восстановлен.");
                Result = false;
            } // Settings
            try
            {
                File.Copy("Data/Trades.bin", "Data/Trades copy.bin", true);
                using Stream MyStream = new FileStream("Data/Trades.bin", FileMode.Create, FileAccess.Write, FileShare.None);
                Formatter.Serialize(MyStream, Trades);
                File.Delete("Data/Trades copy.bin");
            }
            catch (Exception e)
            {
                AddInfo("Исключение сериализации сделок: " + e.Message);
                if (File.Exists("Data/Trades copy.bin"))
                {
                    System.Threading.Thread.Sleep(3000);
                    try
                    {
                        File.Move("Data/Trades copy.bin", "Data/Trades.bin", true);
                        AddInfo("Исходный файл восстановлен.");
                    }
                    catch { AddInfo("Исходный файл не восстановлен."); }
                }
                else AddInfo("Исходный файл не восстановлен.");
                Result = false;
            } // Trades
        }
        else
        {
            Directory.CreateDirectory("Data");
            try
            {
                using Stream MyStream = new FileStream("Data/Tools.bin", FileMode.Create, FileAccess.Write, FileShare.None);
                Formatter.Serialize(MyStream, Tools);
            }
            catch (Exception e)
            {
                AddInfo("Исключение сериализации инструментов: " + e.Message);
                Result = false;
            }
            try
            {
                using Stream MyStream = new FileStream("Data/Settings.bin", FileMode.Create, FileAccess.Write, FileShare.None);
                Formatter.Serialize(MyStream, MySettings);
            }
            catch (Exception e)
            {
                AddInfo("Исключение сериализации настроек: " + e.Message);
                Result = false;
            }
            try
            {
                using Stream MyStream = new FileStream("Data/Trades.bin", FileMode.Create, FileAccess.Write, FileShare.None);
                Formatter.Serialize(MyStream, Trades);
            }
            catch (Exception e)
            {
                AddInfo("Исключение сериализации сделок: " + e.Message);
                Result = false;
            }
        }
        if (SaveInfoPanel)
        {
            try { Dispatcher.Invoke(() => File.WriteAllText("Data/Info.txt", TxtBox.Text)); }
            catch (Exception e) { AddInfo("Исключение записи информационной панели: " + e.Message); Result = false; }
        }

        TriggerSerialization = DateTime.Now.AddSeconds(30);
        return Result;
    }
    private void SaveData(object sender, RoutedEventArgs e)
    {
        if (SaveData(true)) AddInfo("Данные сохранены.");
        else AddInfo("Данные не сохранены.");
    }
    private void SaveLoginDetails(object sender, RoutedEventArgs e)
    {
        if (TxtEmail.Text.Length > 0 && TxtPasEmail.Password.Length > 0)
        {
            MySettings.Email = TxtEmail.Text;
            MySettings.EmailPassword = TxtPasEmail.Password;
            if (SaveData())
            {
                TxtEmail.Clear();
                TxtPasEmail.Clear();
                AddInfo("Данные почты сохранены.");
            }
        }
    }

    public void UpdatePortfolio()
    {
        Dispatcher.Invoke(() =>
        {
            PortfolioView.ItemsSource = new List<object>(MoneyPositions.Concat(Positions)) { Portfolio };
            if (DateTime.Now > TriggerSerialization) SaveData();
        });
    }
    private void UpdateTools(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ToolsView.Items.Refresh();
        if (DateTime.Now > TriggerSerialization) Dispatcher.Invoke(() => SaveData());
    }
    private void UpdateOrders(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OrdersView.Items.Refresh();
        if (OrdersView.Items.Count > 0) OrdersView.ScrollIntoView(OrdersView.Items[^1]);
    }
    private void UpdateTrades(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        TradesView.Items.Refresh();
        TradesView.ScrollIntoView(TradesView.Items[^1]);
    }

    public static void AddInfo(string Data, bool Important = true, bool SendEmail = false)
    {
        Logger.WriteLogSystem(Data);
        if (Important)
        {
            Window.Dispatcher.Invoke(() =>
            {
                Window.TxtBox.AppendText("\n" + DateTime.Now.ToString("dd.MM HH:mm:ss", IC) + ": " + Data);
                Window.TxtBox.ScrollToEnd();
            });
        }
        if (SendEmail && DateTime.Now > TriggerNotification) MainWindow.SendEmail(Data);
    }
    public static void SendEmail(string Data)
    {
        TriggerNotification = DateTime.Now.AddHours(4);
        Task.Run(() =>
        {
            System.Net.Mail.SmtpClient Smtp = new("smtp.gmail.com", 587);
            System.Net.Mail.MailMessage Message = new(MySettings.Email, MySettings.Email, "Info", Data);

            Smtp.EnableSsl = true;
            Smtp.Credentials = new System.Net.NetworkCredential(MySettings.Email, MySettings.EmailPassword);
            while (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) System.Threading.Thread.Sleep(15000);
            try
            {
                Smtp.Send(Message);
                AddInfo("Оповещение отправлено.");
            }
            catch (Exception e)
            {
                AddInfo("Повторная попытка отправки оповещения через 10 минут. Исключение: " + e.Message);
                System.Threading.Thread.Sleep(600000);
                Task.Run(() => SendEmail(Data));
            }
            finally
            {
                Smtp.Dispose();
                Message.Dispose();
            }
        });
    }
    public static void WriteLogTaskException(object sender, UnobservedTaskExceptionEventArgs args)
    {
        Exception[] MyExceptions = args.Exception.InnerExceptions.ToArray();
        string Data = "Task Exception:";
        foreach (Exception e in MyExceptions) Data += "\n" + e.Message + "\n" + e.StackTrace;
        AddInfo(Data, true, true);
    }
    public static void WriteLogUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        Exception e = (Exception)args.ExceptionObject;
        string Path = "UnhandledException " + DateTime.Now.ToString("dd.MM.yyyy HH.mm.ss", IC) + ".txt";
        string Data = e.Message + "\n" + e.StackTrace;
        try { File.WriteAllText(Path, Data); }
        catch { }

        System.Net.Mail.SmtpClient Smtp = new("smtp.gmail.com", 587);
        System.Net.Mail.MailMessage Message = new(MySettings.Email, MySettings.Email, "Info", Data);
        try
        {
            Smtp.EnableSsl = true;
            Smtp.Credentials = new System.Net.NetworkCredential(MySettings.Email, MySettings.EmailPassword);
            Smtp.Send(Message);
        }
        finally
        {
            Smtp.Dispose();
            Message.Dispose();
            Logger.WriteLogSystem(Data);
        }
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
    private void ChangeToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            if (Tools[i].Active) Tools[i].ChangeActivity();
            NewTool NewTool = new(Tools[i]);
            NewTool.Show();
        }
    }
    private void ReloadBarsToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            MessageBoxResult Res = MessageBox.Show("Are you sure you want to reload bars of " + Tools[i].Name + "?", "Reloading bars", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (Res == MessageBoxResult.No) return;
            Tools[i].ReloadBars();
        }
    }
    private void WriteSourceBarsToolContext(object sender, RoutedEventArgs e)
    {
        if (ToolsView.SelectedItem != null)
        {
            int i = Tools.IndexOf(Tools.Single(x => x == ToolsView.SelectedItem));
            Bars.WriteBars(Tools[i].MySecurity.SourceBars, Tools[i].MySecurity.ShortName);
            if (Tools[i].BasicSecurity != null) Bars.WriteBars(Tools[i].BasicSecurity.SourceBars, Tools[i].BasicSecurity.ShortName);
        }
    }
    private void RemoveToolContext(object sender, RoutedEventArgs e)
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
            if (Tools[i].Active) Tools[i].ChangeActivity();
            TabsTools.Items.Remove(TabsTools.Items[i]);

            // Удаление инструмента
            MySettings.ToolsByPriority.Remove(Tools[i].Name);
            ToolsByPriorityView.Items.Refresh();
            if (Tools.Remove(Tools[i])) AddInfo("Удалён инструмент: " + ToolName);
            else AddInfo("Не получилось удалить инструмент: " + ToolName);
        }
    }

    private void RaisePriorityTool(object sender, RoutedEventArgs e)
    {
        int i = ToolsByPriorityView.SelectedIndex;
        if (i > 0)
        {
            string ReplaceableTool = MySettings.ToolsByPriority[i - 1];
            MySettings.ToolsByPriority[i - 1] = MySettings.ToolsByPriority[i];
            MySettings.ToolsByPriority[i] = ReplaceableTool;
            ToolsByPriorityView.Items.Refresh();
        }
    }
    private void LowerPriorityTool(object sender, RoutedEventArgs e)
    {
        int i = ToolsByPriorityView.SelectedIndex;
        if (i > -1 && i < MySettings.ToolsByPriority.Count - 1)
        {
            string ReplaceableTool = MySettings.ToolsByPriority[i + 1];
            MySettings.ToolsByPriority[i + 1] = MySettings.ToolsByPriority[i];
            MySettings.ToolsByPriority[i] = ReplaceableTool;
            ToolsByPriorityView.Items.Refresh();
        }
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
                    if (MyOrder.Sender != null) MyTool.Scripts.Single(x => x.Name == MyOrder.Sender).MyOrders.Remove(MyOrder);
                    else
                    {
                        foreach (IScript MyScript in MyTool.Scripts)
                        {
                            if (MyScript.MyOrders.Contains(MyOrder))
                            {
                                MyScript.MyOrders.Remove(MyOrder);
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
                        if (MyTool.Scripts.Single(x => x.Name == MyOrder.Sender).MyOrders.Remove(MyOrder)) return;
                        else break;
                    }
                }
                AddInfo("Не найдена заявка для удаления.");
            }
        }
    }
    private void UpdateOrdersAndTradesContext(object sender, RoutedEventArgs e)
    {
        OrdersView.Items.Refresh();
        TradesView.Items.Refresh();
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
                        (x => x.Name == MyTrade.SenderOrder).MyTrades.Remove(MyTrade);
                }
                catch (Exception ex) { AddInfo("Исключение во время попытки удаления сделки: " + ex.Message); }
            }
            else
            {
                foreach (Tool MyTool in Tools)
                {
                    if (MyTool.Scripts.SingleOrDefault(x => x.Name == MyTrade.SenderOrder) != null)
                    {
                        if (MyTool.Scripts.Single(x => x.Name == MyTrade.SenderOrder).MyTrades.Remove(MyTrade)) return;
                        else break;
                    }
                }
                AddInfo("Не найдена сделка для удаления.");
            }
        }
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
            TabsTools.Items.Add(new TabItem()
            {
                Header = Tools[^1].Name,
                Width = 48,
                Height = 18,
                Content = GetGridTabTool(Tools[^1])
            });
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
                RequestBars(Tools[k]);
                GetSecurityInfo(Tools[k].MySecurity.Market, Tools[k].MySecurity.Seccode);
                System.Threading.Thread.Sleep(2000);

                GetClnSecPermissions(Tools[k].MySecurity.Board, Tools[k].MySecurity.Seccode, Tools[k].MySecurity.Market);
            });
        }
        else AddInfo("SaveTool: отсутствует соединение.");

        if (Tools[k].MySecurity.Bars != null) UpdateModels(Tools[k]);
        AddInfo("Saved tool: " + Tools[k].Name);
    }
    private void ChangeActivityTool(object sender, RoutedEventArgs e)
    {
        Button MyButton = sender as Button;
        MyButton.IsEnabled = false;
        if (MyButton.Content == null) (MyButton.DataContext as Tool).ChangeActivity();
        else Tools[TabsTools.SelectedIndex].ChangeActivity();
        MyButton.IsEnabled = true;
    }
    private void UpdateModelsAndPanel(object sender, SelectionChangedEventArgs e)
    {
        Tool MyTool = (sender as ComboBox).DataContext as Tool;
        Task.Run(() =>
        {
            UpdateModels(MyTool);
            foreach (IScript Script in MyTool.Scripts)
                MyTool.UpdateModelsAndPanel(Script);
        });
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

        PlotGrid.Children.Add(PlotView);
        PlotGrid.Children.Add(MainPlotView);


        Grid ControlGrid = new();
        Grid.SetColumn(ControlGrid, 1);
        ControlGrid.RowDefinitions.Add(new() { Height = new GridLength(1.4, GridUnitType.Star) });
        ControlGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        ControlGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });

        Grid ControlGrid1 = new();
        UpdateControlGrid(MyTool, ControlGrid1);
        Grid ControlGrid2 = new();
        Grid.SetRow(ControlGrid2, 1);
        Grid ControlGrid3 = new();
        Grid.SetRow(ControlGrid3, 2);

        Border Border = new() { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1) };
        Border Border1 = new() { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1) };
        Grid.SetRow(Border1, 1);
        Border Border2 = new() { BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1) };
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
        foreach (IScript Script in MyTool.Scripts) MyScripts.Add(Script.Name);
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
        ScriptsBox.SelectionChanged += UpdateModelsAndPanel;

        Border BorderState = new()
        {
            Margin = new Thickness(0, 55, 0, 0),
            Height = 10,
            BorderBrush = System.Windows.Media.Brushes.LightGray,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Background = System.Windows.Media.Brushes.Yellow
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
                (ComboBoxTool.SelectedItem as Tool).Scripts.SingleOrDefault(x => x == (IScript)ComboBoxScript.SelectedItem).MyOrders;
            OrdersInfo.Items.Refresh();

            TradesInfo.ItemsSource =
                (ComboBoxTool.SelectedItem as Tool).Scripts.SingleOrDefault(x => x == (IScript)ComboBoxScript.SelectedItem).MyTrades;
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
        if (Tools.Count < 1 || Portfolio.Saldo < 1) return;

        var Assets = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Left };
        var FactVol = new OxyPlot.Series.BarSeries { BarWidth = 3, StrokeColor = OxyPlot.OxyColors.Black, StrokeThickness = 1 };
        var MaxVol = new OxyPlot.Series.BarSeries { FillColor = OxyPlot.OxyColors.Black, StrokeColor = OxyPlot.OxyColors.Black, StrokeThickness = 1 };
        var Axis = new OxyPlot.Axes.LinearAxis
        {
            Position = OxyPlot.Axes.AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = new double[]
            {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
                16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30
            },
            ExtraGridlineColor = OxyPlot.OxyColors.LightGray
        };

        double FactReq, MaxReq;
        double SumMaxVol = 0;
        string Filter = (string)ComboBoxDistrib.SelectedItem;
        for (int i = Tools.Count - 1; i >= 0; i--)
        {
            if (Tools[i].BaseBalance == 0) MaxReq = Tools[i].ShareOfFunds;
            else MaxReq = Tools[i].BaseBalance > 0 ?
                Tools[i].ShareOfFunds + (Tools[i].BaseBalance * Tools[i].MySecurity.InitReqLong / Portfolio.Saldo * 100) :
                Tools[i].ShareOfFunds + (-Tools[i].BaseBalance * Tools[i].MySecurity.InitReqShort / Portfolio.Saldo * 100);

            SumMaxVol += MaxReq;
            if (Filter == "All tools" ||
                Filter == "First part" && i < Tools.Count / 2 || Filter == "Second part" && i >= Tools.Count / 2)
            {
                Position Pos = Positions.SingleOrDefault(x => x.Seccode == Tools[i].MySecurity.Seccode);
                if (Pos != null && Math.Abs(Pos.Saldo) > 0.0001)
                {
                    int shift = (bool)ExcludeBaseCheckBox.IsChecked ? Tools[i].BaseBalance : 0;
                    FactReq = (Pos.Saldo > 0 ? (Pos.Saldo - shift) * Tools[i].MySecurity.InitReqLong :
                        (-Pos.Saldo - shift) * Tools[i].MySecurity.InitReqShort) / Portfolio.Saldo * 100;
                    FactVol.Items.Add(new OxyPlot.Series.BarItem
                    {
                        Value = FactReq,
                        Color = Pos.Saldo - shift > 0 ? OxyPlot.OxyColors.Green : OxyPlot.OxyColors.DarkGoldenrod
                    });
                }
                else if (!(bool)OnlyPosCheckBox.IsChecked) FactVol.Items.Add(new OxyPlot.Series.BarItem { Value = 0 });
                else continue;
                
                Assets.Labels.Add(Tools[i].Name);
                MaxVol.Items.Add(new OxyPlot.Series.BarItem{
                    Value = (bool)ExcludeBaseCheckBox.IsChecked ? Tools[i].ShareOfFunds : MaxReq });
            }
        }

        var Model = new OxyPlot.PlotModel();
        Model.Series.Add(MaxVol);
        Model.Series.Add(FactVol);
        Model.Axes.Add(Assets);
        Model.Axes.Add(Axis);
        Model.PlotMargins = new OxyPlot.OxyThickness(55, 0, 0, 20);
        DistributionPlot.Model = Model;

        var AssetsPorfolio = new OxyPlot.Axes.CategoryAxis { Position = OxyPlot.Axes.AxisPosition.Left };
        var FactVolPorfolio = new OxyPlot.Series.BarSeries { BarWidth = 2, FillColor = OxyPlot.OxyColors.DarkGoldenrod, StrokeColor = OxyPlot.OxyColors.Black, StrokeThickness = 1 };
        var MaxVolPorfolio = new OxyPlot.Series.BarSeries { FillColor = OxyPlot.OxyColors.Black, StrokeColor = OxyPlot.OxyColors.Black, StrokeThickness = 1 };
        var AxisPorfolio = new OxyPlot.Axes.LinearAxis
        {
            Position = OxyPlot.Axes.AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = new double[]
            {
                5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95
            },
            ExtraGridlineColor = OxyPlot.OxyColors.LightGray
        };

        AssetsPorfolio.Labels.Add("Portfolio");
        FactVolPorfolio.Items.Add(new OxyPlot.Series.BarItem { Value = Portfolio.InitReqs / Portfolio.Saldo * 100 });
        MaxVolPorfolio.Items.Add(new OxyPlot.Series.BarItem { Value = SumMaxVol });

        Model = new OxyPlot.PlotModel();
        Model.Series.Add(MaxVolPorfolio);
        Model.Series.Add(FactVolPorfolio);
        Model.Axes.Add(AssetsPorfolio);
        Model.Axes.Add(AxisPorfolio);
        Model.PlotMargins = new OxyPlot.OxyThickness(55, 0, 0, 20);
        PortfolioPlot.Model = Model;
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
