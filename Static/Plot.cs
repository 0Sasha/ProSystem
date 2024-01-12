using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Globalization;
using System.Windows;

namespace ProSystem;

internal static class Plot
{
    private static readonly CultureInfo IC = CultureInfo.InvariantCulture;

    public static PlotController GetController()
    {
        var controller = new PlotController();
        controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.PanAt);
        controller.BindMouseDown(OxyMouseButton.Right, PlotCommands.SnapTrack);
        return controller;
    }

    public static PlotModel GetPlot(this Portfolio portfolio)
    {
        var assets = new CategoryAxis { Position = AxisPosition.Left };
        var initReqs = new BarSeries { BarWidth = 4, FillColor = Theme.ShortPosition, StrokeThickness = 0 };
        var potentialInitReqs = new BarSeries { FillColor = Theme.MaxVolume, StrokeThickness = 0 };
        var axis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = Enumerable.Range(1, 20).Select(i => i * 5D).ToArray()
        };

        assets.Labels.Add("Portfolio");
        initReqs.Items.Add(new BarItem { Value = portfolio.ShareInitReqs });
        potentialInitReqs.Items.Add(new BarItem { Value = portfolio.PotentialShareInitReqs });
        assets.Color();
        axis.Color();

        var model = new PlotModel();
        model.Series.Add(potentialInitReqs);
        model.Series.Add(initReqs);
        model.Axes.Add(assets);
        model.Axes.Add(axis);
        model.PlotMargins = new OxyThickness(55, 0, 0, 20);
        model.Color();
        return model;
    }

    public static PlotModel GetPlot(this IEnumerable<Tool> tools, IEnumerable<Position> positions,
        double saldo, string filter, bool onlyWithPositions, bool excludeBaseBals)
    {
        var assets = new CategoryAxis { Position = AxisPosition.Left };
        var factVol = new BarSeries { BarWidth = 4, StrokeThickness = 0 };
        var maxVol = new BarSeries { FillColor = Theme.MaxVolume, StrokeThickness = 0 };
        var axis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = Enumerable.Range(1, 100).Select(i => (double)i).ToArray(),
        };

        var myTools = tools.Where(x => x.Active).ToArray();
        for (int i = myTools.Length - 1; i >= 0; i--)
        {
            if (filter == "All tools" ||
                filter == "First part" && i < myTools.Length / 2 || filter == "Second part" && i >= myTools.Length / 2)
            {
                var pos = positions.SingleOrDefault(x => x.Seccode == myTools[i].Security.Seccode);
                if (pos != null && Math.Abs(pos.Saldo) > 0.000001)
                {
                    var shift = excludeBaseBals ? myTools[i].BaseBalance : 0;
                    var factReq = Math.Abs((pos.Saldo > 0.000001 ? (pos.Saldo - shift) * myTools[i].Security.InitReqLong :
                        (-pos.Saldo - shift) * myTools[i].Security.InitReqShort) / saldo * 100) / myTools[i].Security.LotSize;
                    factVol.Items.Add(new BarItem
                    {
                        Value = factReq,
                        Color = pos.Saldo - shift > 0 ? Theme.LongPosition : Theme.ShortPosition
                    });
                }
                else if (!onlyWithPositions) factVol.Items.Add(new BarItem { Value = 0 });
                else continue;

                assets.Labels.Add(myTools[i].Name);
                maxVol.Items.Add(new BarItem
                {
                    Value = excludeBaseBals || !myTools[i].UseShiftBalance ?
                    myTools[i].ShareOfFunds : (myTools[i].BaseBalance > 0.000001 ?
                    myTools[i].ShareOfFunds + (myTools[i].BaseBalance * myTools[i].Security.InitReqLong / saldo * 100) :
                    myTools[i].ShareOfFunds + (-myTools[i].BaseBalance * myTools[i].Security.InitReqShort / saldo * 100))
                });
            }
        }
        assets.Color();
        axis.Color();

        var model = new PlotModel();
        model.Series.Add(maxVol);
        model.Series.Add(factVol);
        model.Axes.Add(assets);
        model.Axes.Add(axis);
        model.PlotMargins = new OxyThickness(55, 0, 0, 20);

        model.Color();
        return model;
    }

    public static void UpdateModel(Tool tool)
    {
        var bars = tool.ShowBasicSecurity ? tool.BasicSecurity?.Bars?.GetCopy() : tool.Security.Bars?.GetCopy();
        if (bars == null) return;

        List<double> gridLines = [];
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
            ExtraGridlines = [.. gridLines]
        };
        LinearAxis yAxis = new() { Position = AxisPosition.Right };
        CandleStickSeries candles = new()
        {
            ItemsSource = items,
            TrackerFormatString = "High: {3:0.0000}\nLow: {4:0.0000}\nOpen: {5:0.0000}\nClose: {6:0.0000}"
        };

        xAxis.Color();
        yAxis.Color();
        candles.Color((model?.Series[0] as CandleStickSeries)?.DecreasingColor);

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
        tool.Handler = (s, a) => ScaleModel(candles, xAxis, yAxis);
        xAxis.AxisChanged += tool.Handler;

        if (model == null)
        {
            tool.MainModel = new() { PlotMargins = new OxyThickness(0, 0, 50, 20) };
            model = tool.MainModel;
            model.Color();
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

    public static void UpdateMiniModel(Tool tool, Window window, Script? script = null)
    {
        if (tool.MainModel == null) return;

        double[] gridlines = [];
        List<DataPoint>? points = [];
        List<Series> listSeries = [];
        if (script != null && script.Result != null)
        {
            if (script.Result.Centre != -1) gridlines =
            [
                script.Result.Centre + script.Result.Level,
                script.Result.Centre - script.Result.Level
            ];

            List<OxyColor> colors = [.. Theme.Indicators];
            foreach (var indicator in script.Result.Indicators)
            {
                if (indicator != null)
                {
                    points = [];
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
            if (listSeries.Count > 1) points = (listSeries[0] as LineSeries)?.ItemsSource as List<DataPoint>;
        }
        else if (tool.Model?.Series.Count > 0)
        {
            points = (tool.Model.Series[0] as LineSeries)?.ItemsSource as List<DataPoint>;
            gridlines = tool.Model.Axes[1].ExtraGridlines;
        }
        else return;

        if (points == null) return;
        int range = tool.MainModel.Axes[0].Maximum > 5 ?
            (int)(tool.MainModel.Axes[0].Maximum - tool.MainModel.Axes[0].Minimum) : 250;
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
        xAxis.Color(false);
        yAxis.Color(false);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (int i = (int)xAxis.Minimum; i < points.Count; i++)
        {
            yMin = Math.Min(yMin, points[i].Y);
            yMax = Math.Max(yMax, points[i].Y);
        }
        double margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - margin, yMax + margin);

        if (tool.Model == null)
        {
            tool.Model = new() { PlotMargins = new OxyThickness(0, 0, 50, 0) };
            tool.Model.Axes.Add(xAxis);
            tool.Model.Axes.Add(yAxis);
            tool.Model.Color();
        }
        else
        {
            if (listSeries.Count > 0) tool.Model.Series.Clear();
            tool.Model.Axes[0] = xAxis;
            tool.Model.Axes[1] = yAxis;
        }

        tool.MainModel.Axes[0].AxisChanged -= tool.MiniHandler;
        tool.MiniHandler =
            (s, a) => ScaleMiniModel([.. points], tool.MainModel.Axes[0], xAxis, yAxis, window, tool.Model);
        tool.MainModel.Axes[0].AxisChanged += tool.MiniHandler;

        foreach (var series in listSeries) tool.Model.Series.Add(series);
        tool.Model.InvalidatePlot(true);
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

        double margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - margin, yMax + margin);
    }

    private static void ScaleMiniModel(DataPoint[] points, Axis mainAxis,
        LinearAxis xAxis, LinearAxis yAxis, Window window, PlotModel miniModel)
    {
        int dif = points.Length + 4 - (int)mainAxis.Maximum;
        xAxis.Minimum = mainAxis.ActualMinimum + dif;
        xAxis.Maximum = mainAxis.ActualMaximum + dif;

        int i = Math.Max((int)xAxis.Minimum, 0);
        int xEnd = Math.Min((int)xAxis.Maximum, points.Length - 1);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (; i <= xEnd; i++)
        {
            yMin = Math.Min(yMin, points[i].Y);
            yMax = Math.Max(yMax, points[i].Y);
        }

        double margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - margin, yMax + margin);

        window.Dispatcher.Invoke(() => miniModel.InvalidatePlot(false));
    }
}
