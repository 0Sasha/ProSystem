namespace ProSystem.Algorithms;

[Serializable]
internal class CHO : Script
{
    private int period = 5;
    private int periodEx = 20;
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

    public CHO(string name) : base(name)
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
        var cho = Indicators.CHO(iBars.High, iBars.Low, iBars.Close, iBars.Volume, Period, Period * 3 + 1);
        CalculateEndlessOSC(symbol.Bars, iBars, cho, PeriodEx, UseChannel, IsTrend, OnlyLimit);
    }
}
