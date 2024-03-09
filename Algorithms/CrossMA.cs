namespace ProSystem.Algorithms;

[Serializable]
internal class CrossMA : Script
{
    private int period = 10;
    private int mult = 2;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;
    private NameMA nameMA = NameMA.SMA;

    public int Period
    {
        get => period;
        set { period = value; NotifyChange(); }
    }

    public int Mult
    {
        get => mult;
        set { mult = value; NotifyChange(); }
    }

    public int IndicatorTF
    {
        get => tf;
        set { tf = value; NotifyChange(); }
    }

    public bool OnlyLimit
    {
        get => onlyLimit;
        set { onlyLimit = value; NotifyChange(); }
    }

    public bool IsTrend
    {
        get => isTrend;
        set { isTrend = value; NotifyChange(); }
    }

    public NameMA NameMA
    {
        get => nameMA;
        set { nameMA = value; NotifyChange(); }
    }

    public CrossMA(string name) : base(name)
    {
        var isOSC = false;
        var upper = new[] { nameof(Period), nameof(Mult), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        var maObjects = new[] { NameMA.SMA, NameMA.EMA, NameMA.SMMA, NameMA.DEMA, NameMA.KAMA };
        properties = new(isOSC, upper, middle, nameof(NameMA), maObjects);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        Func<double[], int, int, double[]> indicator = NameMA switch
        {
            NameMA.SMA => Indicators.SMA,
            NameMA.EMA => Indicators.EMA,
            NameMA.SMMA => Indicators.SMMA,
            NameMA.DEMA => Indicators.DEMA,
            NameMA.KAMA => Indicators.KAMA,
            _ => throw new Exception("Unknown type of MA")
        };

        var iBars = symbol.Bars.Compress(IndicatorTF);
        var shortMA = indicator(iBars.Close, Period, symbol.TickPrecision);
        var longMA = indicator(iBars.Close, Period * Mult, symbol.TickPrecision);
        shortMA = Indicators.Synchronize(shortMA, iBars, symbol.Bars);
        longMA = Indicators.Synchronize(longMA, iBars, symbol.Bars);

        var isGrow = GetGrowLineForCross(symbol.Bars.Close.Length, IsTrend, shortMA, longMA);
        Result = new(ScriptType.Line, isGrow, [shortMA, longMA], iBars.DateTime[^1], OnlyLimit);
    }
}
