using OxyPlot;
using OxyPlot.Axes;
using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProSystem;

[Serializable]
public class Tool : INotifyPropertyChanged
{
    private bool showBasicSec;
    private bool stopTrading = true;
    private bool tradeShare = true;
    private bool useNormalization = false;
    private bool useShiftBalance = false;

    private int waitingLimit = 60;
    private double shareOfFunds = 5;
    private int minNumberLots = 0;
    private int maxNumberLots = 2;
    private int numberLots = 1;
    private int baseBalance = 0;

    [NonSerialized] private TabItem tab;
    [NonSerialized] private PlotModel plot;
    [NonSerialized] private PlotModel miniPlot;
    [NonSerialized] private PlotController controller;
    [NonSerialized] private Border borderState;
    [NonSerialized] private Brush brushState = Theme.Red;
    [NonSerialized] private TextBlock blockInfo;
    [NonSerialized] private TextBlock mainBlockInfo;
    [NonSerialized] internal int IsOccupied;

    public string Name { get; set; }
    public bool Active { get; set; }
    public int BaseTF { get; set; } = 30;
    public bool ShowBasicSecurity
    {
        get => showBasicSec;
        set
        {
            if (value == false || BasicSecurity != null)
            {
                showBasicSec = value;
                NotifyChange(nameof(ShowBasicSecurity));
            }
        }
    }
    public Security Security { get; set; }
    public Security BasicSecurity { get; set; }
    public Script[] Scripts { get; set; }

    public TabItem Tab
    {
        get => tab;
        set { tab = value; NotifyChange(); }
    }
    public PlotModel MainModel
    {
        get => plot;
        set { plot = value; NotifyChange(); }
    }
    public PlotModel Model
    {
        get => miniPlot;
        set { miniPlot = value; NotifyChange(); }
    }
    public PlotController Controller
    {
        get => controller;
        set { controller = value; NotifyChange(); }
    }
    public Border BorderState
    {
        get => borderState;
        set { borderState = value; NotifyChange(); }
    }
    public Brush BrushState
    {
        get => brushState;
        set { brushState = value; NotifyChange(); }
    } 
    public TextBlock BlockInfo
    {
        get => blockInfo;
        set { blockInfo = value; NotifyChange(); }
    }
    public TextBlock MainBlockInfo
    {
        get => mainBlockInfo;
        set { mainBlockInfo = value; NotifyChange(); }
    }

    public int WaitingLimit
    {
        get => waitingLimit;
        set { waitingLimit = value; NotifyChange(); }
    }
    public double ShareOfFunds
    {
        get => shareOfFunds;
        set { shareOfFunds = value > 15 ? 5 : value; NotifyChange(); }
    }
    public int MinNumberOfLots
    {
        get => minNumberLots;
        set { minNumberLots = value; NotifyChange(); }
    }
    public int MaxNumberOfLots
    {
        get => maxNumberLots;
        set { maxNumberLots = value; NotifyChange(); }
    }
    public int NumberOfLots
    {
        get => numberLots;
        set { numberLots = value; NotifyChange(); }
    }
    public int BaseBalance
    {
        get => baseBalance;
        set { baseBalance = value; NotifyChange(); }
    }

    public bool StopTrading
    {
        get => stopTrading;
        set
        {
            stopTrading = value;
            if (Active) BrushState = stopTrading ? Theme.Orange : Theme.Green;
            NotifyChange();
        }
    }
    public bool TradeShare
    {
        get => tradeShare;
        set
        {
            tradeShare = value;
            NotifyChange(nameof(TradeShare));
        }
    }
    public bool UseNormalization
    {
        get => useNormalization;
        set { useNormalization = value; NotifyChange(); }
    }
    public bool UseShiftBalance
    {
        get => useShiftBalance;
        set
        {
            useShiftBalance = value;
            NotifyChange(nameof(UseShiftBalance));
        }
    }

    public DateTime TimeLastRecalc { get; set; }
    public DateTime TimeNextRecalc { get; set; }
    public DateTime TriggerPosition { get; set; }

    [field: NonSerialized] public EventHandler<AxisChangedEventArgs> Handler { get; set; }
    [field: NonSerialized] public EventHandler<AxisChangedEventArgs> MiniHandler { get; set; }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

    internal void NotifyChange(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public Tool() { }

    public Tool(string name, Security security, Security basicSecurity, Script[] scripts)
    {
        Name = string.IsNullOrEmpty(name) ? throw new ArgumentNullException(nameof(name)) : name;
        Security = security ?? throw new ArgumentNullException(nameof(security));
        BasicSecurity = basicSecurity;
        Scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        Controller = Plot.GetController();
    }
}
