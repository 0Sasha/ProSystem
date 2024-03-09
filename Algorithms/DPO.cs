namespace ProSystem.Algorithms;

[Serializable]
internal class DPO : Script
{
    private int period = 5;
    private int periodEx = 30;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;
    private bool useChannel = true;

    public int Period
    {
        get => period;
        set { period = value; NotifyChange(); }
    }

    public int PeriodEx
    {
        get => periodEx;
        set { periodEx = value; NotifyChange(); }
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

    public bool UseChannel
    {
        get => useChannel;
        set { useChannel = value; NotifyChange(); }
    }

    public DPO(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(PeriodEx), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit), nameof(UseChannel) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var dpo = Indicators.DPO(iBars.Close, Period);
        CalculateEndlessOSC(symbol.Bars, iBars, dpo, PeriodEx, UseChannel, IsTrend, OnlyLimit);
    }
}
