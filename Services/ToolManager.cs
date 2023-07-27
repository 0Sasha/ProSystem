using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace ProSystem.Services;

internal class ToolManager : IToolManager
{
    private readonly MainWindow Window;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;
    private readonly IScriptManager ScriptManager;

    public ToolManager(MainWindow window, IScriptManager scriptManager)
    {
        Window = window;
        ScriptManager = scriptManager;
    }

    public void Initialize(Tool tool, TabItem tabTool)
    {
        throw new NotImplementedException();
        if (tool.BaseTF < 1) tool.BaseTF = 30;
        tool.Controller = PlotExtensions.GetController();

        UpdateModel(tool);
        ScriptManager.InitializeScripts(tool.Scripts, tabTool);
        foreach (Script script in tool.Scripts)
        {
            script.Calculate(tool.BasicSecurity ?? tool.MySecurity);


            throw new NotImplementedException();
            //UpdateScriptView(script);
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
            model = new() { PlotMargins = new OxyThickness(0, 0, 50, 20) };
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
            model = new() { PlotMargins = new OxyThickness(0, 0, 50, 0) };
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
