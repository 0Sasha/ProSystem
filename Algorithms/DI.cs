namespace ProSystem.Algorithms;

[Serializable]
internal class DI : Script
{
    private int period = 10;
    private int tf = 60;
    private bool onlyLimit = true;
    private bool isTrend = true;

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

    public DI(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var lines = Indicators.DI(iBars.High, iBars.Low ,iBars.Close, Period);
        var mainLine = Indicators.Synchronize(lines.Item1, iBars, symbol.Bars);
        var signalLine = Indicators.Synchronize(lines.Item2, iBars, symbol.Bars);

        var isGrow = GetGrowLineForSignal(symbol.Bars.Close.Length, IsTrend, mainLine, signalLine);
        Result = new(ScriptType.OSC, isGrow, [mainLine, signalLine], iBars.DateTime[^1], OnlyLimit);
    }
}
