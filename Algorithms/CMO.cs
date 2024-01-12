namespace ProSystem.Algorithms;

[Serializable]
internal class CMO : Script
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

    public CMO(string name) : base(name)
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
        var cmo = Indicators.CMO(iBars.Close, Period);
        cmo = Indicators.Synchronize(cmo, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 1; i < isGrow.Length; i++)
        {
            if (cmo[i - 1] - Level > 0.000001) isGrow[i] = IsTrend;
            else if (cmo[i - 1] - -Level < -0.000001) isGrow[i] = !IsTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(ScriptType.OSC, isGrow, [cmo], iBars.DateTime[^1], 0, Level, OnlyLimit);
    }
}
