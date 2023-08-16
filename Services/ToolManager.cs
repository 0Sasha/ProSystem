using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Xml.Linq;
using HarfBuzzSharp;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

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

    public TabItem Initialize(Tool tool)
    {
        if (tool.BaseTF < 1) tool.BaseTF = 30;
        tool.Controller ??= Plot.GetController();
        tool.BrushState = tool.Active ? Theme.Green : Theme.Red;

        var tab = new TabItem()
        {
            Name = tool.Name,
            Header = tool.Name,
            Width = 54,
            Height = 24,
            Content = Controls.GetGridForToolTab(tool)
        };
        UpdateControlGrid(tool, tab);
        ScriptManager.InitializeScripts(tool, tab);

        UpdateView(tool, true);
        return tab;
    }

    public void UpdateControlGrid(Tool tool, TabItem tabTool = null)
    {
        var controlGrid = tabTool != null ?
            ((tabTool.Content as Grid).Children[1] as Grid).Children[0] as Grid :
            (((Window.TabsTools.Items[TradingSystem.Tools.IndexOf(tool)]
            as TabItem).Content as Grid).Children[1] as Grid).Children[0] as Grid;
        controlGrid.Children.Clear();
        Controls.FillControlGrid(tool, controlGrid, Window.ChangeActivityTool, UpdateViewTool);
    }

    public async Task ChangeActivityAsync(Tool tool)
    {
        if (Connector.Connection == ConnectionState.Connected)
        {
            var security = tool.Security;
            var basicSecurity = tool.BasicSecurity;
            if (tool.Active)
            {
                _ = Task.Run(async () =>
                {
                    await Connector.UnsubscribeFromTradesAsync(security);
                    if (basicSecurity != null) await Connector.UnsubscribeFromTradesAsync(basicSecurity);
                });
            }
            else
            {
                try
                {
                    await RequestBarsAsync(tool);
                    await Connector.OrderSecurityInfoAsync(security);
                    if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
                    {
                        await Task.Delay(500);
                        if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
                        {
                            AddInfo("Failed to activate the tool because there are no bars");
                            return;
                        }
                    }

                    await Connector.SubscribeToTradesAsync(security);
                    if (basicSecurity != null) await Connector.SubscribeToTradesAsync(basicSecurity);
                    await RequestBarsAsync(tool);
                }
                catch (Exception ex)
                {
                    AddInfo("ChangeActivity: " + ex.Message);
                    AddInfo(ex.StackTrace, false);
                    return;
                }
            }
        }

        tool.Active = !tool.Active;
        tool.BrushState = tool.Active ? (tool.StopTrading ? Theme.Orange : Theme.Green) : Theme.Red;
        var btnContent = tool.Active ? "Deactivate tool" : "Activate tool";

        Window.Dispatcher.Invoke(() => ((Window.TabsTools.Items.OfType<TabItem>().Single(i => i.Name == tool.Name)
            .Content as Grid).Children.OfType<Grid>().Last().Children.OfType<Grid>().First()
            .Children.OfType<Button>().First().Content = btnContent));
        tool.NotifyChange();
    }

    public async Task CalculateAsync(Tool tool)
    {
        while (Interlocked.Exchange(ref tool.IsBusy, 1) != 0)
        {
            AddInfo(tool.Name + ": Calculate: method is occupied by another thread", false);
            await Task.Delay(500);
        }

        while (DateTime.Now.AddSeconds(-3) < tool.TimeLastRecalc)
        {
            AddInfo(tool.Name + ": Calculate: waiting for data from server", false);
            await Task.Delay(1000);
        }

        try
        {
            if (await CheckTool(tool)) await Calculate(tool);
            AddInfo("Calculate: scripts executed: " + tool.Name, false);
        }
        catch (Exception e)
        {
            AddInfo(tool.Name + ": Calculate: " + e.Message, notify: true);
            AddInfo("StackTrace: " + e.StackTrace);
            if (e.InnerException != null)
            {
                AddInfo("Inner exception: " + e.InnerException.Message);
                AddInfo("StackTrace: " + e.InnerException.StackTrace);
            }
        }
        finally
        {
            tool.TimeLastRecalc = DateTime.Now;
            tool.TimeNextRecalc = DateTime.Now.AddSeconds(TradingSystem.Settings.RecalcInterval / 2);
            Interlocked.Exchange(ref tool.IsBusy, 0);
        }
    }

    public async Task RequestBarsAsync(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));
        if (Connector.TimeFrames == null || Connector.TimeFrames.Count == 0)
        {
            AddInfo("RequestBars: Connector.TimeFrames is empty");
            return;
        }

        var tf = Connector.TimeFrames.Last(x => x.Period / 60 <= tool.BaseTF);
        int count = 25;

        var security = tool.Security;
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
        var security = tool.Security;
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
            await Task.Delay(500);
            if (security.Bars != null) UpdateView(tool, true);
        }
    }

    public void UpdateBars(Tool tool, bool updateBasicSecurity)
    {
        var sec = tool.Security;
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
        if (updateScriptView)
        {
            if (tool.MainModel == null) UpdateModel(tool);
            foreach (var script in tool.Scripts)
            {
                script.Calculate(tool.BasicSecurity ?? tool.Security);
                ScriptManager.UpdateView(tool, script);
            }
        }
        UpdateModel(tool);
        if (tool.Model != null) UpdateMiniModel(tool);
    }

    public void UpdateModel(Tool tool)
    {
        if (tool.Security.Bars == null || tool.ShowBasicSecurity && tool.BasicSecurity.Bars == null) return;
        Bars bars = tool.ShowBasicSecurity ? tool.BasicSecurity.Bars.GetCopy() : tool.Security.Bars.GetCopy();

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
        CandleStickSeries candles = new()
        {
            ItemsSource = items,
            TrackerFormatString = "High: {3:0.0000}\nLow: {4:0.0000}\nOpen: {5:0.0000}\nClose: {6:0.0000}"
        };

        Theme.Color(xAxis);
        Theme.Color(yAxis);
        Theme.Color(candles);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (int i = Math.Max((int)xAxis.Minimum, 0); i < items.Length; i++)
        {
            yMin = Math.Min(yMin, items[i].Low);
            yMax = Math.Max(yMax, items[i].High);
        }
        double margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - margin, yMax + margin);

        if (model != null)
        {
            model.Axes[0].AxisChanged -= tool.Handler;
            model.Axes[0].AxisChanged -= tool.MiniHandler;
        }
        tool.Handler = (s, a) => Plot.ScaleModel(candles, xAxis, yAxis);
        xAxis.AxisChanged += tool.Handler;

        if (model == null)
        {
            tool.MainModel = new() { PlotMargins = new OxyThickness(0, 0, 50, 20) };
            model = tool.MainModel;
            Theme.Color(model);
            model.Axes.Add(xAxis);
            model.Axes.Add(yAxis);
            model.Series.Add(candles);
        }
        else
        {
            model.Axes[0] = xAxis;
            model.Axes[1] = yAxis;
            model.Series[0] = candles;
        }
        model.InvalidatePlot(true);
    }

    public void UpdateMiniModel(Tool tool, Script script = null)
    {
        var model = tool.Model;
        var mainModel = tool.MainModel;

        double[] gridlines = null;
        List<DataPoint> points = new();
        List<Series> listSeries = new();
        if (script != null)
        {
            if (script.Result.Centre != -1) gridlines = new double[]
            {
                script.Result.Centre + script.Result.Level,
                script.Result.Centre - script.Result.Level
            };

            List<OxyColor> colors = Theme.Indicators.ToList();
            foreach (var indicator in script.Result.Indicators)
            {
                if (indicator != null)
                {
                    points = new();
                    for (int i = 0; i < indicator.Length; i++) points.Add(new DataPoint(i, indicator[i]));
                    listSeries.Add(new LineSeries()
                    {
                        ItemsSource = points,
                        Title = script.Name,
                        Color = colors[0]
                    });
                    if (colors.Count > 1) colors.RemoveAt(0);
                }
            }
            if (listSeries.Count > 1) points = (listSeries[0] as LineSeries).ItemsSource as List<DataPoint>;
        }
        else if (model?.Series.Count > 0)
        {
            points = (model.Series[0] as LineSeries).ItemsSource as List<DataPoint>;
            gridlines = model.Axes[1].ExtraGridlines;
        }
        else return;

        int range = mainModel.Axes[0].Maximum > 5 ?
            (int)(mainModel.Axes[0].Maximum - mainModel.Axes[0].Minimum) : 250;
        LinearAxis xAxis = new()
        {
            Position = AxisPosition.Bottom,
            IsZoomEnabled = false,
            IsPanEnabled = false,
            IsAxisVisible = false,
            Maximum = points.Count + 4,
            Minimum = points.Count + 4 - range > 1 ? points.Count + 4 - range : 1
        };
        LinearAxis yAxis = new()
        {
            Position = AxisPosition.Right,
            IsPanEnabled = false,
            ExtraGridlines = gridlines
        };
        Theme.Color(xAxis, false);
        Theme.Color(yAxis, false);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (int i = (int)xAxis.Minimum; i < points.Count; i++)
        {
            yMin = Math.Min(yMin, points[i].Y);
            yMax = Math.Max(yMax, points[i].Y);
        }
        double margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - margin, yMax + margin);

        mainModel.Axes[0].AxisChanged -= tool.MiniHandler;
        tool.MiniHandler =
            (s, a) => Plot.ScaleMiniModel(points.ToArray(), mainModel.Axes[0], xAxis, yAxis, Window, tool.Model);
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
            if (listSeries.Count > 0) model.Series.Clear();
            model.Axes[0] = xAxis;
            model.Axes[1] = yAxis;
        }

        foreach (var series in listSeries) model.Series.Add(series);
        model.InvalidatePlot(true);
    }

    public void UpdateLastTrade(Tool tool, Trade lastTrade)
    {
        var security = tool.Security.Seccode == lastTrade.Seccode ? tool.Security : tool.BasicSecurity;
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
                    .LastOrDefault(x => x.Seccode == tool.Security.Seccode && x.Status == "matched");

                if (lastExecuted != null && lastExecuted.DateTime.AddSeconds(3) > DateTime.Now)
                {
                    AddInfo(tool.Name + ": an order is executed during the bar opening. Waiting.", false);
                    await Task.Delay(2000);
                }
                else if (tool.Security.Seccode == security.Seccode)
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


    private async Task<bool> CheckTool(Tool tool)
    {
        var security = tool.Security;
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

    private async Task Calculate(Tool tool)
    {
        await WaitCertainty(tool);

        var toolState = CalculateToolState(tool);
        UpdateControlPanel(tool, toolState);
        UpdateBarsColor(tool, toolState);
        if (toolState.IsLogging) WriteStateLog(tool, toolState);

        ScriptManager.IdentifyOrdersAndTrades(tool);
        IdentifySystemOrdersAndTrades(tool);

        if (!tool.StopTrading)
        {
            if (!await CancelUnknownOrders(tool)) return;
            if (toolState.ReadyToTrade)
            {
                foreach (var script in tool.Scripts) await ScriptManager.UpdateOrdersAndPositionAsync(script);
                if (!await CheckPositionMatching(tool, toolState)) return;
                await NormalizePosition(tool, toolState);
            }
        }
        else if (!await CancelActiveOrders(tool)) return;

        var security = tool.Security;
        var basicSecurity = tool.BasicSecurity ?? security;
        foreach (var script in tool.Scripts)
        {
            // Обновление заявок и позиции скрипта, вычисление индикаторов на основе базисного актива
            if (!await ScriptManager.CalculateAsync(script, basicSecurity)) continue;

            // Обновление моделей, информационной панели скрипта и логирование
            ScriptManager.UpdateView(tool, script);
            if (toolState.IsLogging) ScriptManager.WriteLog(script, tool.Name);

            // Выравнивание данных и проверка условий для выхода
            if (basicSecurity != security && !ScriptManager.AlignData(tool, script)) continue;
            if (!toolState.ReadyToTrade ||
                script.Result.Type != ScriptType.StopLine && !toolState.IsBidding) continue;

            // Работа с заявками
            await ScriptManager.ProcessOrdersAsync(tool, toolState, script);
        }
    }

    private async Task WaitCertainty(Tool tool)
    {
        var undefined = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == tool.Security.Seccode && (x.Status is "forwarding" or "inactive"));
        if (undefined.Any())
        {
            AddInfo(tool.Name + ": uncertain order status: " + undefined.First().Status);
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(300);
                if (!undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;
            }
            AddInfo(tool.Name + ": failed to get certain order status");
        }

        var lastTrade = TradingSystem.Trades.ToArray().LastOrDefault(x => x.Seccode == tool.Security.Seccode);
        if (lastTrade != null && lastTrade.DateTime.AddSeconds(2) > DateTime.Now) await Task.Delay(1500);
    }

    private void IdentifySystemOrdersAndTrades(Tool tool)
    {
        var systemOrders = TradingSystem.SystemOrders;
        var unknownOrders = TradingSystem.Orders.ToArray()
            .Where(x => x.Sender == null && x.Seccode == tool.Security.Seccode);
        foreach (var unknownOrder in unknownOrders)
        {
            int i = Array.FindIndex(systemOrders.ToArray(), x => x.TrID == unknownOrder.TrID);
            if (i > -1)
            {
                unknownOrder.Sender = systemOrders[i].Sender;
                unknownOrder.Signal = systemOrders[i].Signal;
                unknownOrder.Note = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => systemOrders[i] = unknownOrder);
            }
        }

        var unknownTrades = TradingSystem.Trades.ToArray()
            .Where(x => x.SenderOrder == null && x.Seccode == tool.Security.Seccode);
        foreach (var unknownTrade in unknownTrades)
        {
            int i = Array.FindIndex(systemOrders.ToArray(), x => x.OrderNo == unknownTrade.OrderNo);
            if (i > -1)
            {
                unknownTrade.SenderOrder = systemOrders[i].Sender;
                unknownTrade.SignalOrder = systemOrders[i].Signal;
                unknownTrade.NoteOrder = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => TradingSystem.SystemTrades.Add(unknownTrade));
            }
        }
    }


    private ToolState CalculateToolState(Tool tool)
    {
        var readyToTrade = !tool.StopTrading && TradingSystem.PortfolioManager.CheckEquity();
        var average = Math.Round(tool.Security.Bars.Close.TakeLast(30).Average(), tool.Security.Decimals);
        var atr = Indicators.ATR(tool.Security.Bars.High, tool.Security.Bars.Low, tool.Security.Bars.Close, 50);
        var toolState = new ToolState(readyToTrade, IsLogging(tool), IsBidding(tool))
        {
            IsNormalPrice = Math.Abs(average - tool.Security.Bars.Close[^1]) < atr[^2] * 10,
            AveragePrice = average,
            ATR = atr[^2]
        };
        if (!toolState.IsNormalPrice) AddInfo(tool.Name + ": the price is out of range", notify: true);

        SetRubReqs(tool, toolState);
        SetPositionVolumes(tool, toolState);
        SetBalance(tool, toolState);
        SetOrdersVolumes(tool, toolState);
        return toolState;
    }

    private bool IsBidding(Tool tool)
    {
        if (DateTime.Now < DateTime.Today.AddHours(1)) return false;

        if (DateTime.Now > DateTime.Today.AddMinutes(839).AddSeconds(55) &&
            DateTime.Now < DateTime.Today.AddMinutes(845)) return false;

        if (DateTime.Now > DateTime.Today.AddMinutes(1129).AddSeconds(55) &&
            DateTime.Now < DateTime.Today.AddMinutes(1145))
        {
            return tool.Security.LastTrade.DateTime > DateTime.Today.AddMinutes(1130);
        }

        if (tool.Security.LastTrade.DateTime.AddHours(1) > DateTime.Now) return true;
        return false;
    }

    private bool IsLogging(Tool tool)
    {
        var sec = tool.Security;
        var basicSec = tool.BasicSecurity;
        if (DateTime.Now.Second >= 30 && (DateTime.Now.Minute is 0 or 29 or 30 or 59))
        {
            try
            {
                if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
                if (!Directory.Exists("Logs/LogsTools")) Directory.CreateDirectory("Logs/LogsTools");

                var path = "Logs/LogsTools/" + tool.Name + ".txt";
                if (!File.Exists(path)) File.Create(path).Close();

                var data = DateTime.Now.ToString(IC) + ": /////////////////// RECOUNT SCRIPTS" +
                    "\nLastTrade " + sec.LastTrade.Price.ToString(IC) +
                    "\nDateLastTrade " + sec.LastTrade.DateTime.ToString(IC) + "\n";

                if (sec.Bars != null)
                    data += "OHLCV[^1] " + sec.Bars.DateTime[^1] + "/" + sec.Bars.Open[^1] + "/" +
                        sec.Bars.High[^1] + "/" + sec.Bars.Low[^1] + "/" +
                        sec.Bars.Close[^1] + "/" + sec.Bars.Volume[^1] + "\n";

                if (basicSec != null && basicSec.Bars != null)
                    data += "BasicOHLCV[^1] " + basicSec.Bars.DateTime[^1] + "/" + basicSec.Bars.Open[^1] + "/" +
                        basicSec.Bars.High[^1] + "/" + basicSec.Bars.Low[^1] + "/" +
                        basicSec.Bars.Close[^1] + "/" + basicSec.Bars.Volume[^1] + "\n";

                File.AppendAllText(path, data);
                return true;
            }
            catch (Exception e) { AddInfo(tool.Name + ": logging exception: " + e.Message); }
        }
        return false;
    }

    private void SetRubReqs(Tool tool, ToolState toolState)
    {
        var security = tool.Security;
        var mult = 1D;

        if (security.Market == "7")
        {
            if (Connector.USDRUB < 0.1 || Connector.EURRUB < 0.1)
            {
                AddInfo(tool.Name + ": request of USDRUB and EURRUB", notify: true);
                Task.Run(async () =>
                {
                    await Connector.OrderHistoricalDataAsync(new("CETS", "USD000UTSTOM"), new("1"), 1);
                    await Connector.OrderHistoricalDataAsync(new("CETS", "EUR_RUB__TOM"), new("1"), 1);
                });
                toolState.ReadyToTrade = false;
            }
            else if (security.Currency == "USD") mult = Connector.USDRUB;
            else if (security.Currency == "EUR") mult = Connector.EURRUB;
            else
            {
                AddInfo(tool.Name + ": unknown currency: " + security.Currency, notify: true);
                toolState.ReadyToTrade = false;
            }
        }
        toolState.LongReqs = security.InitReqLong * mult;
        toolState.ShortReqs = security.InitReqShort * mult;

        if (toolState.LongReqs < 10 || toolState.ShortReqs < 10 ||
            security.SellDeposit < 10 || toolState.LongReqs < security.SellDeposit / 2)
        {
            AddInfo(tool.Name + ": rub reqs are out of norm: " +
                toolState.LongReqs + "/" + toolState.ShortReqs + " SellDep: " + security.SellDeposit, true, true);
            Task.Run(async () => await Connector.OrderSecurityInfoAsync(security));
            toolState.ReadyToTrade = false;
        }
    }

    private void SetPositionVolumes(Tool tool, ToolState toolState)
    {
        var saldo = TradingSystem.Portfolio.Saldo;
        var maxShare = saldo / 100 * TradingSystem.Settings.MaxShareInitReqsPosition;
        if (tool.TradeShare)
        {
            var settings = TradingSystem.Settings;
            var optShare = saldo / 100 * tool.ShareOfFunds;
            if (optShare > maxShare)
            {
                AddInfo(tool.Name + ": ShareOfFunds превышает допустимый объём риска: " +
                    settings.MaxShareInitReqsPosition.ToString(IC) + "%", settings.DisplaySpecialInfo, true);
                optShare = maxShare;
            }

            int longVol = (int)Math.Floor(optShare / toolState.LongReqs);
            if (longVol < tool.MinNumberOfLots) longVol = tool.MinNumberOfLots;
            if (longVol > tool.MaxNumberOfLots) longVol = tool.MaxNumberOfLots;
            if (longVol * toolState.LongReqs > maxShare)
            {
                AddInfo(tool.Name + ": LongVolume превышает допустимый объём риска: " +
                    settings.MaxShareInitReqsPosition.ToString(IC) + "%", settings.DisplaySpecialInfo, true);
                longVol = (int)Math.Floor(optShare / toolState.LongReqs);
            }

            int shortVol = (int)Math.Floor(optShare / toolState.ShortReqs);
            if (shortVol < tool.MinNumberOfLots) shortVol = tool.MinNumberOfLots;
            if (shortVol > tool.MaxNumberOfLots) shortVol = tool.MaxNumberOfLots;
            if (shortVol * toolState.ShortReqs > maxShare)
            {
                AddInfo(tool.Name + ": ShortVolume превышает допустимый объём риска: " +
                    settings.MaxShareInitReqsPosition.ToString(IC) + "%", settings.DisplaySpecialInfo, true);
                shortVol = (int)Math.Floor(optShare / toolState.ShortReqs);
            }

            toolState.LongVolume = longVol;
            toolState.ShortVolume = shortVol;

            toolState.LongRealVolume = Math.Round(saldo * 0.01 * tool.ShareOfFunds / toolState.LongReqs, 2);
            toolState.ShortRealVolume = Math.Round(saldo * 0.01 * tool.ShareOfFunds / toolState.ShortReqs, 2);
        }
        else
        {
            if (tool.NumberOfLots * Math.Max(toolState.LongReqs, toolState.ShortReqs) > maxShare)
            {
                AddInfo(tool.Name + ": NumberOfLots превышает допустимый объём риска.", notify: true);
                toolState.LongVolume = (int)Math.Floor(maxShare / toolState.LongReqs);
                toolState.ShortVolume = (int)Math.Floor(maxShare / toolState.ShortReqs);
            }
            else
            {
                toolState.LongVolume = tool.NumberOfLots;
                toolState.ShortVolume = tool.NumberOfLots;
            }
            toolState.LongRealVolume = toolState.LongVolume;
            toolState.ShortRealVolume = toolState.ShortVolume;
        }
    }

    private void SetBalance(Tool tool, ToolState toolState)
    {
        toolState.Balance = 0;
        var position = TradingSystem.Portfolio.Positions
            .ToArray().SingleOrDefault(x => x.Seccode == tool.Security.Seccode);
        if (position != null) toolState.Balance = (int)position.Saldo;

        toolState.RealBalance = toolState.Balance;
        if (tool.UseShiftBalance) toolState.Balance -= tool.BaseBalance;

        if (Math.Abs(toolState.Balance) >
            Math.Max(Math.Max(toolState.LongVolume, toolState.ShortVolume), 1) *
            TradingSystem.Settings.TolerancePosition)
        {
            if (DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                toolState.ReadyToTrade = false;
                if (tool.TriggerPosition == DateTime.MinValue)
                {
                    AddInfo(tool.Name +
                        ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                    tool.TriggerPosition = DateTime.Now.AddHours(12);
                }
            }
            else
            {
                AddInfo(tool.Name +
                    ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                if (tool.TriggerPosition == DateTime.MinValue)
                {
                    tool.TriggerPosition = DateTime.Now.AddHours(4);
                    toolState.ReadyToTrade = false;
                }
                else if (DateTime.Now < tool.TriggerPosition) toolState.ReadyToTrade = false;
                else AddInfo(tool.Name +
                    ": Объём текущей позиции всё ещё за пределами допустимого отклонения, но торговля разрешена.");
            }
        }
        else if (tool.TriggerPosition != DateTime.MinValue) tool.TriggerPosition = DateTime.MinValue;
    }

    private void SetOrdersVolumes(Tool tool, ToolState toolState)
    {
        var balance = Math.Abs(toolState.Balance);

        toolState.LongOrderVolume = balance;
        toolState.ShortOrderVolume = balance;
        if (tool.Scripts.Length == 1 || toolState.Balance == 0)
        {
            toolState.LongOrderVolume += toolState.LongVolume;
            toolState.ShortOrderVolume += toolState.ShortVolume;
        }
    }


    private async Task NormalizePosition(Tool tool, ToolState toolState)
    {
        var activeOrders = TradingSystem.Orders.ToArray().Where(x => x.Sender == "System" &&
            x.Seccode == tool.Security.Seccode && (x.Status is "active" or "watching"));
        if (!activeOrders.Any() && !tool.UseNormalization || !toolState.IsBidding) return;
        if (activeOrders.Count() > 1)
        {
            AddInfo(tool.Name + ": cancellation of several active system orders: " + activeOrders.Count());
            foreach (var order in activeOrders) await Connector.CancelOrderAsync(order);
            return;
        }

        if (TradingSystem.Orders.ToArray().Any(x => 
            x.Sender != "System" && x.Seccode == tool.Security.Seccode && x.Status is "active" &&
            (x.Quantity - x.Balance > 0.00001 || x.Note == "PartEx"))) return;

        var security = tool.Security;
        var balance = toolState.Balance;

        double gap = Math.Abs(balance) / 14D;
        bool needToNormalizeUp =
            balance > 0 && 
            balance + Math.Ceiling(balance * 0.04) + (gap < 0.5 ? gap : 0.5) < toolState.LongRealVolume ||
            balance < 0 &&
            -balance + Math.Ceiling(-balance * 0.04) + (gap < 0.5 ? gap : 0.5) < toolState.ShortRealVolume;

        var activeOrder = activeOrders.SingleOrDefault();
        if (activeOrder != null)
        {
            if ((activeOrder.BuySell == "B") == balance < 0 &&
                (balance > toolState.LongVolume || -balance > toolState.ShortVolume))
            {
                if (!await PrepareForNormalization(tool, activeOrder)) return;

                int volume = balance > toolState.LongVolume ?
                    balance - toolState.LongVolume : -balance - toolState.ShortVolume;

                if (Math.Abs(activeOrder.Price - security.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || activeOrder.Balance != volume)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit,
                        security.Bars.Close[^2], volume, "Normalization", null, "NM");
                }
            }
            else if ((activeOrder.BuySell == "B") == balance > 0 && needToNormalizeUp)
            {
                if (!await PrepareForNormalization(tool, activeOrder)) return;

                int volume = balance > 0 ?
                    toolState.LongVolume - balance : toolState.ShortVolume + balance;

                if (Math.Abs(activeOrder.Price - security.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || activeOrder.Balance != volume)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security,
                        OrderType.Limit, security.Bars.Close[^2], volume, "NormalizationUp", null, "NM");
                }
            }
            else await Connector.CancelOrderAsync(activeOrder);
        }
        else if (balance > toolState.LongVolume || -balance > toolState.ShortVolume)
        {
            if (!await PrepareForNormalization(tool)) return;

            int volume = balance > toolState.LongVolume ?
                balance - toolState.LongVolume : -balance - toolState.ShortVolume;

            await Connector.SendOrderAsync(security, OrderType.Limit, balance < 0,
                security.Bars.Close[^2], volume, "Normalization", null, "NM");
            
            WriteStateLog(tool, toolState, "NM");
        }
        else if (needToNormalizeUp)
        {
            foreach (var script in tool.Scripts)
                if (script.ActiveOrder != null &&
                    (script.ActiveOrder.Quantity - script.ActiveOrder.Balance > 0.00001 ||
                    script.ActiveOrder.Note == "PartEx" ||
                    Math.Abs(script.ActiveOrder.Price - security.Bars.Close[^2]) < 0.00001)) return;

            foreach (var script in tool.Scripts)
            {
                var lastExecuted = script.Orders.LastOrDefault(x => x.Status == "matched");
                if (lastExecuted != null &&
                    (lastExecuted.DateTime.AddDays(4) > DateTime.Now ||
                    balance > 0 == security.Bars.Close[^2] < lastExecuted.Price))
                {
                    int volume = balance > 0 ?
                        toolState.LongVolume - balance : toolState.ShortVolume + balance;

                    await Connector.SendOrderAsync(security, OrderType.Limit,
                        balance > 0, security.Bars.Close[^2], volume, "NormalizationUp", null, "NM");

                    WriteStateLog(tool, toolState, "NM");
                    return;
                }
            }
        }
    }

    private async Task<bool> PrepareForNormalization(Tool tool, Order activeOrderNM = null)
    {
        foreach (var script in tool.Scripts)
        {
            if (script.ActiveOrder != null &&
                Math.Abs(script.ActiveOrder.Price - tool.Security.Bars.Close[^2]) < 0.00001)
            {
                AddInfo(tool.Name +
                    ": NM: script has already sent an order with the closing price of the previous bar");
                if (activeOrderNM != null) await Connector.CancelOrderAsync(activeOrderNM);
                return false;
            }
        }
        return true;
    }

    private async Task<bool> CheckPositionMatching(Tool tool, ToolState toolState)
    {
        var Long = PositionType.Long;
        var Short = PositionType.Short;
        var Neutral = PositionType.Neutral;

        var security = tool.Security;
        var scripts = tool.Scripts;
        var balance = toolState.Balance;

        if (scripts.Length == 1)
        {
            // Проверка частичного исполнения заявки
            if (scripts[0].ActiveOrder != null &&
                (scripts[0].ActiveOrder.Quantity - scripts[0].ActiveOrder.Balance > 0.00001 ||
                scripts[0].ActiveOrder.Note == "PartEx")) return true;

            // Проверка соответствия позиций
            var position = scripts[0].CurrentPosition;
            if (position == Neutral && balance != 0 ||
                position == Long && balance <= 0 || position == Short && balance >= 0)
            {
                if (!await PrepareForBringingIntoLine(tool, toolState)) return true;

                int volume;
                bool isBuy = position == Neutral ? balance < 0 : position == Long;
                if (position == Long) volume = Math.Abs(balance) + toolState.LongVolume;
                else if (position == Short) volume = Math.Abs(balance) + toolState.ShortVolume;
                else volume = Math.Abs(balance);

                await Connector.SendOrderAsync(security, OrderType.Market,
                    isBuy, security.Bars.Close[^2], volume, "BringingIntoLine", null, "NM");
                return false;
            }
        }
        else if (scripts.Length == 2)
        {
            // Проверка частичного исполнения заявок
            foreach (var script in scripts)
                if (script.ActiveOrder != null &&
                    (script.ActiveOrder.Quantity - script.ActiveOrder.Balance > 0.00001 ||
                    script.ActiveOrder.Note == "PartEx")) return true;

            // Проверка соответствия позиций
            var position1 = scripts[0].CurrentPosition;
            var position2 = scripts[1].CurrentPosition;
            if (position1 == position2)
            {
                if (position1 == Neutral && balance != 0 ||
                    position1 == Long && balance <= 0 || position1 == Short && balance >= 0)
                {
                    if (!await PrepareForBringingIntoLine(tool, toolState)) return true;

                    int volume;
                    bool isBuy = position1 == Neutral ? balance < 0 : position1 == Long;
                    if (position1 == Long) volume = Math.Abs(balance) + toolState.LongVolume;
                    else if (position1 == Short) volume = Math.Abs(balance) + toolState.ShortVolume;
                    else volume = Math.Abs(balance);

                    await Connector.SendOrderAsync(security, OrderType.Market,
                        isBuy, security.Bars.Close[^2], volume, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else if (position1 == Long && position2 == Short || position1 == Short && position2 == Long)
            {
                if (balance != 0)
                {
                    if (!await PrepareForBringingIntoLine(tool, toolState)) return true;
                    await Connector.SendOrderAsync(security, OrderType.Market,
                        balance < 0, security.Bars.Close[^2], Math.Abs(balance), "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else // Одна из позиций Neutral
            {
                var position = position1 == Neutral ? position2 : position1;
                if (position == Long && balance <= 0 || position == Short && balance >= 0)
                {
                    if (!await PrepareForBringingIntoLine(tool, toolState)) return true;
                    int vol = position == Long ?
                        Math.Abs(balance) + toolState.LongVolume : 
                        Math.Abs(balance) + toolState.ShortVolume;
                    await Connector.SendOrderAsync(security, OrderType.Market,
                        position == Long, security.Bars.Close[^2], vol, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
        }
        else throw new ArgumentException("Unexpected number of scripts", nameof(tool));
        return true;
    }

    private async Task<bool> PrepareForBringingIntoLine(Tool tool, ToolState toolState)
    {
        if (!toolState.IsBidding || !toolState.IsNormalPrice)
        {
            AddInfo(tool.Name + ": position mismatch, but it is not bidding or normal price.",
                TradingSystem.Settings.DisplaySpecialInfo, true);
            return false;
        }
        AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
        await CancelActiveOrders(tool);
        return true;
    }


    private async Task<bool> CancelActiveOrders(Tool tool)
    {
        var active = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == tool.Security.Seccode && (x.Status is "active" or "watching"));
        if (!active.Any()) return true;

        AddInfo(tool.Name + ": cancellation of all active orders: " + active.Count());
        foreach (var order in active) await Connector.CancelOrderAsync(order);

        for (int i = 0; i < 20; i++)
        {
            if (!active.Where(x => x.Status is "active" or "watching").Any()) return true;
            await Task.Delay(250);
        }

        AddInfo(tool.Name + ": failed to cancel all active orders in time");
        return false;
    }

    private async Task<bool> CancelUnknownOrders(Tool tool)
    {
        var unknown = TradingSystem.Orders.ToArray().Where(x => x.Sender == null &&
            x.Seccode == tool.Security.Seccode && (x.Status is "active" or "watching"));
        if (!unknown.Any()) return true;

        AddInfo(tool.Name + ": cancellation of unknown orders: " + unknown.Count());
        foreach (var order in unknown) await Connector.CancelOrderAsync(order);

        for (int i = 0; i < 20; i++)
        {
            if (!unknown.Where(x => x.Status is "active" or "watching").Any()) return true;
            await Task.Delay(250);
        }

        AddInfo(tool.Name + ": failed to cancel unknown orders in time");
        return false;
    }


    private void UpdateControlPanel(Tool tool, ToolState toolState)
    {
        Window.Dispatcher.Invoke(() =>
        {
            tool.BorderState.Background = toolState.ReadyToTrade ? Theme.Green : Theme.Orange;

            tool.MainBlockInfo.Text =
            "\nReq " + Math.Round(toolState.LongReqs) + "/" + Math.Round(toolState.ShortReqs) +
            "\nVols " + toolState.LongVolume + "/" + toolState.ShortVolume +
            "\nOrderVols " + toolState.LongOrderVolume + "/" + toolState.ShortOrderVolume +
            "\nLastTr " + tool.Security.LastTrade.DateTime.TimeOfDay.ToString();

            tool.BlockInfo.Text = "\nBal/Real " + toolState.Balance + "/" + toolState.RealBalance +
            "\nRealV " + toolState.LongRealVolume + "/" + toolState.ShortRealVolume +
            "\nSMA " + toolState.AveragePrice + "\n10ATR " + Math.Round(toolState.ATR * 10, tool.Security.Decimals);
        });
    }

    private void UpdateBarsColor(Tool tool, ToolState toolState)
    {
        (tool.MainModel.Series[0] as CandleStickSeries).DecreasingColor = toolState.IsBidding &&
            (!tool.ShowBasicSecurity ||
            tool.ShowBasicSecurity && tool.BasicSecurity.LastTrade.DateTime.AddHours(2) > DateTime.Now) ?
            Theme.RedBar : Theme.FadedBar;
        tool.MainModel.InvalidatePlot(false);
    }

    private void WriteStateLog(Tool tool, ToolState toolState, string type = "Risks")
    {
        var data = DateTime.Now + ": /////////////// " + type +
            "\nStopTrading " + tool.StopTrading + "\nIsBidding " + toolState.IsBidding +
            "\nReadyToTrade " + toolState.ReadyToTrade + "\nPortfolio.Saldo " + TradingSystem.Portfolio.Saldo +
            "\nBalance " + toolState.Balance + "\nRealBalance " + toolState.RealBalance +
            "\nUseShiftBalance " + tool.UseShiftBalance + "\nBaseBalance " + tool.BaseBalance +
            "\nReserate " + tool.Security.ReserateLong + "/" + tool.Security.ReserateShort +
            "\nInitReq " + tool.Security.InitReqLong + "/" + tool.Security.InitReqShort +
            "\nShareOfFunds " + tool.ShareOfFunds + "\nRubReqs " + toolState.LongReqs + "/" + toolState.ShortReqs +
            "\nVols " + toolState.LongVolume + "/" + toolState.ShortVolume +
            "\nRealVols " + toolState.LongRealVolume + "/" + toolState.ShortRealVolume +
            "\nOrderVol " + toolState.LongOrderVolume + "/" + toolState.ShortOrderVolume +
            "\nMin/Max lots " + tool.MinNumberOfLots + "/" + tool.MaxNumberOfLots + "\n";
        try
        {
            File.AppendAllText("Logs/LogsTools/" + tool.Name + ".txt", data);
        }
        catch (Exception e)
        {
            AddInfo(tool.Name + ": logging exception: " + type + ": " + e.Message);
        }
    }

    private void UpdateViewTool(object sender, SelectionChangedEventArgs e)
    {
        var tool = (sender as ComboBox).DataContext as Tool;
        Task.Run(() => UpdateView(tool, true));
    }
}
