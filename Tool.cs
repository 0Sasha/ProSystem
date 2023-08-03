using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using static ProSystem.MainWindow;
using OxyPlot;
using OxyPlot.Axes;

namespace ProSystem;

[Serializable]
public class Tool : INotifyPropertyChanged
{
    private bool showBasicSec;
    [NonSerialized] private PlotModel plot;
    [NonSerialized] private PlotModel miniPlot;
    [NonSerialized] internal int IsBusy;

    private int waitingLimit = 60;
    private double shareOfFunds = 5;
    private int minNumberLots = 0;
    private int maxNumberLots = 2;
    private int numberLots = 1;
    private int baseBalance = 0;
    internal DateTime triggerPosition;

    private bool stopTrading = true;
    private bool tradeShare = true;
    private bool useNormalization = false;
    private bool useShiftBalance = false;

    [field: NonSerialized] private Border borderState;
    [field: NonSerialized] private TextBlock blockInfo;
    [field: NonSerialized] private TextBlock mainBlockInfo;

    public string Name { get; set; }
    public bool Active { get; set; }
    public bool ShowBasicSecurity
    {
        get => showBasicSec;
        set
        {
            if (value == false || BasicSecurity != null)
            {
                showBasicSec = value;
                Window.TradingSystem.ToolManager.UpdateView(this, true);
            }
            else Window.AddInfo("У инструмента нет базисного актива.");
        }
    } // Remove dependency
    public int BaseTF { get; set; }
    public DateTime TimeLastRecalc { get; set; }
    public DateTime TimeNextRecalc { get; set; }
    public Security MySecurity { get; set; }
    public Security BasicSecurity { get; set; }
    public Script[] Scripts { get; set; }
    public PlotModel MainModel
    {
        get => plot;
        set { plot = value; Notify(); }
    }
    public PlotModel Model
    {
        get => miniPlot;
        set { miniPlot = value; Notify(); }
    }

    public int WaitingLimit
    {
        get => waitingLimit;
        set { waitingLimit = value; Notify(); }
    }
    public double ShareOfFunds
    {
        get => shareOfFunds;
        set { shareOfFunds = value > 15 ? 5 : value; Notify(); }
    }
    public int MinNumberOfLots
    {
        get => minNumberLots;
        set { minNumberLots = value; Notify(); }
    }
    public int MaxNumberOfLots
    {
        get => maxNumberLots;
        set { maxNumberLots = value; Notify(); }
    }
    public int NumberOfLots
    {
        get => numberLots;
        set { numberLots = value; Notify(); }
    }
    public int BaseBalance
    {
        get => baseBalance;
        set { baseBalance = value; Notify(); }
    }

    public bool StopTrading
    {
        get => stopTrading;
        set
        {
            stopTrading = value;
            if (Active) BrushState = stopTrading ? Theme.Orange : Theme.Green;
            Notify();
        }
    }
    public bool TradeShare
    {
        get => tradeShare;
        set
        {
            tradeShare = value;
            Window.TradingSystem.ToolManager.UpdateControlGrid(this);
            Notify();
        }
    }
    public bool UseNormalization
    {
        get => useNormalization;
        set { useNormalization = value; Notify(); }
    }
    public bool UseShiftBalance
    {
        get => useShiftBalance;
        set
        {
            useShiftBalance = value;
            Window.TradingSystem.ToolManager.UpdateControlGrid(this);
            Notify();
        }
    }

    public Border BorderState
    {
        get => borderState;
        set { borderState = value; Notify(); }
    }
    public TextBlock BlockInfo
    {
        get => blockInfo;
        set { blockInfo = value; Notify(); }
    }
    public TextBlock MainBlockInfo
    {
        get => mainBlockInfo;
        set { mainBlockInfo = value; Notify(); }
    }

    [field: NonSerialized] public PlotController Controller { get; set; }
    [field: NonSerialized] public Brush BrushState { get; set; }
    [field: NonSerialized] public EventHandler<AxisChangedEventArgs> Handler { get; set; }
    [field: NonSerialized] public EventHandler<AxisChangedEventArgs> MiniHandler { get; set; }

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
}
