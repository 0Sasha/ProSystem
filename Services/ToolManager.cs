using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HarfBuzzSharp;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using static ProSystem.Controls;

namespace ProSystem.Services;

internal class ToolManager : IToolManager
{
    private readonly MainWindow Window;
    private readonly AddInformation AddInfo;
    private readonly TradingSystem TradingSystem;
    private readonly IScriptManager ScriptManager;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    private Connector Connector { get => TradingSystem.Connector; }

    public ToolManager(MainWindow window, TradingSystem tradingSystem,
        IScriptManager scriptManager, AddInformation addInfo)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
        ScriptManager = scriptManager ?? throw new ArgumentNullException(nameof(scriptManager));
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
    }

    public void Initialize(Tool tool, TabItem tabTool)
    {
        if (tool.BaseTF < 1) tool.BaseTF = 30;
        tool.Controller = PlotExtensions.GetController();

        UpdateModel(tool);
        ScriptManager.InitializeScripts(tool, tabTool);
        if (tool.BasicSecurity == null && tool.MySecurity.Bars != null || tool.BasicSecurity?.Bars != null)
        {
            foreach (var script in tool.Scripts)
            {
                script.Calculate(tool.BasicSecurity ?? tool.MySecurity);
                ScriptManager.UpdateView(tool, script);
            }
        }
        UpdateModel(tool);
        UpdateMiniModel(tool);

        if (tool.Active)
        {
            tool.BrushState = Theme.Green;
            (tabTool.Content as Grid).Children.OfType<Grid>().Last().
                Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Deactivate tool";
        }
        else
        {
            tool.BrushState = Theme.Red;
            (tabTool.Content as Grid).Children.OfType<Grid>().Last().
                Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Activate tool";
        }
    }

    public void CreateTab(Tool tool)
    {
        Window.TabsTools.Items.Add(GetToolTab(tool));
        UpdateControlGrid(tool);
        Initialize(tool, Window.TabsTools.Items[^1] as TabItem);
    }

    public void UpdateControlGrid(Tool tool)
    {
        Button ActiveButton = new()
        {
            Content = tool.Active ? "Deactivate tool" : "Activate tool",
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(5, 5, 0, 0),
            Width = 90,
            Height = 20
        };
        ActiveButton.Click += new RoutedEventHandler(Window.ChangeActivityTool);

        ComboBox BaseTFBox = new()
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(45, 30, 0, 0),
            Width = 50,
            ItemsSource = new int[] { 1, 5, 15, 30, 60, 120, 240, 360, 720 }
        };
        BaseTFBox.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
            new Binding() { Source = tool, Path = new PropertyPath("BaseTF"), Mode = BindingMode.TwoWay });

        List<string> MyScripts = new();
        foreach (Script Script in tool.Scripts) MyScripts.Add(Script.Name);
        MyScripts.Add("AllScripts");
        MyScripts.Add("Nothing");
        ComboBox ScriptsBox = new()
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(105, 30, 0, 0),
            Width = 90,
            ItemsSource = MyScripts,
            SelectedValue = "AllScripts",
            DataContext = tool
        };
        ScriptsBox.SelectionChanged += UpdateViewTool;

        Border BorderState = new()
        {
            Margin = new Thickness(0, 55, 0, 0),
            Height = 10,
            BorderThickness = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Background = Theme.Orange
        };
        tool.BorderState = BorderState;

        var ControlGrid = (((Window.TabsTools.Items[TradingSystem.Tools.IndexOf(tool)]
            as TabItem).Content as Grid).Children[1] as Grid).Children[0] as Grid;
        ControlGrid.Children.Clear();
        ControlGrid.Children.Add(ActiveButton);
        ControlGrid.Children.Add(GetTextBlock("BaseTF", 5, 33));
        ControlGrid.Children.Add(BaseTFBox);
        ControlGrid.Children.Add(ScriptsBox);

        ControlGrid.Children.Add(BorderState);
        ControlGrid.Children.Add(GetCheckBox(tool, "Stop trading", "StopTrading", 5, 70));
        ControlGrid.Children.Add(GetCheckBox(tool, "Normalization", "UseNormalization", 5, 110));
        ControlGrid.Children.Add(GetCheckBox(tool, "Trade share", "TradeShare", 5, 130));

        ControlGrid.Children.Add(GetCheckBox(tool, "Basic security", "ShowBasicSecurity", 105, 70));
        ControlGrid.Children.Add(GetTextBlock("Wait limit", 105, 110));
        ControlGrid.Children.Add(GetTextBox(tool, "WaitingLimit", 165, 110));
        ControlGrid.Children.Add(GetCheckBox(tool, "Shift balance", "UseShiftBalance", 105, 130));

        if (tool.TradeShare)
        {
            ControlGrid.Children.Add(GetTextBlock("Share fund", 5, 150));
            ControlGrid.Children.Add(GetTextBox(tool, "ShareOfFunds", 65, 150));

            ControlGrid.Children.Add(GetTextBlock("Min lots", 5, 170));
            ControlGrid.Children.Add(GetTextBox(tool, "MinNumberOfLots", 65, 170));

            ControlGrid.Children.Add(GetTextBlock("Max lots", 105, 170));
            ControlGrid.Children.Add(GetTextBox(tool, "MaxNumberOfLots", 165, 170));
        }
        else
        {
            ControlGrid.Children.Add(GetTextBlock("Num lots", 5, 150));
            ControlGrid.Children.Add(GetTextBox(tool, "NumberOfLots", 65, 150));
        }
        if (tool.UseShiftBalance)
        {
            ControlGrid.Children.Add(GetTextBlock("Base balance", 105, 150));
            ControlGrid.Children.Add(GetTextBox(tool, "BaseBalance", 165, 150));
        }

        TextBlock MainBlInfo = GetTextBlock("Main info", 5, 190);
        tool.MainBlockInfo = MainBlInfo;
        ControlGrid.Children.Add(MainBlInfo);

        TextBlock BlInfo = GetTextBlock("Info", 105, 190);
        tool.BlockInfo = BlInfo;
        ControlGrid.Children.Add(BlInfo);
    }

    public async Task ChangeActivityAsync(Tool tool)
    {
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity;
        if (tool.Active)
        {
            tool.Active = false;
            if (Connector.Connection == ConnectionState.Connected)
            {
                await Task.Run(async () =>
                {
                    await Connector.UnsubscribeFromTradesAsync(security);
                    if (basicSecurity != null) await Connector.UnsubscribeFromTradesAsync(basicSecurity);
                });
            }

            tool.BrushState = Theme.Red;
            Window.Dispatcher.Invoke(() =>
            ((Window.TabsTools.Items[TradingSystem.Tools.IndexOf(tool)] as TabItem).Content as Grid).Children.OfType<Grid>()
            .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Activate tool");
        }
        else
        {
            if (Connector.Connection == ConnectionState.Connected)
            {
                if (!await Task.Run(async () =>
                {
                    await RequestBarsAsync(tool);
                    await Connector.OrderSecurityInfoAsync(security);
                    if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
                    {
                        System.Threading.Thread.Sleep(500);
                        if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
                        {
                            AddInfo("Не удалось активировать инструмент, потому что не пришли бары. Попробуйте ещё раз.");
                            return false;
                        }
                    }

                    await Connector.SubscribeToTradesAsync(security);
                    if (basicSecurity != null) await Connector.SubscribeToTradesAsync(basicSecurity);
                    await RequestBarsAsync(tool);
                    return true;
                })) return;
            }
            tool.BrushState = tool.StopTrading ? Theme.Orange : Theme.Green;
            tool.Active = true;
            Window.Dispatcher.Invoke(() =>
            ((Window.TabsTools.Items[TradingSystem.Tools.IndexOf(tool)] as TabItem).Content as Grid).Children.OfType<Grid>()
                .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Deactivate tool");
        }
        tool.Notify();
    }

    public async Task CalculateAsync(Tool tool)
    {
        // Блокировка или ожидание освобождения блокировки
        while (Interlocked.Exchange(ref tool.IsBusy, 1) != 0)
        {
            AddInfo(tool.Name + ": Calculate: Метод используется другим потоком", false);
            Thread.Sleep(500);
        }

        // Пересчёт или ожидание потенциальных данных с сервера
        while (DateTime.Now.AddSeconds(-3) < tool.TimeLastRecalc)
        {
            AddInfo(tool.Name + ": Calculate: Ожидание потенциальных данных с сервера", false);
            Thread.Sleep(250);
        }
        try
        {
            await Calculate(tool);
            AddInfo("Calculate: Выполнены скрипты инструмента: " + tool.Name, false);
        }
        catch (Exception e)
        {
            AddInfo(tool.Name + ": Calculate: Исключение: " + e.Message, notify: true);
            AddInfo("Трассировка стека: " + e.StackTrace);
            if (e.InnerException != null)
            {
                AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
            }
        }

        // Обновление времени последнего пересчёта и следующего
        tool.TimeLastRecalc = DateTime.Now;
        tool.TimeNextRecalc = DateTime.Now.AddSeconds(TradingSystem.Settings.RecalcInterval / 2);

        // Освобождение блокировки
        Interlocked.Exchange(ref tool.IsBusy, 0);
    }

    private async Task Calculate(Tool tool)
    {
        if (!await CheckTool(tool)) return;
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity ?? security;
        var scripts = tool.Scripts;
        double[] Close = security.Bars.Close;

        bool ReadyToTrade = !tool.StopTrading;
        bool NowLogging = CheckNeedLogging(tool);
        bool NowBidding = CheckStateSession(tool);

        // Вычисление универсальных индикаторов
        double[] SmallATR = Indicators.ATR(security.Bars.High, security.Bars.Low, security.Bars.Close, 150);
        double Average = Math.Round(Close[(Close.Length - 1 - 30)..(Close.Length - 1)].Average(), security.Decimals);
        bool NormalPrice = Math.Abs(Average - Close[^1]) < SmallATR[^2] * 10;
        if (!NormalPrice) AddInfo(tool.Name + ": the price is out of range", notify: true);

        // Ожидание определённости заявок и позиции
        WaitCertainty(tool);

        // Проверка портфеля, вычисление рублёвых требований и оптимальных объёмов позиций
        CheckPortfolio(ref ReadyToTrade);
        var RubReqs = GetAndCheckRubReqs(tool, ref ReadyToTrade);
        (int Long, int Short) PosVolumes = GetPositionVolumes(tool, RubReqs, out (double, double) ClearVolumes);

        // Получение текущей позиции и вычисление объёмов заявок
        int Balance = GetAndCheckBalance(tool, PosVolumes, ref ReadyToTrade, ref tool.triggerPosition, out int RealBalance);
        (int Long, int Short) OrderVolumes = scripts.Length == 1 || Balance == 0 ?
            (Math.Abs(Balance) + PosVolumes.Long, Math.Abs(Balance) + PosVolumes.Short) :
            (Math.Abs(Balance), Math.Abs(Balance));

        // Обновление информации на контрольной панели и логирование риск-параметров
        UpdateControlPanel(tool, Balance, RealBalance, NowBidding, ReadyToTrade,
            RubReqs, ClearVolumes, PosVolumes, OrderVolumes, Average, SmallATR[^2]);
        if (NowLogging) WriteLogRisks(tool, Balance, RealBalance, tool.StopTrading,
            NowBidding, ReadyToTrade, RubReqs, ClearVolumes, PosVolumes, OrderVolumes);

        // Идентификация заявок, проверка соответствия общей позиции позициям скриптов и нормализация общей позиции
        ScriptManager.IdentifyOrdersAndTrades(tool);
        IdentifySystemOrdersAndTrades(tool);
        if (!tool.StopTrading)
        {
            if (!CancelUnknownsOrders(tool)) return;
            if (ReadyToTrade)
            {
                foreach (Script MyScript in scripts) await ScriptManager.UpdateOrdersAndPositionAsync(MyScript);
                if (!CheckPositionMatching(tool, Balance, PosVolumes, NowBidding, NormalPrice)) return;
                await NormalizePosition(tool, Balance, PosVolumes, ClearVolumes, NowBidding);
            }
        }
        else if (!CancelActiveOrders(tool)) return;

        // Работа со скриптами
        foreach (Script script in scripts)
        {
            // Обновление заявок и позиции скрипта, вычисление индикаторов на основе базисного актива
            if (!await ScriptManager.CalculateAsync(script, basicSecurity)) continue;

            // Обновление моделей, информационной панели скрипта и логирование
            ScriptManager.UpdateView(tool, script);
            if (NowLogging) ScriptManager.WriteLog(script, tool.Name);

            // Выравнивание данных и проверка условий для выхода
            if (basicSecurity != security && !ScriptManager.AlignData(tool, script)) continue;
            if (!ReadyToTrade || script.Result.Type != ScriptType.StopLine && !NowBidding) continue;

            // Работа с заявками
            if (!NormalPrice) continue;
            var volume = script.CurrentPosition == PositionType.Long ? OrderVolumes.Short : OrderVolumes.Long;
            await ScriptManager.ProcessOrdersAsync(tool, script, volume, NowBidding, SmallATR[^2]);
        }
    }

    public async Task RequestBarsAsync(Tool tool)
    {
        TimeFrame tf;
        if (Connector.TimeFrames.Count > 0)
            tf = Connector.TimeFrames.Last(x => x.Period / 60 <= tool.BaseTF);
        else { AddInfo("RequestBars: пустой массив таймфреймов."); return; }

        int count = 25;
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity;
        if (basicSecurity != null)
        {
            if (basicSecurity.SourceBars == null || basicSecurity.SourceBars.Close.Length < 500 ||
                basicSecurity.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
            tool.BaseTF != basicSecurity.SourceBars.TF && tool.BaseTF != basicSecurity.Bars.TF) count = 10000;

            await Connector.OrderHistoricalDataAsync(basicSecurity, tf, count);
        }

        count = 25;
        if (security.SourceBars == null || security.SourceBars.Close.Length < 500 ||
            security.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
        tool.BaseTF != security.SourceBars.TF && tool.BaseTF != security.Bars.TF) count = 10000;

        await Connector.OrderHistoricalDataAsync(security, tf, count);
    }

    public async Task ReloadBarsAsync(Tool tool)
    {
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity;
        if (tool.Active)
        {
            while (Connector.Connection == ConnectionState.Connected &&
                !TradingSystem.ReadyToTrade) await Task.Delay(100);
            await ChangeActivityAsync(tool);
            await Task.Delay(250);
        }
        security.SourceBars = null;
        security.Bars = null;
        if (basicSecurity != null)
        {
            basicSecurity.SourceBars = null;
            basicSecurity.Bars = null;
        }

        if (Connector.Connection == ConnectionState.Connected)
        {
            await RequestBarsAsync(tool);
            await System.Threading.Tasks.Task.Delay(500);
            if (security.Bars != null) UpdateView(tool, true);
        }
    }

    public void UpdateBars(Tool tool, bool updateBasicSecurity)
    {
        var sec = tool.MySecurity;
        var basicSec = tool.BasicSecurity;
        if (updateBasicSecurity)
        {
            if (basicSec.SourceBars.TF == tool.BaseTF) basicSec.Bars = basicSec.SourceBars;
            else basicSec.Bars = basicSec.SourceBars.Compress(tool.BaseTF);
        }
        else
        {
            if (sec.SourceBars.TF == tool.BaseTF) sec.Bars = sec.SourceBars;
            else sec.Bars = sec.SourceBars.Compress(tool.BaseTF);
        }
    }

    public void UpdateView(Tool tool, bool updateScriptView)
    {
        try
        {
            if (updateScriptView)
            {
                if (tool.MainModel == null) UpdateModel(tool);
                foreach (var script in tool.Scripts)
                {
                    script.Calculate(tool.BasicSecurity ?? tool.MySecurity);
                    ScriptManager.UpdateView(tool, script);
                }
            }
            UpdateModel(tool);
            if (tool.Model != null) UpdateMiniModel(tool);
        }
        catch (Exception ex)
        {
            AddInfo("UpdateView: " + tool.Name + ": Исключение: " + ex.Message);
            AddInfo("Трассировка стека: " + ex.StackTrace);
            if (ex.InnerException != null)
            {
                AddInfo("Внутреннее исключение: " + ex.InnerException.Message);
                AddInfo("Трассировка стека внутреннего исключения: " + ex.InnerException.StackTrace);
            }
        }
    }

    public void UpdateModel(Tool tool)
    {
        if (tool.MySecurity.Bars == null || tool.ShowBasicSecurity && tool.BasicSecurity.Bars == null) return;
        Bars bars = tool.ShowBasicSecurity ? tool.BasicSecurity.Bars.GetCopy() : tool.MySecurity.Bars.GetCopy();

        List<double> gridLines = new();
        HighLowItem[] items = new HighLowItem[bars.Close.Length];
        items[0] = new HighLowItem(0, bars.High[0], bars.Low[0], bars.Open[0], bars.Close[0]);
        for (int i = 1; i < bars.Close.Length; i++)
        {
            items[i] = new HighLowItem(i, bars.High[i], bars.Low[i], bars.Open[i], bars.Close[i]);
            if (bars.DateTime[i].Date != bars.DateTime[i - 1].Date) gridLines.Add(i);
        }

        var model = tool.MainModel;
        int range = model?.Axes[0].ActualMaximum > 10 ?
            (int)(model.Axes[0].ActualMaximum - model.Axes[0].ActualMinimum) : 100;
        DateTimeAxis xAxis = new()
        {
            Position = AxisPosition.Bottom,
            LabelFormatter = (value) =>
            {
                if (value > 1 && value < bars.Close.Length)
                {
                    if (bars.DateTime[(int)value].Date == bars.DateTime[(int)value - 1].Date)
                        return bars.DateTime[(int)value].ToString("HH:mm", IC);
                    else return bars.DateTime[(int)value].ToString("dd.MM.yy HH:mm", IC);
                }
                else return "";
            },
            Maximum = items[^1].X + 5,
            Minimum = items[^1].X + 5 - range > 1 ? items[^1].X + 5 - range : 1,
            ExtraGridlines = gridLines.ToArray()
        };
        LinearAxis yAxis = new() { Position = AxisPosition.Right };
        CandleStickSeries Candles = new()
        {
            ItemsSource = items,
            TrackerFormatString = "High: {3:0.0000}\nLow: {4:0.0000}\nOpen: {5:0.0000}\nClose: {6:0.0000}"
        };

        Theme.Color(xAxis);
        Theme.Color(yAxis);
        Theme.Color(Candles);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (int i = Math.Max((int)xAxis.Minimum, 0); i < items.Length; i++)
        {
            yMin = Math.Min(yMin, items[i].Low);
            yMax = Math.Max(yMax, items[i].High);
        }
        double Margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - Margin, yMax + Margin);

        if (model != null)
        {
            model.Axes[0].AxisChanged -= tool.Handler;
            model.Axes[0].AxisChanged -= tool.MiniHandler;
        }
        tool.Handler = (s, a) => ScaleModel(Candles, xAxis, yAxis);
        xAxis.AxisChanged += tool.Handler;

        if (model == null)
        {
            tool.MainModel = new() { PlotMargins = new OxyThickness(0, 0, 50, 20) };
            model = tool.MainModel;
            Theme.Color(model);
            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);
            model.Series.Add(Candles);
        }
        else
        {
            model.Axes[0] = xAxis;
            model.Axes[1] = yAxis;
            model.Series[0] = Candles;
        }
        model.InvalidatePlot(true);
    }

    public void UpdateMiniModel(Tool tool, Script script = null)
    {
        var model = tool.Model;
        var mainModel = tool.MainModel;

        double[] Gridlines = null;
        List<DataPoint> Points = new();
        List<Series> ListSeries = new();
        if (script != null)
        {
            if (script.Result.Centre != -1)
                Gridlines = new double[] { script.Result.Centre + script.Result.Level, script.Result.Centre - script.Result.Level };

            List<OxyColor> colors = Theme.Indicators.ToList();
            foreach (double[] Indicator in script.Result.Indicators)
            {
                if (Indicator != null)
                {
                    Points = new();
                    for (int i = 0; i < Indicator.Length; i++) Points.Add(new DataPoint(i, Indicator[i]));
                    ListSeries.Add(new LineSeries() { ItemsSource = Points, Title = script.Name, Color = colors[0] });
                    if (colors.Count > 1) colors.RemoveAt(0);
                }
            }
            if (ListSeries.Count > 1) Points = (ListSeries[0] as LineSeries).ItemsSource as List<DataPoint>;
        }
        else if (model?.Series.Count > 0)
        {
            Points = (model.Series[0] as LineSeries).ItemsSource as List<DataPoint>;
            Gridlines = model.Axes[1].ExtraGridlines;
        }
        else return;

        int Range = mainModel.Axes[0].Maximum > 5 ? (int)(mainModel.Axes[0].Maximum - mainModel.Axes[0].Minimum) : 250;
        LinearAxis xAxis = new()
        {
            Position = AxisPosition.Bottom,
            IsZoomEnabled = false,
            IsPanEnabled = false,
            IsAxisVisible = false,
            Maximum = Points.Count + 4,
            Minimum = Points.Count + 4 - Range > 1 ? Points.Count + 4 - Range : 1
        };
        LinearAxis yAxis = new()
        {
            Position = AxisPosition.Right,
            IsPanEnabled = false,
            ExtraGridlines = Gridlines
        };
        Theme.Color(xAxis, false);
        Theme.Color(yAxis, false);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (int i = (int)xAxis.Minimum; i < Points.Count; i++)
        {
            yMin = Math.Min(yMin, Points[i].Y);
            yMax = Math.Max(yMax, Points[i].Y);
        }
        double Margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - Margin, yMax + Margin);

        mainModel.Axes[0].AxisChanged -= tool.MiniHandler;
        tool.MiniHandler =
            (s, a) => ScaleMiniModel(Points.ToArray(), mainModel.Axes[0], xAxis, yAxis, Window, tool.Model);
        mainModel.Axes[0].AxisChanged += tool.MiniHandler;

        if (model == null)
        {
            tool.Model = new() { PlotMargins = new OxyThickness(0, 0, 50, 0) };
            model = tool.Model;
            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);
            Theme.Color(model);
        }
        else
        {
            if (ListSeries.Count > 0) model.Series.Clear();
            model.Axes[0] = xAxis;
            model.Axes[1] = yAxis;
        }

        foreach (Series MySeries in ListSeries) model.Series.Add(MySeries);
        model.InvalidatePlot(true);
    }

    public void UpdateLastTrade(Tool tool, Trade lastTrade)
    {
        var security = tool.MySecurity.Seccode == lastTrade.Seccode ? tool.MySecurity : tool.BasicSecurity;
        security.LastTrade = lastTrade;
        var bars = security.Bars;

        if (lastTrade.DateTime < bars.DateTime[^1].AddMinutes(bars.TF))
        {
            bars.Close[^1] = lastTrade.Price;
            if (lastTrade.Price > bars.High[^1]) bars.High[^1] = lastTrade.Price;
            else if (lastTrade.Price < bars.Low[^1]) bars.Low[^1] = lastTrade.Price;
            bars.Volume[^1] += lastTrade.Quantity;
        }
        else if (DateTime.Now > security.LastTrDT)
        {
            security.LastTrDT = DateTime.Now.AddSeconds(10);
            tool.TimeNextRecalc = DateTime.Now.AddSeconds(30);

            if (DateTime.Now.Date == bars.DateTime[^1].Date) bars.DateTime =
                    bars.DateTime.Concat(new DateTime[] { bars.DateTime[^1].AddMinutes(bars.TF) }).ToArray();
            else bars.DateTime =
                    bars.DateTime.Concat(new DateTime[] { DateTime.Now.Date.AddHours(DateTime.Now.Hour) }).ToArray();
            bars.Open = bars.Open.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.High = bars.High.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.Low = bars.Low.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.Close = bars.Close.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.Volume = bars.Volume.Concat(new double[] { lastTrade.Quantity }).ToArray();

            Task.Run(async () =>
            {
                await Task.Delay(250);
                var lastExecuted = TradingSystem.Orders.ToArray()
                    .LastOrDefault(x => x.Seccode == tool.MySecurity.Seccode && x.Status == "matched");

                if (lastExecuted != null && lastExecuted.DateTime.AddSeconds(3) > DateTime.Now)
                {
                    AddInfo(tool.Name + ": an order is executed during the bar opening. Waiting.", false);
                    await Task.Delay(2000);
                }
                else if (tool.MySecurity.Seccode == security.Seccode)
                {
                    var active = TradingSystem.Orders.ToArray()
                        .Where(x => x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
                    if (active.Any(x => Math.Abs(x.Price - security.LastTrade.Price) < 0.00001))
                    {
                        AddInfo(tool.Name + ": active order price equals bar opening. Waiting.", false);
                        await Task.Delay(2000);
                    }
                }

                await CalculateAsync(tool);
                tool.MainModel.InvalidatePlot(true);
                await RequestBarsAsync(tool);
            });
        }
    }

    private TabItem GetToolTab(Tool tool)
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

    private void UpdateViewTool(object sender, SelectionChangedEventArgs e)
    {
        Tool MyTool = (sender as ComboBox).DataContext as Tool;
        Task.Run(() => UpdateView(MyTool, true));
    }

    private void IdentifySystemOrdersAndTrades(Tool tool)
    {
        var systemOrders = TradingSystem.SystemOrders;
        var unknownsOrders = TradingSystem.Orders.ToArray()
            .Where(x => x.Sender == null && x.Seccode == tool.MySecurity.Seccode);
        foreach (Order UnknowOrder in unknownsOrders)
        {
            int i = Array.FindIndex(systemOrders.ToArray(), x => x.TrID == UnknowOrder.TrID);
            if (i > -1)
            {
                UnknowOrder.Sender = systemOrders[i].Sender;
                UnknowOrder.Signal = systemOrders[i].Signal;
                UnknowOrder.Note = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => systemOrders[i] = UnknowOrder);
            }
        }

        var UnknownsTrades = TradingSystem.Trades.ToArray()
            .Where(x => x.SenderOrder == null && x.Seccode == tool.MySecurity.Seccode);
        foreach (Trade UnknowTrade in UnknownsTrades)
        {
            int i = Array.FindIndex(systemOrders.ToArray(), x => x.OrderNo == UnknowTrade.OrderNo);
            if (i > -1)
            {
                UnknowTrade.SenderOrder = systemOrders[i].Sender;
                UnknowTrade.SignalOrder = systemOrders[i].Signal;
                UnknowTrade.NoteOrder = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => TradingSystem.SystemTrades.Add(UnknowTrade));
            }
        }
    }

    private async Task<bool> CheckTool(Tool tool)
    {
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity;
        if (security.LastTrade == null || security.LastTrade.DateTime < DateTime.Now.AddDays(-5))
        {
            AddInfo(tool.Name +
                ": Последняя сделка не актуальна или её не существует. Подписка на сделки и выход.", notify: true);
            await Connector.SubscribeToTradesAsync(security);
            return false;
        }
        else if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
        {
            AddInfo(tool.Name + ": Базовых баров не существует. Запрос баров и выход.", notify: true);
            await RequestBarsAsync(tool);
            return false;
        }
        else if (basicSecurity == null && security.Bars.Close.Length < 200 ||
            basicSecurity != null && (security.Bars.Close.Length < 200 || basicSecurity.Bars.Close.Length < 200))
        {
            string Counts = security.Bars.Close.Length.ToString();
            if (basicSecurity != null) Counts += "/" + basicSecurity.Bars.Close.Length;
            AddInfo(tool.Name + ": Недостаточно базовых баров: " + Counts + " Запрос баров и выход.", notify: true);
            await RequestBarsAsync(tool);
            return false;
        }
        else if (tool.Scripts.Length > 2)
        {
            AddInfo(tool.Name + ": Непредвиденное количество скриптов: " + tool.Scripts.Length, notify: true);
            return false;
        }
        return true;
    }

    private bool CheckStateSession(Tool tool)
    {
        if (DateTime.Now > DateTime.Today.AddMinutes(839).AddSeconds(55) && DateTime.Now < DateTime.Today.AddMinutes(845)) { }
        else if (DateTime.Now > DateTime.Today.AddMinutes(1124).AddSeconds(55) && DateTime.Now < DateTime.Today.AddMinutes(1145))
        {
            if (tool.MySecurity.LastTrade.DateTime > DateTime.Today.AddMinutes(1125)) return true;
        }
        else if (DateTime.Now < DateTime.Today.AddHours(1)) { }
        else if (tool.MySecurity.LastTrade.DateTime.AddHours(1) > DateTime.Now) return true;
        return false;
    }

    private bool CheckNeedLogging(Tool tool)
    {
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity;

        if (DateTime.Now.Second >= 30 &&
            (DateTime.Now.Minute == 0 || DateTime.Now.Minute == 29 || DateTime.Now.Minute == 30 || DateTime.Now.Minute == 59))
        {
            try
            {
                string Path = "Logs/LogsTools/" + tool.Name + ".txt";
                if (!System.IO.File.Exists(Path)) System.IO.File.Create(Path).Close();

                string Data = DateTime.Now.ToString(IC) + ": /////////////////// RECOUNT SCRIPTS" +
                    "\nLastTrade " + security.LastTrade.Price.ToString(IC) +
                    "\nDateLastTrade " + security.LastTrade.DateTime.ToString(IC) + "\n";

                if (security.Bars != null)
                    Data += "OHLCV[^1] " + security.Bars.DateTime[^1] + "/" +
                        security.Bars.Open[^1] + "/" + security.Bars.High[^1] + "/" +
                        security.Bars.Low[^1] + "/" + security.Bars.Close[^1] + "/" + security.Bars.Volume[^1] + "\n";

                if (basicSecurity != null && basicSecurity.Bars != null)
                    Data += "BasicOHLCV[^1] " + basicSecurity.Bars.DateTime[^1] + "/" +
                        basicSecurity.Bars.Open[^1] + "/" + basicSecurity.Bars.High[^1] + "/" +
                        basicSecurity.Bars.Low[^1] + "/" + basicSecurity.Bars.Close[^1] + "/" + basicSecurity.Bars.Volume[^1] + "\n";

                System.IO.File.AppendAllText(Path, Data);
                return true;
            }
            catch (Exception e) { AddInfo(tool.Name + ": Исключение логирования: " + e.Message); }
        }
        return false;
    }

    private void WaitCertainty(Tool tool)
    {
        Order[] Undefined = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == tool.MySecurity.Seccode && (x.Status is "forwarding" or "inactive")).ToArray();
        if (Undefined.Length > 0)
        {
            AddInfo(tool.Name + ": Неопределённый статус заявки: " + Undefined[0].Status);

            Thread.Sleep(500);
            if (!Undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;

            Thread.Sleep(1000);
            if (!Undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;

            Thread.Sleep(1500);
            if (!Undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;

            AddInfo(tool.Name + ": Не удалось вовремя получить определённый статус заявки.");
        }

        Trade LastTrade = TradingSystem.Trades.ToArray().LastOrDefault(x => x.Seccode == tool.MySecurity.Seccode);
        if (LastTrade != null && LastTrade.DateTime.AddSeconds(2) > DateTime.Now) Thread.Sleep(1500);
    }

    private void CheckPortfolio(ref bool ReadyToTrade)
    {
        if (DateTime.Now > DateTime.Today.AddMinutes(840) && DateTime.Now < DateTime.Today.AddMinutes(845)) return;
        if (!TradingSystem.PortfolioManager.CheckEquity()) ReadyToTrade = false;
    }

    private (double, double) GetAndCheckRubReqs(Tool tool, ref bool ReadyToTrade)
    {
        var security = tool.MySecurity;
        var usdrub = Connector.USDRUB;
        var eurrub = Connector.EURRUB;
        (double, double) RubReqs = (0, 0);
        if (security.Market != "7") RubReqs = (security.InitReqLong, security.InitReqShort);
        else
        {
            if (usdrub < 0.1 || eurrub < 0.1)
            {
                AddInfo(tool.Name + ": Запрос USDRUB и EURRUB", notify: true);
                Task.Run(async () =>
                {
                    await Connector.OrderHistoricalDataAsync(new("CETS", "USD000UTSTOM"), new("1"), 1);
                    await Connector.OrderHistoricalDataAsync(new("CETS", "EUR_RUB__TOM"), new("1"), 1);
                });
                ReadyToTrade = false;
            }
            else if (security.Currency == "USD")
                RubReqs = (security.InitReqLong * usdrub, security.InitReqShort * usdrub);
            else if (security.Currency == "EUR")
                RubReqs = (security.InitReqLong * eurrub, security.InitReqShort * eurrub);
            else
            {
                AddInfo(tool.Name + ": Неизвестная валюта: " + security.Currency, notify: true);
                ReadyToTrade = false;
            }
        }

        if (RubReqs.Item1 < 10 || RubReqs.Item2 < 10 || security.SellDeposit < 10 || RubReqs.Item1 < security.SellDeposit / 2)
        {
            AddInfo(tool.Name + ": Требования за пределами нормы: " +
                RubReqs.Item1 + "/" + RubReqs.Item2 + " SellDep: " + security.SellDeposit, true, true);
            Task.Run(async () => await Connector.OrderSecurityInfoAsync(security));
            ReadyToTrade = false;
        }
        return RubReqs;
    }

    private (int, int) GetPositionVolumes(Tool tool, (double, double) RubReqs, out (double, double) ClearVolumes)
    {
        var settings = TradingSystem.Settings;
        var portfolio = TradingSystem.Portfolio;
        int LongVolume, ShortVolume;
        double MaxShare = portfolio.Saldo / 100 * settings.MaxShareInitReqsPosition;

        if (tool.TradeShare)
        {
            double OptShare = portfolio.Saldo / 100 * tool.ShareOfFunds;
            if (OptShare > MaxShare)
            {
                AddInfo(tool.Name + ": ShareOfFunds превышает допустимый объём риска: " +
                    settings.MaxShareInitReqsPosition.ToString(IC) + "%", settings.DisplaySpecialInfo, true);
                OptShare = MaxShare;
            }

            LongVolume = (int)Math.Floor(OptShare / RubReqs.Item1);
            if (LongVolume > tool.MaxNumberOfLots) LongVolume = tool.MaxNumberOfLots;
            if (LongVolume < tool.MinNumberOfLots) LongVolume = tool.MinNumberOfLots;

            ShortVolume = (int)Math.Floor(OptShare / RubReqs.Item2);
            if (ShortVolume > tool.MaxNumberOfLots) ShortVolume = tool.MaxNumberOfLots;
            if (ShortVolume < tool.MinNumberOfLots) ShortVolume = tool.MinNumberOfLots;

            if (LongVolume * RubReqs.Item1 > MaxShare)
            {
                AddInfo(tool.Name + ": LongVolume превышает допустимый объём риска: " +
                    settings.MaxShareInitReqsPosition.ToString(IC) + "%", settings.DisplaySpecialInfo, true);
                LongVolume = (int)Math.Floor(OptShare / RubReqs.Item1);
            }
            if (ShortVolume * RubReqs.Item2 > MaxShare)
            {
                AddInfo(tool.Name + ": ShortVolume превышает допустимый объём риска: " +
                    settings.MaxShareInitReqsPosition.ToString(IC) + "%", settings.DisplaySpecialInfo, true);
                ShortVolume = (int)Math.Floor(OptShare / RubReqs.Item2);
            }
        }
        else if (tool.NumberOfLots * Math.Max(RubReqs.Item1, RubReqs.Item2) > MaxShare)
        {
            AddInfo(tool.Name + ": NumberOfLots превышает допустимый объём риска.", notify: true);
            LongVolume = (int)Math.Floor(MaxShare / RubReqs.Item1);
            ShortVolume = (int)Math.Floor(MaxShare / RubReqs.Item2);
        }
        else
        {
            LongVolume = tool.NumberOfLots;
            ShortVolume = tool.NumberOfLots;
        }

        ClearVolumes = (Math.Round(portfolio.Saldo * 0.01 * tool.ShareOfFunds / RubReqs.Item1, 2),
            Math.Round(portfolio.Saldo * 0.01 * tool.ShareOfFunds / RubReqs.Item2, 2));
        return (LongVolume, ShortVolume);
    }

    private int GetAndCheckBalance(Tool tool, (int, int) PosVolumes, ref bool ReadyToTrade, ref DateTime TriggerPosition, out int RealBalance)
    {
        int Balance = 0;
        Position MyPosition = TradingSystem.Portfolio.Positions
            .ToArray().SingleOrDefault(x => x.Seccode == tool.MySecurity.Seccode);
        if (MyPosition != null) Balance = (int)MyPosition.Saldo;

        RealBalance = Balance;
        if (tool.UseShiftBalance) Balance -= tool.BaseBalance;

        if (Math.Abs(Balance) >
            Math.Max(Math.Max(PosVolumes.Item1, PosVolumes.Item2), 1) * TradingSystem.Settings.TolerancePosition)
        {
            if (DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                ReadyToTrade = false;
                if (TriggerPosition == DateTime.MinValue)
                {
                    AddInfo(tool.Name + ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                    TriggerPosition = DateTime.Now.AddHours(12);
                }
            }
            else
            {
                AddInfo(tool.Name + ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                if (TriggerPosition == DateTime.MinValue)
                {
                    TriggerPosition = DateTime.Now.AddHours(4);
                    ReadyToTrade = false;
                }
                else if (DateTime.Now < TriggerPosition) ReadyToTrade = false;
                else AddInfo(tool.Name + ": Объём текущей позиции всё ещё за пределами допустимого отклонения, но торговля разрешена.");
            }
        }
        else if (TriggerPosition != DateTime.MinValue) TriggerPosition = DateTime.MinValue;
        return Balance;
    }

    private async Task NormalizePosition(Tool tool, int Balance, (int, int) PosVolumes, (double, double) ClearVolumes, bool NowBidding)
    {
        var security = tool.MySecurity;
        var cancelOrder = Connector.CancelOrderAsync;
        var sendOrder = Connector.SendOrderAsync;
        var replaceOrder = Connector.ReplaceOrderAsync;

        var activeOrders = TradingSystem.Orders.ToArray().Where(x => x.Sender == "System" &&
        x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
        if (activeOrders.Length == 0 && !tool.UseNormalization || !NowBidding) return;
        if (activeOrders.Length > 1)
        {
            AddInfo(tool.Name + ": Отмена нескольких активных заявок System: " + activeOrders.Length);
            foreach (Order MyOrder in activeOrders) await cancelOrder(MyOrder);
            return;
        }

        if (TradingSystem.Orders.ToArray().Any(x => x.Sender != "System" && x.Seccode == security.Seccode &&
        x.Status is "active" && (x.Quantity - x.Balance > 0.00001 || x.Note == "PartEx"))) return;

        double gap = Math.Abs(Balance) / 14D;
        bool NeedToNormalizeUp =
            Balance > 0 && Balance + Math.Ceiling(Balance * 0.04) + (gap < 0.5 ? gap : 0.5) < ClearVolumes.Item1 ||
            Balance < 0 && -Balance + Math.Ceiling(-Balance * 0.04) + (gap < 0.5 ? gap : 0.5) < ClearVolumes.Item2;

        Order ActiveOrder = activeOrders.SingleOrDefault();
        if (ActiveOrder != null)
        {
            if ((ActiveOrder.BuySell == "B") == Balance < 0 && (Balance > PosVolumes.Item1 || -Balance > PosVolumes.Item2))
            {
                foreach (Script MyScript in tool.Scripts)
                    if (MyScript.ActiveOrder != null && Math.Abs(MyScript.ActiveOrder.Price - security.Bars.Close[^2]) < 0.00001)
                    {
                        AddInfo(tool.Name +
                            ": Отмена заявки для нормализации, скрипт уже выставил заявку с ценой закрытия прошлого бара.");
                        await cancelOrder(ActiveOrder);
                        return;
                    }

                int Volume = Balance > PosVolumes.Item1 ? Balance - PosVolumes.Item1 : -Balance - PosVolumes.Item2;
                if (Math.Abs(ActiveOrder.Price - security.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || ActiveOrder.Balance != Volume)
                    await replaceOrder(ActiveOrder, security, OrderType.Limit,
                        security.Bars.Close[^2], Volume, "Normalization", null, "NM");
            }
            else if ((ActiveOrder.BuySell == "B") == Balance > 0 && NeedToNormalizeUp)
            {
                foreach (Script MyScript in tool.Scripts)
                    if (MyScript.ActiveOrder != null && Math.Abs(MyScript.ActiveOrder.Price - security.Bars.Close[^2]) < 0.00001)
                    {
                        AddInfo(tool.Name +
                            ": Отмена заявки для нормализации, скрипт уже выставил заявку с ценой закрытия прошлого бара.");
                        await cancelOrder(ActiveOrder);
                        return;
                    }

                int Volume = Balance > 0 ? PosVolumes.Item1 - Balance : PosVolumes.Item2 + Balance;
                if (Math.Abs(ActiveOrder.Price - security.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || ActiveOrder.Balance != Volume)
                    await replaceOrder(ActiveOrder, security,
                        OrderType.Limit, security.Bars.Close[^2], Volume, "NormalizationUp", null, "NM");
            }
            else await cancelOrder(ActiveOrder);
        }
        else if (Balance > PosVolumes.Item1 || -Balance > PosVolumes.Item2)
        {
            foreach (Script MyScript in tool.Scripts)
                if (MyScript.ActiveOrder != null && Math.Abs(MyScript.ActiveOrder.Price - security.Bars.Close[^2]) < 0.00001)
                {
                    AddInfo(tool.Name + ": Требуется нормализация, но скрипт уже выставил заявку с ценой закрытия прошлого бара.", false);
                    return;
                }

            int Volume = Balance > PosVolumes.Item1 ? Balance - PosVolumes.Item1 : -Balance - PosVolumes.Item2;
            await sendOrder(security, OrderType.Limit, Balance < 0, security.Bars.Close[^2], Volume, "Normalization", null, "NM");
            WriteLogNM(tool, Balance, PosVolumes);
        }
        else if (NeedToNormalizeUp)
        {
            foreach (Script MyScript in tool.Scripts)
                if (MyScript.ActiveOrder != null &&
                    (MyScript.ActiveOrder.Quantity - MyScript.ActiveOrder.Balance > 0.00001 || MyScript.ActiveOrder.Note == "PartEx" ||
                    Math.Abs(MyScript.ActiveOrder.Price - security.Bars.Close[^2]) < 0.00001)) return;

            foreach (Script MyScript in tool.Scripts)
            {
                Order LastExecuted = MyScript.Orders.LastOrDefault(x => x.Status == "matched");
                if (LastExecuted != null &&
                    (LastExecuted.DateTime.AddDays(4) > DateTime.Now || Balance > 0 == security.Bars.Close[^2] < LastExecuted.Price))
                {
                    int Volume = Balance > 0 ? PosVolumes.Item1 - Balance : PosVolumes.Item2 + Balance;
                    await sendOrder(security, OrderType.Limit,
                        Balance > 0, security.Bars.Close[^2], Volume, "NormalizationUp", null, "NM");
                    WriteLogNM(tool, Balance, PosVolumes);
                    return;
                }
            }
        }
    }

    private bool CheckPositionMatching(Tool tool, int Balance, (int, int) PosVolumes, bool NowBidding, bool NormalPrice)
    {
        var Long = PositionType.Long;
        var Short = PositionType.Short;
        var Neutral = PositionType.Neutral;

        var security = tool.MySecurity;
        var scripts = tool.Scripts;
        var name = tool.Name;
        var cancelOrder = Connector.CancelOrderAsync;
        var sendOrder = Connector.SendOrderAsync;
        var settings = TradingSystem.Settings;
        var orders = TradingSystem.Orders;

        if (scripts.Length == 1)
        {
            // Проверка частичного исполнения заявки
            if (scripts[0].ActiveOrder != null && (scripts[0].ActiveOrder.Quantity - scripts[0].ActiveOrder.Balance > 0.00001 ||
                scripts[0].ActiveOrder.Note == "PartEx")) return true;

            // Проверка соответствия позиций
            PositionType CurPosition = scripts[0].CurrentPosition;
            if (CurPosition == Neutral && Balance != 0 ||
                CurPosition == Long && Balance <= 0 || CurPosition == Short && Balance >= 0)
            {
                if (!NowBidding || !NormalPrice)
                {
                    AddInfo(name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                        settings.DisplaySpecialInfo, true);
                    return true;
                }
                AddInfo(name + ": Позиция скрипта не соответствует позиции в портфеле. Нормализация по рынку.", notify: true);

                Order[] ActiveOrders =
                    orders.ToArray().Where(x => x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
                foreach (Order MyOrder in ActiveOrders) cancelOrder(MyOrder);

                int VolumeOrder;
                bool IsBuy = CurPosition == Neutral ? Balance < 0 : CurPosition == Long;
                if (CurPosition == Long) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item1;
                else if (CurPosition == Short) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item2;
                else VolumeOrder = Math.Abs(Balance);

                sendOrder(security, OrderType.Market,
                    IsBuy, security.Bars.Close[^2], VolumeOrder, "BringingIntoLine", null, "NM");
                return false;
            }
        }
        else if (scripts.Length == 2)
        {
            // Проверка частичного исполнения заявок
            foreach (Script MyScript in scripts)
                if (MyScript.ActiveOrder != null &&
                    (MyScript.ActiveOrder.Quantity - MyScript.ActiveOrder.Balance > 0.00001 ||
                    MyScript.ActiveOrder.Note == "PartEx")) return true;

            // Проверка соответствия позиций
            PositionType CurPosition1 = scripts[0].CurrentPosition;
            PositionType CurPosition2 = scripts[1].CurrentPosition;
            if (CurPosition1 == CurPosition2)
            {
                if (CurPosition1 == Neutral && Balance != 0 ||
                    CurPosition1 == Long && Balance <= 0 || CurPosition1 == Short && Balance >= 0)
                {
                    if (!NowBidding || !NormalPrice)
                    {
                        AddInfo(name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                            settings.DisplaySpecialInfo, true);
                        return true;
                    }
                    AddInfo(name +
                        ": Текущие позиции скриптов не соответствуют позиции в портфеле. Нормализация по рынку.", notify: true);

                    Order[] ActiveOrders = orders.ToArray()
                        .Where(x => x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
                    foreach (Order MyOrder in ActiveOrders) cancelOrder(MyOrder);

                    int VolumeOrder;
                    bool IsBuy = CurPosition1 == Neutral ? Balance < 0 : CurPosition1 == Long;
                    if (CurPosition1 == Long) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item1;
                    else if (CurPosition1 == Short) VolumeOrder = Math.Abs(Balance) + PosVolumes.Item2;
                    else VolumeOrder = Math.Abs(Balance);

                    sendOrder(security, OrderType.Market,
                        IsBuy, security.Bars.Close[^2], VolumeOrder, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else if (CurPosition1 == Long && CurPosition2 == Short || CurPosition1 == Short && CurPosition2 == Long)
            {
                if (Balance != 0)
                {
                    if (!NowBidding || !NormalPrice)
                    {
                        AddInfo(name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                            settings.DisplaySpecialInfo, true);
                        return true;
                    }
                    AddInfo(name +
                        ": Текущие позиции скриптов не соответствуют позиции в портфеле. Нормализация по рынку.", notify: true);

                    Order[] ActiveOrders = orders.ToArray()
                        .Where(x => x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
                    foreach (Order MyOrder in ActiveOrders) cancelOrder(MyOrder);

                    sendOrder(security, OrderType.Market,
                        Balance < 0, security.Bars.Close[^2], Math.Abs(Balance), "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else // Одна из позиций Neutral
            {
                PositionType CurPos = CurPosition1 == Neutral ? CurPosition2 : CurPosition1;
                if (CurPos == Long && Balance <= 0 || CurPos == Short && Balance >= 0)
                {
                    if (!NowBidding || !NormalPrice)
                    {
                        AddInfo(name + ": Несоответствие позиции, но торги не ведутся или цена за пределами нормы.",
                            settings.DisplaySpecialInfo, true);
                        return true;
                    }
                    AddInfo(name +
                        ": Текущие позиции скриптов не соответствуют позиции в портфеле. Нормализация по рынку.", notify: true);

                    Order[] ActiveOrders = orders.ToArray()
                        .Where(x => x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
                    foreach (Order MyOrder in ActiveOrders) cancelOrder(MyOrder);

                    int VolumeOrder = CurPos == Long ? Math.Abs(Balance) + PosVolumes.Item1 : Math.Abs(Balance) + PosVolumes.Item2;
                    sendOrder(security, OrderType.Market,
                        CurPos == Long, security.Bars.Close[^2], VolumeOrder, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
        }
        return true;
    }

    private bool CancelActiveOrders(Tool tool)
    {
        Order[] active = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == tool.MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
        if (active.Length == 0) return true;

        AddInfo(tool.Name + ": Отмена всех активных заявок: " + active.Length);
        foreach (Order MyOrder in active) Connector.CancelOrderAsync(MyOrder);

        Thread.Sleep(500);
        if (!active.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1000);
        if (!active.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1500);
        if (!active.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(2000);
        if (!active.Where(x => x.Status is "active" or "watching").Any()) return true;

        AddInfo(tool.Name + ": Не удалось вовремя отменить все активные заявки");
        return false;
    }

    private bool CancelUnknownsOrders(Tool tool)
    {
        Order[] Unknowns = TradingSystem.Orders.ToArray()
            .Where(x => x.Sender == null && x.Seccode == tool.MySecurity.Seccode && (x.Status is "active" or "watching")).ToArray();
        if (Unknowns.Length == 0) return true;

        AddInfo(tool.Name + ": Отмена неизвестных активных заявок: " + Unknowns.Length);
        foreach (Order MyOrder in Unknowns) Connector.CancelOrderAsync(MyOrder);

        Thread.Sleep(500);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1000);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(1500);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        Thread.Sleep(2000);
        if (!Unknowns.Where(x => x.Status is "active" or "watching").Any()) return true;

        AddInfo(tool.Name + ": Не удалось вовремя отменить неизвестные активные заявки");
        return false;
    }

    private void UpdateControlPanel(Tool tool, int Balance, int RealBalance, bool NowBidding, bool ReadyToTrade, (double, double) RubReqs,
        (double, double) ClearVols, (int, int) PosVolumes, (int, int) OrderVolumes, double Average, double SmallATR)
    {
        (tool.MainModel.Series[0] as OxyPlot.Series.CandleStickSeries).DecreasingColor =
            NowBidding && (!tool.ShowBasicSecurity || tool.ShowBasicSecurity && tool.BasicSecurity.LastTrade.DateTime.AddHours(2) > DateTime.Now) ?
            Theme.RedBar : Theme.FadedBar;
        tool.MainModel.InvalidatePlot(false);

        Window.Dispatcher.Invoke(() =>
        {
            tool.BorderState.Background = ReadyToTrade ? Theme.Green : Theme.Orange;

            tool.MainBlockInfo.Text = "\nReq " + Math.Round(RubReqs.Item1) + "/" + Math.Round(RubReqs.Item2) +
            "\nVols " + PosVolumes.Item1 + "/" + PosVolumes.Item2 + "\nOrderVols " + OrderVolumes.Item1 + "/" + OrderVolumes.Item2 +
            "\nLastTr " + tool.MySecurity.LastTrade.DateTime.TimeOfDay.ToString();

            tool.BlockInfo.Text = "\nBal/Real " + Balance + "/" + RealBalance + "\nClearV " + ClearVols.Item1 + "/" + ClearVols.Item2 +
            "\nSMA " + Average + "\n10ATR " + Math.Round(SmallATR * 10, tool.MySecurity.Decimals);
        });
    }

    private void WriteLogRisks(Tool tool, int Balance, int RealBalance, bool StopTrading, bool NowBidding, bool ReadyToTrade,
        (double, double) RubReqs, (double, double) ClearVols, (double, double) PosVolumes, (double, double) OrderVolumes)
    {
        try
        {
            System.IO.File.AppendAllText("Logs/LogsTools/" + tool.Name + ".txt", DateTime.Now + ": /////////////////// Risks" +
                "\nBalance " + Balance + "\nRealBalance " + RealBalance +
                "\nUseShiftBalance " + tool.UseShiftBalance + "\nBaseBalance " + tool.BaseBalance +
                "\nStopTrading " + StopTrading + "\nNowBidding " + NowBidding + "\nReadyToTrade " + ReadyToTrade +
                "\nPortfolio.Saldo " + TradingSystem.Portfolio.Saldo + "\nShareOfFunds " + tool.ShareOfFunds +
                "\nRubReqs " + RubReqs.Item1 + "/" + RubReqs.Item2 + "\nClearVols " + ClearVols.Item1 + "/" + ClearVols.Item2 +
                "\nPosVols " + PosVolumes.Item1 + "/" + PosVolumes.Item2 +
                "\nOrderVol " + OrderVolumes.Item1 + "/" + OrderVolumes.Item2 +
                "\nMinLots " + tool.MinNumberOfLots + "\nMaxLots " + tool.MaxNumberOfLots + "\n");
        }
        catch (Exception e) { AddInfo(tool.Name + ": Исключение логирования рисков: " + e.Message); }
    }

    private void WriteLogNM(Tool tool, int Balance, (int, int) PosVolumes)
    {
        try
        {
            System.IO.File.AppendAllText("Logs/LogsTools/" + tool.Name + ".txt", DateTime.Now + ": /////////////////// NM" +
                "\nBalance " + Balance + "\nUseShiftBalance " + tool.UseShiftBalance + "\nBaseBalance " + tool.BaseBalance +
                "\nReserateLong " + tool.MySecurity.ReserateLong + "\nReserateShort " + tool.MySecurity.ReserateShort +
                "\nInitReqLong " + tool.MySecurity.InitReqLong + "\nInitReqShort " + tool.MySecurity.InitReqShort +
                "\nPortfolio.Saldo " + TradingSystem.Portfolio.Saldo + "\nShareOfFunds " + tool.ShareOfFunds +
                "\nPosVols " + PosVolumes.Item1 + "/" + PosVolumes.Item2 +
                "\nMinLots " + tool.MinNumberOfLots + "\nMaxLots " + tool.MaxNumberOfLots + "\n");
        }
        catch (Exception e) { AddInfo(tool.Name + ": Исключение логирования NM: " + e.Message); }
    }

    private static void ScaleModel(CandleStickSeries candles, DateTimeAxis xAxis, LinearAxis yAxis)
    {
        int i = candles.FindByX(xAxis.ActualMinimum);
        int xEnd = candles.FindByX(xAxis.ActualMaximum, i);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (; i <= xEnd; i++)
        {
            yMin = Math.Min(yMin, candles.Items[i].Low);
            yMax = Math.Max(yMax, candles.Items[i].High);
        }

        double Margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - Margin, yMax + Margin);
    }

    private static void ScaleMiniModel(DataPoint[] Points, Axis MainAxis,
        LinearAxis xAxis, LinearAxis yAxis, MainWindow window, PlotModel model)
    {
        int Difference = Points.Length + 4 - (int)MainAxis.Maximum;
        xAxis.Minimum = MainAxis.ActualMinimum + Difference;
        xAxis.Maximum = MainAxis.ActualMaximum + Difference;

        int i = Math.Max((int)xAxis.Minimum, 0);
        int xEnd = Math.Min((int)xAxis.Maximum, Points.Length - 1);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (; i <= xEnd; i++)
        {
            yMin = Math.Min(yMin, Points[i].Y);
            yMax = Math.Max(yMax, Points[i].Y);
        }

        double Margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - Margin, yMax + Margin);

        window.Dispatcher.Invoke(() => model.InvalidatePlot(false));
    }
}
