namespace ProSystem.Algorithms;

[Serializable]
internal class RVI : Script
{
    private int period = 20;
    private int level = 20;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;

    public int Period
    {
        get => period;
        set { period = value; NotifyChange(); }
    }

    public int Level
    {
        get => level;
        set { level = value; NotifyChange(); }
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

    public RVI(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(Level), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var rvi = Indicators.RVI(iBars.Open, iBars.High, iBars.Low, iBars.Close, Period);
        rvi = Indicators.Synchronize(rvi, iBars, symbol.Bars);

        var isGrow = GetGrowLineForLevels(symbol.Bars.Close.Length, IsTrend, rvi, Level, -Level);
        Result = new(ScriptType.OSC, isGrow, [rvi], iBars.DateTime[^1], 0, Level, OnlyLimit);
    }
}
