namespace ProSystem.Algorithms;

[Serializable]
internal class MA : Script
{
    private int period = 20;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;
    private NameMA nameMA = NameMA.SMA;

    public int Period
    {
        get => period;
        set { period = value; NotifyChange(); }
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

    public MA(string name) : base(name)
    {
        var isOSC = false;
        var upper = new[] { nameof(Period), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        var maObjects = new[] { NameMA.SMA, NameMA.EMA, NameMA.DEMA, NameMA.KAMA, NameMA.Median };
        properties = new(isOSC, upper, middle, nameof(NameMA), maObjects);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        Func<double[], int, int, double[]> indicator = NameMA switch
        {
            NameMA.SMA => Indicators.SMA,
            NameMA.EMA => Indicators.EMA,
            NameMA.DEMA => Indicators.DEMA,
            NameMA.KAMA => Indicators.KAMA,
            NameMA.Median => Indicators.Median,
            _ => throw new Exception("Unknown type of MA")
        };
        var ma = indicator(iBars.Close, Period, -1);
        ma = Indicators.Synchronize(ma, iBars, symbol.Bars);

        var isGrow = GetGrowLineForDirection(symbol.Bars.Close.Length, IsTrend, ma);
        Result = new(ScriptType.Line, isGrow, [ma], iBars.DateTime[^1], OnlyLimit);
    }
}
