namespace ProSystem.Algorithms;

[Serializable]
internal class Channel : Script
{
    private int period = 20;
    private double mult = 2;
    private int tf = 60;
    private bool isTrend = true;
    private bool useSD = true;
    private NameMA nameMa = NameMA.SMA;

    public int Period
    {
        get => period;
        set { period = value; NotifyChange(); }
    }

    public double Mult
    {
        get => mult;
        set { mult = value; NotifyChange(); }
    }

    public int IndicatorTF
    {
        get => tf;
        set { tf = value; NotifyChange(); }
    }

    public bool IsTrend
    {
        get => isTrend;
        set { isTrend = value; NotifyChange(); }
    }

    public bool UseSD
    {
        get => useSD;
        set { useSD = value; NotifyChange(); }
    }

    public NameMA NameMA
    {
        get => nameMa;
        set { nameMa = value; NotifyChange(); }
    }

    public Channel(string name) : base(name)
    {
        var isOSC = false;
        var upper = new[] { nameof(Period), nameof(Mult), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(UseSD) };
        var maObjects = new[] { NameMA.SMA, NameMA.WMA, NameMA.DEMA, NameMA.KAMA, NameMA.LR };
        properties = new(isOSC, upper, middle, nameof(NameMA), maObjects);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        double[] line;
        if (NameMA == NameMA.SMA) line = Indicators.SMA(iBars.Close, Period);
        else if (NameMA == NameMA.WMA) line = Indicators.WMA(iBars.Close, Period);
        else if (NameMA == NameMA.DEMA) line = Indicators.DEMA(iBars.Close, Period);
        else if (NameMA == NameMA.KAMA) line = Indicators.KAMA(iBars.Close, Period);
        else if (NameMA == NameMA.LR) line = Indicators.LinearRegression(iBars.Close, Period);
        else throw new Exception("Непредвиденный тип MA");

        var bands = UseSD ? Indicators.ChannelSD(line, Period, Mult) : Indicators.ChannelPC(line, Mult);
        var upper = Indicators.Synchronize(bands.Item1, iBars, symbol.Bars);
        var lower = Indicators.Synchronize(bands.Item2, iBars, symbol.Bars);

        var isGrow = GetGrowLineForChannel(symbol.Bars, IsTrend, upper, lower);
        Result = new(ScriptType.Line, isGrow, [upper, lower], iBars.DateTime[^1], true);
    }
}
