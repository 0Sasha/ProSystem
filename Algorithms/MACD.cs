namespace ProSystem.Algorithms;

[Serializable]
internal class MACD : Script
{
    private int period = 10;
    private int mult = 2;
    private int tf = 60;
    private bool onlyLimit = true;
    private bool isTrend = true;

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

    public MACD(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(Mult), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var macdLine = Indicators.MACD(iBars.Close, Period, Period * Mult);
        var signalLine = Indicators.EMA(macdLine, (int)(Period * 0.75));
        macdLine = Indicators.Synchronize(macdLine, iBars, symbol.Bars);
        signalLine = Indicators.Synchronize(signalLine, iBars, symbol.Bars);

        var isGrow = GetGrowLineForSignal(symbol.Bars.Close.Length, IsTrend, macdLine, signalLine);
        Result = new(ScriptType.OSC, isGrow, [macdLine, signalLine], iBars.DateTime[^1], OnlyLimit);
    }
}
