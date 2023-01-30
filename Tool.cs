﻿using System;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls;
using System.Collections.Generic;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using static ProSystem.MainWindow;
namespace ProSystem;

[Serializable]
public partial class Tool : INotifyPropertyChanged
{
    #region Fields, properties and constructors
    private bool ShBasSec;
    [NonSerialized] private int UsingMethod;
    [NonSerialized] private PlotModel Plot;
    [NonSerialized] private PlotModel MiniPlot;
    [NonSerialized] private EventHandler<AxisChangedEventArgs> Handler;
    [NonSerialized] private EventHandler<AxisChangedEventArgs> MiniHandler;

    public string Name { get; set; }
    public bool Active { get; set; }
    public bool ShowBasicSecurity
    {
        get => ShBasSec;
        set
        {
            if (value == false || BasicSecurity != null)
            {
                ShBasSec = value;
                UpdateModel();
                UpdateMiniModel();
            }
            else AddInfo("У инструмента нет базисного актива.");
        }
    }
    public int BaseTF { get; set; }
    public DateTime TimeLastRecalc { get; set; }
    public DateTime TimeNextRecalc { get; set; }
    public Security MySecurity { get; set; }
    public Security BasicSecurity { get; set; }
    public IScript[] Scripts { get; set; }
    public PlotModel MainModel
    {
        get => Plot;
        set { Plot = value; NotifyChanged(); }
    }
    public PlotModel Model
    {
        get => MiniPlot;
        set { MiniPlot = value; NotifyChanged(); }
    }

    [field: NonSerialized] public PlotController Controller { get; set; }
    [field: NonSerialized] public Brush BrushState { get; set; }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;
    private void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public Tool() { }
    public Tool(string Name, Security MySecurity, Security BasicSecurity, IScript[] Scripts)
    {
        this.Name = Name;
        this.MySecurity = MySecurity;
        this.BasicSecurity = BasicSecurity;
        this.Scripts = Scripts;

        BaseTF = 30;
        Controller = new PlotController();
        Controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.PanAt);
        Controller.BindMouseDown(OxyMouseButton.Right, PlotCommands.SnapTrack);
        BrushState = Colors.Red;
    }
    #endregion

    #region Methods
    public void Initialize(TabItem MyTabItem)
    {
        if (BaseTF < 1) BaseTF = 30;
        Controller = new PlotController();
        Controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.PanAt);
        Controller.BindMouseDown(OxyMouseButton.Right, PlotCommands.SnapTrack);

        UpdateModel();
        foreach (IScript script in Scripts)
        {
            script.Initialize(this, MyTabItem);
            script.Calculate(BasicSecurity ?? MySecurity);
            UpdateModelsAndPanel(script);
        }

        if (Active)
        {
            BrushState = Colors.Green;
            (MyTabItem.Content as Grid).Children.OfType<Grid>().Last().
                Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Deactivate tool";
        }
        else
        {
            BrushState = Colors.Red;
            (MyTabItem.Content as Grid).Children.OfType<Grid>().Last().
                Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Activate tool";
        }

        UpdateModel();
        UpdateMiniModel();
    }
    public void InitializeScript(IScript MyScript, TabItem TabTool, bool IsOSC, string[] UpperProperties,
        string[] MiddleProperties = null, string SpecProperty = null, NameMA[] SpecObjs = null)
    {
        Window.Dispatcher.Invoke(() =>
        {
            if (IsOSC)
            {
                var MyPlot = ((TabTool.Content as Grid).Children[0] as Grid).Children[0] as OxyPlot.SkiaSharp.Wpf.PlotView;
                if (MyPlot.Visibility == System.Windows.Visibility.Hidden)
                {
                    Grid.SetRow(((TabTool.Content as Grid).Children[0] as Grid).Children[1] as OxyPlot.SkiaSharp.Wpf.PlotView, 1);
                    MyPlot.Visibility = System.Windows.Visibility.Visible;
                }
            }
            int i = Array.IndexOf(Scripts, MyScript) + 1;
            UIElementCollection UICollection = (((TabTool.Content as Grid).Children[1] as Grid).Children[i] as Grid).Children;

            UICollection.Clear();
            UICollection.Add(GetTextBlock(MyScript.Name, 5, 0));
            AddUpperControls(MyScript, UICollection, UpperProperties, SpecProperty, SpecObjs);
            if (MiddleProperties != null) AddMiddleControls(MyScript, UICollection, MiddleProperties);

            TextBlock Block = GetTextBlock("Block Info", 5, 170);
            MyScript.BlockInfo = Block;
            UICollection.Add(Block);
        });
    }
    public async void ChangeActivity()
    {
        if (Active)
        {
            Active = false;
            if (Connection == ConnectionState.Connected)
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    TXmlConnector.SubUnsub(false, MySecurity.Board, MySecurity.Seccode);
                    if (BasicSecurity != null) TXmlConnector.SubUnsub(false, BasicSecurity.Board, BasicSecurity.Seccode);
                });
            }

            BrushState = Colors.Red;
            ((Window.TabsTools.Items[Tools.IndexOf(this)] as TabItem).Content as Grid).Children.OfType<Grid>()
                .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Activate tool";
        }
        else
        {
            if (Connection == ConnectionState.Connected)
            {
                if (!await System.Threading.Tasks.Task.Run(() =>
                {
                    RequestBars(this);
                    TXmlConnector.GetClnSecPermissions(MySecurity.Board, MySecurity.Seccode, MySecurity.Market);
                    if (MySecurity.Bars == null || BasicSecurity != null && BasicSecurity.Bars == null)
                    {
                        System.Threading.Thread.Sleep(500);
                        if (MySecurity.Bars == null || BasicSecurity != null && BasicSecurity.Bars == null)
                        {
                            AddInfo("Не удалось активировать инструмент, потому что не пришли бары. Попробуйте ещё раз.");
                            return false;
                        }
                    }

                    TXmlConnector.SubUnsub(true, MySecurity.Board, MySecurity.Seccode);
                    if (BasicSecurity != null) TXmlConnector.SubUnsub(true, BasicSecurity.Board, BasicSecurity.Seccode);
                    RequestBars(this);
                    return true;
                })) return;
            }
            BrushState = StopTrading ? Colors.Orange : Colors.Green;
            Active = true;
            ((Window.TabsTools.Items[Tools.IndexOf(this)] as TabItem).Content as Grid).Children.OfType<Grid>()
                .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Deactivate tool";
        }
        NotifyChanged();
    }
    public async void ReloadBars()
    {
        if (Active)
        {
            while (Connection == ConnectionState.Connected && !SystemReadyToTrading) await System.Threading.Tasks.Task.Delay(100);
            ChangeActivity();
        }
        MySecurity.SourceBars = null;
        MySecurity.Bars = null;
        if (BasicSecurity != null)
        {
            BasicSecurity.SourceBars = null;
            BasicSecurity.Bars = null;
        }

        if (Connection == ConnectionState.Connected)
        {
            RequestBars(this);
            await System.Threading.Tasks.Task.Delay(500);
            if (MySecurity.Bars != null) UpdateModel();
        }
    }

    public void UpdateModel()
    {
        // Обработка баров
        if (MySecurity.Bars == null || ShowBasicSecurity && BasicSecurity.Bars == null)
        {
            AddInfo("MakeModel: нет баров. ShowBasic: " + ShowBasicSecurity);
            return;
        }
        Bars MyBars = ShowBasicSecurity ? BasicSecurity.Bars.GetCopy() : MySecurity.Bars.GetCopy();

        List<double> GridLines = new();
        HighLowItem[] MyItems = new HighLowItem[MyBars.Close.Length];
        MyItems[0] = new HighLowItem(0, MyBars.High[0], MyBars.Low[0], MyBars.Open[0], MyBars.Close[0]);
        for (int i = 1; i < MyBars.Close.Length; i++)
        {
            MyItems[i] = new HighLowItem(i, MyBars.High[i], MyBars.Low[i], MyBars.Open[i], MyBars.Close[i]);
            if (MyBars.DateTime[i].Date != MyBars.DateTime[i - 1].Date) GridLines.Add(i);
        }

        // Формирование новой модели
        PlotModel NewModel = new();
        DateTimeAxis xAxis = new()
        {
            Position = AxisPosition.Bottom,
            LabelFormatter = (value) =>
            {
                if (value > 1 && value < MyBars.Close.Length)
                {
                    if (MyBars.DateTime[(int)value].Date == MyBars.DateTime[(int)value - 1].Date) return MyBars.DateTime[(int)value].ToString("HH:mm", IC);
                    else return MyBars.DateTime[(int)value].ToString("dd.MM.yy HH:mm", IC);
                }
                else return "";
            },
            ExtraGridlines = GridLines.ToArray(),
            ExtraGridlineColor = OxyColors.LightGray
        };
        LinearAxis yAxis = new() { Position = AxisPosition.Right };
        CandleStickSeries Candles = new()
        {
            ItemsSource = MyItems,
            TrackerFormatString = "High: {3:0.0000}\nLow: {4:0.0000}\nOpen: {5:0.0000}\nClose: {6:0.0000}"
        };

        PlotColors.Color(NewModel);
        PlotColors.Color(xAxis);
        PlotColors.Color(yAxis);
        PlotColors.Color(Candles);

        NewModel.Axes.Add(xAxis);
        NewModel.Axes.Add(yAxis);
        NewModel.Series.Add(Candles);
        NewModel.PlotMargins = new OxyThickness(0, 0, 50, 20);

        // Обработка исходной модели
        int Range = 100;
        Series[] MySeries = null;
        Annotation[] MyArrows = null;
        if (MainModel != null)
        {
            MainModel.Axes[0].AxisChanged -= Handler;
            if (MiniHandler != null) MainModel.Axes[0].AxisChanged -= MiniHandler;

            if (MainModel.Annotations.Count > 0)
            {
                if (!ShowBasicSecurity) MyArrows = MainModel.Annotations.ToArray();
                MainModel.Annotations.Clear();
            }
            if (MainModel.Axes[0].ActualMaximum > 10) Range = (int)(MainModel.Axes[0].ActualMaximum - MainModel.Axes[0].ActualMinimum);

            if (MainModel.Series.Count > 0)
            {
                Candles.DecreasingColor = (MainModel.Series[0] as CandleStickSeries).DecreasingColor;
                if (MainModel.Series.Count > 1)
                {
                    MySeries = new Series[MainModel.Series.Count - 1];
                    for (int i = 0; i < MainModel.Series.Count - 1; i++) MySeries[i] = MainModel.Series[i + 1];
                }
                MainModel.Series.Clear();
            }
            else AddInfo("MakeModel: У исходной модели нет ни одной серии.");
        }

        // Обновление модели, её серий и аннотаций
        MainModel = NewModel;
        if (MySeries != null) foreach (Series Ser in MySeries) MainModel.Series.Add(Ser);
        if (MyArrows != null) foreach (Annotation Arrow in MyArrows) MainModel.Annotations.Add(Arrow);

        // Настройка новой модели
        xAxis.Maximum = MyItems[^1].X + 5;
        xAxis.Minimum = xAxis.Maximum - Range > 1 ? xAxis.Maximum - Range : 1;
        if (Range > 0)
        {
            yAxis.Maximum = MyItems.TakeLast(Range).Select(x => x.High).Max();
            yAxis.Minimum = MyItems.TakeLast(Range).Select(x => x.Low).Min();
        }
        Handler = (sender, e) => AutoScaling(Candles, xAxis, yAxis);
        xAxis.AxisChanged += Handler;

        int xEnd = MyItems.Length - 1;
        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (int i = Math.Max((int)xAxis.Minimum, 0); i <= xEnd; i++)
        {
            yMin = Math.Min(yMin, MyItems[i].Low);
            yMax = Math.Max(yMax, MyItems[i].High);
        }

        double Margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - Margin, yMax + Margin);
        MainModel.InvalidatePlot(true);
    }
    public void UpdateMiniModel(IScript MyScript = null)
    {
        // Обработка исходных данных
        double[] Gridlines = null;
        int Range = MainModel.Axes[0].Maximum > 5 ? (int)(MainModel.Axes[0].Maximum - MainModel.Axes[0].Minimum) : 250;

        List<DataPoint> Points = new();
        List<Series> ListSeries = new();
        if (MyScript == null)
        {
            if (Model.Series.Count > 0)
            {
                foreach (Series MySeries in Model.Series) ListSeries.Add(MySeries);
                Points = (Model.Series[0] as LineSeries).ItemsSource as List<DataPoint>;

                Model.Series.Clear();
                Gridlines = Model.Axes[1].ExtraGridlines;
            }
            else return;
        }
        else
        {
            if (MyScript.Result.Centre != -1)
                Gridlines = new double[] { MyScript.Result.Centre + MyScript.Result.Level, MyScript.Result.Centre - MyScript.Result.Level };

            List<OxyColor> colors = PlotColors.Indicators.ToList();
            foreach (double[] Indicator in MyScript.Result.Indicators)
            {
                if (Indicator != null)
                {
                    Points = new();
                    for (int i = 0; i < Indicator.Length; i++) Points.Add(new DataPoint(i, Indicator[i]));
                    ListSeries.Add(new LineSeries() { ItemsSource = Points, Title = MyScript.Name, Color = colors[0] });
                    if (colors.Count > 1) colors.RemoveAt(0);
                }
            }
            if (ListSeries.Count > 1) Points = (ListSeries[0] as LineSeries).ItemsSource as List<DataPoint>;
        }

        // Формирование новой модели
        Model = new PlotModel();
        LinearAxis xAxis = new() { Position = AxisPosition.Bottom, IsZoomEnabled = false, IsPanEnabled = false, IsAxisVisible = false };
        LinearAxis yAxis = new() { Position = AxisPosition.Right, IsPanEnabled = false };

        xAxis.Maximum = Points.Count + 4;
        xAxis.Minimum = xAxis.Maximum - Range > 1 ? xAxis.Maximum - Range : 1;
        yAxis.ExtraGridlines = Gridlines;

        PlotColors.Color(Model);
        PlotColors.Color(xAxis, false);
        PlotColors.Color(yAxis, false);

        Model.Axes.Add(xAxis);
        Model.Axes.Add(yAxis);
        foreach (Series MySeries in ListSeries) Model.Series.Add(MySeries);
        Model.PlotMargins = new OxyThickness(0, 0, 50, 0);

        // Настройка новой модели
        MainModel.Axes[0].AxisChanged -= MiniHandler;
        MiniHandler = (sender, e) => AutoScaling(Points.ToArray(), MainModel.Axes[0], xAxis, yAxis);
        MainModel.Axes[0].AxisChanged += MiniHandler;

        int xStart = (int)xAxis.Minimum;
        int xEnd = Points.Count - 1;

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (int i = xStart; i <= xEnd; i++)
        {
            yMin = Math.Min(yMin, Points[i].Y);
            yMax = Math.Max(yMax, Points[i].Y);
        }

        double Margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - Margin, yMax + Margin);
        Model.InvalidatePlot(true);
    }

    private void AddAnnotations(Trade[] MyTrades, Order[] MyOrders)
    {
        int i;
        OxyColor MyColor;
        double yStartPoint, yEndPoint;
        List<(Trade, int)> trades = new();
        List<(Trade, int)> tradesOnlyWithPoint = new();
        foreach (Trade trade in MyTrades)
        {
            i = Array.FindIndex(MySecurity.Bars.DateTime, x => x.AddMinutes(MySecurity.Bars.TF) > trade.DateTime);
            if (i < 0)
            {
                AddInfo("AddAnnotations: Не найден бар, на котором произошла сделка.");
                continue;
            }

            var sameTrade = trades.SingleOrDefault(x => x.Item2 == i && x.Item1.BuySell == trade.BuySell);
            if (sameTrade.Item1 != null)
            {
                sameTrade.Item1.Quantity += trade.Quantity;
                if (sameTrade.Item1.Price != trade.Price) tradesOnlyWithPoint.Add((trade, i));
            }
            else trades.Add((trade.GetCopy(), i));
        }
        foreach ((Trade, int) MyTrade in trades)
        {
            var trade = MyTrade.Item1;
            i = MyTrade.Item2;

            if (trade.BuySell == "B")
            {
                MyColor = PlotColors.GreenBar;
                yStartPoint = MySecurity.Bars.Low[i] - MySecurity.Bars.Low[i] * 0.001;
                yEndPoint = MySecurity.Bars.Low[i];
            }
            else
            {
                MyColor = PlotColors.RedBar;
                yStartPoint = MySecurity.Bars.High[i] + MySecurity.Bars.High[i] * 0.001;
                yEndPoint = MySecurity.Bars.High[i];
            }

            MainModel.Annotations.Add(new ArrowAnnotation()
            {
                Color = MyColor,
                Text = trade.SignalOrder + "; " + trade.Quantity + "; " + trade.Price,
                StartPoint = new DataPoint(i, yStartPoint),
                EndPoint = new DataPoint(i, yEndPoint),
                ToolTip = trade.SenderOrder
            });
            MainModel.Annotations.Add(new PointAnnotation()
            {
                X = i,
                Y = trade.Price,
                Fill = OxyColors.WhiteSmoke,
                Stroke = OxyColors.Black,
                StrokeThickness = 1,
                ToolTip = trade.SenderOrder
            });
        }
        foreach ((Trade, int) MyTrade in tradesOnlyWithPoint)
        {
            MainModel.Annotations.Add(new PointAnnotation()
            {
                X = MyTrade.Item2,
                Y = MyTrade.Item1.Price,
                Fill = OxyColors.WhiteSmoke,
                Stroke = OxyColors.Black,
                StrokeThickness = 1,
                ToolTip = MyTrade.Item1.SenderOrder
            });
        }
        foreach (Order ActiveOrder in MyOrders)
        {
            MainModel.Annotations.Add(new LineAnnotation()
            {
                Type = LineAnnotationType.Horizontal,
                Y = ActiveOrder.Price,
                MinimumX = MainModel.Axes[0].ActualMaximum - 20,
                Color = ActiveOrder.BuySell == "B" ? PlotColors.GreenBar : PlotColors.RedBar,
                ToolTip = ActiveOrder.Sender,
                Text = ActiveOrder.Signal,
                StrokeThickness = 2
            });
        }
    }
    private static LineSeries MakeLineSeries(double[] Indicator, string Name)
    {
        DataPoint[] Points = new DataPoint[Indicator.Length];
        for (int i = 0; i < Indicator.Length; i++) Points[i] = new DataPoint(i, Indicator[i]);
        return new LineSeries() { ItemsSource = Points, Color = PlotColors.Indicator, Title = Name };
    }
    private static void AutoScaling(CandleStickSeries Candles, DateTimeAxis xAxis, LinearAxis yAxis)
    {
        int i = Candles.FindByX(xAxis.ActualMinimum);
        int xEnd = Candles.FindByX(xAxis.ActualMaximum, i);

        double yMin = double.MaxValue;
        double yMax = double.MinValue;
        for (; i <= xEnd; i++)
        {
            yMin = Math.Min(yMin, Candles.Items[i].Low);
            yMax = Math.Max(yMax, Candles.Items[i].High);
        }

        double Margin = (yMax - yMin) * 0.05;
        yAxis.Zoom(yMin - Margin, yMax + Margin);
    }
    private static void AutoScaling(DataPoint[] Points, Axis MainAxis, LinearAxis xAxis, LinearAxis yAxis)
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

        i = Window.TabsTools.SelectedIndex;
        if (Tools[i].Model != null) Window.Dispatcher.Invoke(() => Tools[i].Model.InvalidatePlot(false));
    }

    public static void AddUpperControls(IScript MyScript, UIElementCollection UICollection, string[] Properties,
        string SpecProperty = null, NameMA[] SpecObjs = null)
    {
        UICollection.Add(GetTextBlock(Properties[0], 5, 20));
        UICollection.Add(GetTextBox(MyScript, Properties[0], 65, 20));

        UICollection.Add(GetTextBlock(Properties[1], 5, 40));
        UICollection.Add(GetTextBox(MyScript, Properties[1], 65, 40));

        if (Properties.Length > 2)
        {
            UICollection.Add(GetTextBlock(Properties[2], 5, 60));
            UICollection.Add(GetTextBox(MyScript, Properties[2], 65, 60));
        }
        if (Properties.Length > 3)
        {
            UICollection.Add(GetTextBlock(Properties[3], 105, 20));
            UICollection.Add(GetTextBox(MyScript, Properties[3], 165, 20));
        }
        if (Properties.Length > 4)
        {
            UICollection.Add(GetTextBlock(Properties[4], 105, 40));
            UICollection.Add(GetTextBox(MyScript, Properties[4], 165, 40));
        }
        if (Properties.Length > 5)
        {
            UICollection.Add(GetTextBlock(Properties[5], 105, 60));
            UICollection.Add(GetTextBox(MyScript, Properties[5], 165, 60));
        }
        if (Properties.Length > 6) AddInfo(MyScript.Name + ": Непредвиденное количество верхних контролов.");

        ComboBox ComboBox = new()
        {
            Width = 90,
            Margin = new System.Windows.Thickness(5, 80, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            ItemsSource = new PositionType[] { PositionType.Long, PositionType.Short, PositionType.Neutral }
        };
        Binding Binding = new() { Source = MyScript, Path = new System.Windows.PropertyPath("CurrentPosition"), Mode = BindingMode.TwoWay };
        ComboBox.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, Binding);
        UICollection.Add(ComboBox);

        if (SpecProperty != null)
        {
            ComboBox ComboBox2 = new()
            {
                Width = 90,
                Margin = new System.Windows.Thickness(105, 80, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                ItemsSource = SpecObjs
            };
            Binding Binding2 = new() { Source = MyScript, Path = new System.Windows.PropertyPath(SpecProperty), Mode = BindingMode.TwoWay };
            ComboBox2.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, Binding2);
            UICollection.Add(ComboBox2);
        }
    }
    public static void AddMiddleControls(IScript MyScript, UIElementCollection UICollection, string[] Properties)
    {
        UICollection.Add(GetCheckBox(MyScript, Properties[0], Properties[0], 5, 110));
        if (Properties.Length > 1) UICollection.Add(GetCheckBox(MyScript, Properties[1], Properties[1], 5, 130));
        if (Properties.Length > 2) UICollection.Add(GetCheckBox(MyScript, Properties[2], Properties[2], 5, 150));
        if (Properties.Length > 3) UICollection.Add(GetCheckBox(MyScript, Properties[3], Properties[3], 105, 110));
        if (Properties.Length > 4) UICollection.Add(GetCheckBox(MyScript, Properties[4], Properties[4], 105, 130));
        if (Properties.Length > 5) UICollection.Add(GetCheckBox(MyScript, Properties[5], Properties[5], 105, 150));
        if (Properties.Length > 6) AddInfo(MyScript.Name + ": Непредвиденное количество средних контролов.");
    }
    #endregion
}
