using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        var AssetsPorfolio = new CategoryAxis { Position = AxisPosition.Left };
        var FactVolPorfolio = new BarSeries { BarWidth = 4, FillColor = Theme.ShortPosition, StrokeThickness = 0 };
        var MaxVolPorfolio = new BarSeries { FillColor = Theme.MaxVolume, StrokeThickness = 0 };
        var AxisPorfolio = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = new double[]
            {
                5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95
            },
        };

        AssetsPorfolio.Labels.Add("Portfolio");
        FactVolPorfolio.Items.Add(new BarItem { Value = portfolio.ShareInitReqs });
        MaxVolPorfolio.Items.Add(new BarItem { Value = portfolio.PotentialShareInitReqs });
        Theme.Color(AssetsPorfolio);
        Theme.Color(AxisPorfolio);

        var Model = new PlotModel();
        Model.Series.Add(MaxVolPorfolio);
        Model.Series.Add(FactVolPorfolio);
        Model.Axes.Add(AssetsPorfolio);
        Model.Axes.Add(AxisPorfolio);
        Model.PlotMargins = new OxyThickness(55, 0, 0, 20);
        Theme.Color(Model);
        return Model;
    }

    public static PlotModel GetPlot(this IEnumerable<Tool> tools, IEnumerable<Position> positions,
        double saldo, string filter, bool onlyWithPositions, bool excludeBaseBals)
    {
        var Assets = new CategoryAxis { Position = AxisPosition.Left };
        var FactVol = new BarSeries { BarWidth = 4, StrokeThickness = 0 };
        var MaxVol = new BarSeries { FillColor = Theme.MaxVolume, StrokeThickness = 0 };
        var Axis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = new double[]
            {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
                16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30
            },
        };

        Tool[] MyTools = tools.Where(x => x.Active).ToArray();
        for (int i = MyTools.Length - 1; i >= 0; i--)
        {
            if (filter == "All tools" ||
                filter == "First part" && i < MyTools.Length / 2 || filter == "Second part" && i >= MyTools.Length / 2)
            {
                Position Pos = positions.SingleOrDefault(x => x.Seccode == MyTools[i].Security.Seccode);
                if (Pos != null && Math.Abs(Pos.Saldo) > 0.0001)
                {
                    int shift = excludeBaseBals ? MyTools[i].BaseBalance : 0;
                    double FactReq = Math.Abs((Pos.Saldo > 0 ? (Pos.Saldo - shift) * MyTools[i].Security.InitReqLong :
                        (-Pos.Saldo - shift) * MyTools[i].Security.InitReqShort) / saldo * 100);
                    FactVol.Items.Add(new BarItem
                    {
                        Value = FactReq,
                        Color = Pos.Saldo - shift > 0 ? Theme.LongPosition : Theme.ShortPosition
                    });
                }
                else if (!onlyWithPositions) FactVol.Items.Add(new BarItem { Value = 0 });
                else continue;

                Assets.Labels.Add(MyTools[i].Name);
                MaxVol.Items.Add(new BarItem
                {
                    Value = excludeBaseBals || !MyTools[i].UseShiftBalance ?
                    MyTools[i].ShareOfFunds : (MyTools[i].BaseBalance > 0 ?
                    MyTools[i].ShareOfFunds + (MyTools[i].BaseBalance * MyTools[i].Security.InitReqLong / saldo * 100) :
                    MyTools[i].ShareOfFunds + (-MyTools[i].BaseBalance * MyTools[i].Security.InitReqShort / saldo * 100))
                });
            }
        }
        Theme.Color(Assets);
        Theme.Color(Axis);

        var Model = new PlotModel();
        Model.Series.Add(MaxVol);
        Model.Series.Add(FactVol);
        Model.Axes.Add(Assets);
        Model.Axes.Add(Axis);
        Model.PlotMargins = new OxyThickness(55, 0, 0, 20);

        Theme.Color(Model);
        return Model;
    }


    public static void UpdateModel(Tool tool)
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
        tool.Handler = (s, a) => ScaleModel(candles, xAxis, yAxis);
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

    public static void UpdateMiniModel(Tool tool, Window window, Script script = null)
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
            (s, a) => ScaleMiniModel(points.ToArray(), mainModel.Axes[0], xAxis, yAxis, window, tool.Model);
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
