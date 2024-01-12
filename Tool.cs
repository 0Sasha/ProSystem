using Newtonsoft.Json;
using OxyPlot;
using OxyPlot.Axes;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProSystem;

[Serializable]
public class Tool : INotifyPropertyChanged
{
    private bool stopTrading = true;
    private bool tradeShare = true;
    private bool showBasicSec = false;
    private bool useNormalization = false;
    private bool useShiftBalance = false;

    private double shareOfFunds = 5;
    private double minQty = 0;
    private double maxQty = 2;
    private double hardQty = 1;
    private double baseBalance = 0;

    [JsonIgnore]
    [NonSerialized]
    private TabItem? tab;

    [JsonIgnore]
    [NonSerialized]
    private PlotModel? plot;

    [JsonIgnore]
    [NonSerialized]
    private PlotModel? miniPlot;

    [JsonIgnore]
    [NonSerialized]
    private PlotController controller = Plot.GetController();

    [JsonIgnore]
    [NonSerialized]
    private Border borderState = new();

    [JsonIgnore]
    [NonSerialized]
    private Brush brushState = Theme.Red;

    [JsonIgnore]
    [NonSerialized]
    private TextBlock blockInfo = new();

    [JsonIgnore]
    [NonSerialized]
    private TextBlock mainBlockInfo = new();

    [JsonIgnore]
    [NonSerialized]
    internal int IsOccupied;

    public string Name { get; set; }
    public bool Active { get; set; }
    public int BaseTF { get; set; }
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
    public Security? BasicSecurity { get; set; }
    public Script[] Scripts { get; set; }

    [JsonIgnore]
    public TabItem? Tab
    {
        get => tab;
        set { tab = value; NotifyChange(); }
    }

    [JsonIgnore]
    public PlotModel? MainModel
    {
        get => plot;
        set { plot = value; NotifyChange(); }
    }

    [JsonIgnore]
    public PlotModel? Model
    {
        get => miniPlot;
        set { miniPlot = value; NotifyChange(); }
    }

    [JsonIgnore]
    public PlotController Controller
    {
        get => controller;
        set { controller = value; NotifyChange(); }
    }

    [JsonIgnore]
    public Border BorderState
    {
        get => borderState;
        set { borderState = value; NotifyChange(); }
    }

    [JsonIgnore]
    public Brush BrushState
    {
        get => brushState;
        set { brushState = value; NotifyChange(); }
    }

    [JsonIgnore]
    public TextBlock BlockInfo
    {
        get => blockInfo;
        set { blockInfo = value; NotifyChange(); }
    }

    [JsonIgnore]
    public TextBlock MainBlockInfo
    {
        get => mainBlockInfo;
        set { mainBlockInfo = value; NotifyChange(); }
    }

    public double ShareOfFunds
    {
        get => shareOfFunds;
        set
        {
            shareOfFunds = value > 85 ? 5 : value;
            NotifyChange();
        }
    }
    public double MinQty
    {
        get => minQty;
        set { minQty = value; NotifyChange(); }
    }
    public double MaxQty
    {
        get => maxQty;
        set { maxQty = value; NotifyChange(); }
    }
    public double HardQty
    {
        get => hardQty;
        set { hardQty = value; NotifyChange(); }
    }
    public double BaseBalance
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

    public DateTime LastRecalc { get; set; }
    public DateTime NextRecalc { get; set; }
    public DateTime TriggerPosition { get; set; }

    [JsonIgnore]
    [field: NonSerialized]
    public EventHandler<AxisChangedEventArgs>? Handler { get; set; }

    [JsonIgnore]
    [field: NonSerialized]
    public EventHandler<AxisChangedEventArgs>? MiniHandler { get; set; }

    [field: NonSerialized]
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void NotifyChange(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    [JsonConstructor]
    public Tool(string name, Security security, Security? basicSecurity, Script[] scripts)
    {
        ArgumentException.ThrowIfNullOrEmpty(nameof(name), name);
        Name = name;
        Security = security;
        BasicSecurity = basicSecurity;
        Scripts = scripts;
        BaseTF = 30;
    }
}
