namespace ProSystem.Algorithms;

[Serializable]
internal class SumLine : Script
{
    private int period = 20;
    private int periodSignal = 20;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;
    private bool useChannel = true;
    private bool channelBands = false;

    public int Period
    {
        get => period;
        set { period = value; NotifyChange(); }
    }

    public int PeriodSignal
    {
        get => periodSignal;
        set { periodSignal = value; NotifyChange(); }
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

    public bool ChannelBands
    {
        get => channelBands;
        set { channelBands = value; NotifyChange(); }
    }

    public SumLine(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(PeriodSignal), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit), nameof(UseChannel), nameof(ChannelBands) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var sumLine = Indicators.SumLine(iBars.Close, Period);
        CalculateTotalOSC(symbol.Bars, iBars, sumLine, Period, UseChannel, ChannelBands, IsTrend, OnlyLimit);
    }
}
