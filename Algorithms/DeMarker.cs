namespace ProSystem.Algorithms;

[Serializable]
internal class DeMarker : Script
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

    public DeMarker(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(Level), nameof(IndicatorTF) };
        var mddle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        properties = new(isOSC, upper, mddle);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var dem = Indicators.DeMarker(iBars.High, iBars.Low, Period);
        dem = Indicators.Synchronize(dem, iBars, symbol.Bars);

        var isGrow = GetGrowLineForLevels(symbol.Bars.Close.Length, IsTrend, dem, 50 + Level, 50 - Level);
        Result = new(ScriptType.OSC, isGrow, [dem], iBars.DateTime[^1], 50, Level, OnlyLimit);
    }
}
