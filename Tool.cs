using System;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Collections.Generic;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;
using static ProSystem.MainWindow;
using static ProSystem.Controls;

namespace ProSystem;

[Serializable]
public partial class Tool : INotifyPropertyChanged
{
    #region Fields, properties and constructors
    private bool ShBasSec;
    [NonSerialized] private int UsingMethod;
    [NonSerialized] private PlotModel Plot;
    [NonSerialized] private PlotModel MiniPlot;
    [NonSerialized] public EventHandler<AxisChangedEventArgs> Handler;
    [NonSerialized] public EventHandler<AxisChangedEventArgs> MiniHandler;

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
                UpdateView(true);
            }
            else AddInfo("У инструмента нет базисного актива.");
        }
    }
    public int BaseTF { get; set; }
    public DateTime TimeLastRecalc { get; set; }
    public DateTime TimeNextRecalc { get; set; }
    public Security MySecurity { get; set; }
    public Security BasicSecurity { get; set; }
    public Script[] Scripts { get; set; }
    public PlotModel MainModel
    {
        get => Plot;
        set { Plot = value; Notify(); }
    }
    public PlotModel Model
    {
        get => MiniPlot;
        set { MiniPlot = value; Notify(); }
    }

    [field: NonSerialized] public PlotController Controller { get; set; }
    [field: NonSerialized] public Brush BrushState { get; set; }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;
    internal void Notify(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public Tool() { }
    public Tool(string Name, Security MySecurity, Security BasicSecurity, Script[] Scripts)
    {
        this.Name = Name;
        this.MySecurity = MySecurity;
        this.BasicSecurity = BasicSecurity;
        this.Scripts = Scripts;

        BaseTF = 30;
        Controller = PlotExtensions.GetController();
        BrushState = Theme.Red;
    }
    #endregion

    #region Methods
    public void Initialize(TabItem MyTabItem)
    {
        if (BaseTF < 1) BaseTF = 30;
        Controller = PlotExtensions.GetController();

        ToolManager.UpdateModel(this);
        ScriptManager.InitializeScripts(Scripts, MyTabItem);
        foreach (Script script in Scripts)
        {
            script.Calculate(BasicSecurity ?? MySecurity);
            UpdateScriptView(script);
        }
        ToolManager.UpdateModel(this);
        ToolManager.UpdateMiniModel(this);

        if (Active)
        {
            BrushState = Theme.Green;
            (MyTabItem.Content as Grid).Children.OfType<Grid>().Last().
                Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Deactivate tool";
        }
        else
        {
            BrushState = Theme.Red;
            (MyTabItem.Content as Grid).Children.OfType<Grid>().Last().
                Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Activate tool";
        }
    }
    public async Task ChangeActivity()
    {
        if (Active)
        {
            Active = false;
            if (Connection == ConnectionState.Connected)
            {
                await Task.Run(() =>
                {
                    TXmlConnector.SubUnsub(false, MySecurity.Board, MySecurity.Seccode);
                    if (BasicSecurity != null) TXmlConnector.SubUnsub(false, BasicSecurity.Board, BasicSecurity.Seccode);
                });
            }

            BrushState = Theme.Red;
            Window.Dispatcher.Invoke(() =>
            ((Window.TabsTools.Items[Tools.IndexOf(this)] as TabItem).Content as Grid).Children.OfType<Grid>()
            .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Activate tool");
        }
        else
        {
            if (Connection == ConnectionState.Connected)
            {
                if (!await Task.Run(() =>
                {
                    RequestBars();
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
                    RequestBars();
                    return true;
                })) return;
            }
            BrushState = StopTrading ? Theme.Orange : Theme.Green;
            Active = true;
            Window.Dispatcher.Invoke(() =>
            ((Window.TabsTools.Items[Tools.IndexOf(this)] as TabItem).Content as Grid).Children.OfType<Grid>()
                .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = "Deactivate tool");
        }
        Notify();
    }

    public void RequestBars()
    {
        string IdTF;
        if (TimeFrames.Count > 0) IdTF = TimeFrames.Last(x => x.Period / 60 <= BaseTF).ID;
        else { AddInfo("RequestBars: пустой массив таймфреймов."); return; }

        int Count = 25;
        if (BasicSecurity != null)
        {
            if (BasicSecurity.SourceBars == null || BasicSecurity.SourceBars.Close.Length < 500 ||
                BasicSecurity.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
                BaseTF != BasicSecurity.SourceBars.TF && BaseTF != BasicSecurity.Bars.TF) Count = 10000;

            TXmlConnector.GetHistoryData(BasicSecurity.Board, BasicSecurity.Seccode, IdTF, Count);
        }

        Count = 25;
        if (MySecurity.SourceBars == null || MySecurity.SourceBars.Close.Length < 500 ||
            MySecurity.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
            BaseTF != MySecurity.SourceBars.TF && BaseTF != MySecurity.Bars.TF) Count = 10000;

        TXmlConnector.GetHistoryData(MySecurity.Board, MySecurity.Seccode, IdTF, Count);
    }
    public void UpdateBars(bool updateBasicSecurity)
    {
        if (updateBasicSecurity)
        {
            if (BasicSecurity.SourceBars.TF == BaseTF) BasicSecurity.Bars = BasicSecurity.SourceBars;
            else BasicSecurity.Bars = BasicSecurity.SourceBars.Compress(BaseTF);
        }
        else
        {
            if (MySecurity.SourceBars.TF == BaseTF) MySecurity.Bars = MySecurity.SourceBars;
            else MySecurity.Bars = MySecurity.SourceBars.Compress(BaseTF);
        }
    }
    public async void ReloadBars()
    {
        if (Active)
        {
            while (Connection == ConnectionState.Connected && !SystemReadyToTrading) await System.Threading.Tasks.Task.Delay(100);
            await ChangeActivity();
            await System.Threading.Tasks.Task.Delay(250);
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
            RequestBars();
            await System.Threading.Tasks.Task.Delay(500);
            if (MySecurity.Bars != null) UpdateView(true);
        }
    }

    public void UpdateView(bool updateScriptView)
    {
        try
        {
            if (updateScriptView)
            {
                if (MainModel == null) ToolManager.UpdateModel(this);
                foreach (var script in Scripts)
                {
                    script.Calculate(BasicSecurity ?? MySecurity);
                    UpdateScriptView(script);
                }
            }
            ToolManager.UpdateModel(this);
            if (Model != null) ToolManager.UpdateMiniModel(this);
        }
        catch (Exception ex)
        {
            AddInfo("UpdateView: " + Name + ": Исключение: " + ex.Message);
            AddInfo("Трассировка стека: " + ex.StackTrace);
            if (ex.InnerException != null)
            {
                AddInfo("Внутреннее исключение: " + ex.InnerException.Message);
                AddInfo("Трассировка стека внутреннего исключения: " + ex.InnerException.StackTrace);
            }
        }
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
                MyColor = Theme.GreenBar;
                yStartPoint = MySecurity.Bars.Low[i] - MySecurity.Bars.Low[i] * 0.001;
                yEndPoint = MySecurity.Bars.Low[i];
            }
            else
            {
                MyColor = Theme.RedBar;
                yStartPoint = MySecurity.Bars.High[i] + MySecurity.Bars.High[i] * 0.001;
                yEndPoint = MySecurity.Bars.High[i];
            }

            string text = trade.SignalOrder is "BuyAtLimit" or "SellAtLimit" or "BuyAtStop" or "SellAtStop" ?
                trade.Price + "; " + trade.Quantity : trade.SignalOrder + "; " + trade.Price + "; " + trade.Quantity;
            MainModel.Annotations.Add(new ArrowAnnotation()
            {
                Color = MyColor,
                Text = text,
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
                Color = ActiveOrder.BuySell == "B" ? Theme.GreenBar : Theme.RedBar,
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
        return new LineSeries() { ItemsSource = Points, Color = Theme.Indicator, Title = Name };
    }
    #endregion
}
