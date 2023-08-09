using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using OxyPlot;
using OxyPlot.Axes;

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

    [NonSerialized] private PlotModel plot;
    [NonSerialized] private PlotModel miniPlot;
    [NonSerialized] private PlotController controller;
    [NonSerialized] private Border borderState;
    [NonSerialized] private TextBlock blockInfo;
    [NonSerialized] private TextBlock mainBlockInfo;
    [NonSerialized] internal int IsBusy;

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
                Notify(nameof(ShowBasicSecurity));
            }
        }
    }
    public int BaseTF { get; set; } = 30;
    public DateTime TimeLastRecalc { get; set; }
    public DateTime TimeNextRecalc { get; set; }
    public DateTime TriggerPosition { get; set; }
    public Security Security { get; set; }
    public Security BasicSecurity { get; set; }
    public Script[] Scripts { get; set; }
    public PlotModel MainModel
    {
        get => plot;
        set { plot = value; Notify(nameof(MainModel)); }
    }
    public PlotModel Model
    {
        get => miniPlot;
        set { miniPlot = value; Notify(nameof(Model)); }
    }
    public PlotController Controller
    {
        get => controller;
        set { controller = value; Notify(nameof(Controller)); }
    }

    public int WaitingLimit
    {
        get => waitingLimit;
        set { waitingLimit = value; Notify(nameof(WaitingLimit)); }
    }
    public double ShareOfFunds
    {
        get => shareOfFunds;
        set { shareOfFunds = value > 15 ? 5 : value; Notify(nameof(ShareOfFunds)); }
    }
    public int MinNumberOfLots
    {
        get => minNumberLots;
        set { minNumberLots = value; Notify(nameof(MinNumberOfLots)); }
    }
    public int MaxNumberOfLots
    {
        get => maxNumberLots;
        set { maxNumberLots = value; Notify(nameof(MaxNumberOfLots)); }
    }
    public int NumberOfLots
    {
        get => numberLots;
        set { numberLots = value; Notify(nameof(NumberOfLots)); }
    }
    public int BaseBalance
    {
        get => baseBalance;
        set { baseBalance = value; Notify(nameof(BaseBalance)); }
    }

    public bool StopTrading
    {
        get => stopTrading;
        set
        {
            stopTrading = value;
            if (Active) BrushState = stopTrading ? Theme.Orange : Theme.Green;
            Notify(nameof(StopTrading));
        }
    }
    public bool TradeShare
    {
        get => tradeShare;
        set
        {
            tradeShare = value;
            Notify(nameof(TradeShare));
        }
    }
    public bool UseNormalization
    {
        get => useNormalization;
        set { useNormalization = value; Notify(nameof(UseNormalization)); }
    }
    public bool UseShiftBalance
    {
        get => useShiftBalance;
        set
        {
            useShiftBalance = value;
            Notify(nameof(UseShiftBalance));
        }
    }

    public Border BorderState
    {
        get => borderState;
        set { borderState = value; Notify(nameof(BorderState)); }
    }
    public TextBlock BlockInfo
    {
        get => blockInfo;
        set { blockInfo = value; Notify(nameof(BlockInfo)); }
    }
    public TextBlock MainBlockInfo
    {
        get => mainBlockInfo;
        set { mainBlockInfo = value; Notify(nameof(MainBlockInfo)); }
    }

    [field: NonSerialized] public Brush BrushState { get; set; } = Theme.Red;
    [field: NonSerialized] public EventHandler<AxisChangedEventArgs> Handler { get; set; }
    [field: NonSerialized] public EventHandler<AxisChangedEventArgs> MiniHandler { get; set; }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

    internal void Notify(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public Tool() { }
    public Tool(string name, Security security, Security basicSecurity, Script[] scripts)
    {
        Name = string.IsNullOrEmpty(name) ? throw new ArgumentNullException(nameof(name)) : name;
        Security = security ?? throw new ArgumentNullException(nameof(security));
        BasicSecurity = basicSecurity;
        Scripts = scripts ?? throw new ArgumentNullException(nameof(scripts));
        Controller = PlotExtensions.GetController();
    }
}
