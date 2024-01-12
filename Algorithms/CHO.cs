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
        var oneLevel = PeriodEx < 1;
        double[] upper = [], lower = [], signalLine = [];
        double[] cho = Indicators.CHO(iBars.High, iBars.Low, iBars.Close, iBars.Volume, Period, Period * 3 + 1);
        if (!oneLevel)
        {
            if (UseChannel)
            {
                var lines = Indicators.BBands(cho, PeriodEx, 1.5);
                upper = Indicators.Synchronize(lines.Item1, iBars, symbol.Bars);
                lower = Indicators.Synchronize(lines.Item2, iBars, symbol.Bars);
            }
            else signalLine = Indicators.Synchronize(Indicators.EMA(cho, PeriodEx), iBars, symbol.Bars);
        }
        cho = Indicators.Synchronize(cho, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 2; i < isGrow.Length; i++)
        {
            if (oneLevel)
            {
                if (cho[i - 1] > 0.000001) isGrow[i] = IsTrend;
                else if (cho[i - 1] < -0.000001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
            else if (UseChannel)
            {
                if (cho[i - 1] - upper[i - 2] > 0.000001) isGrow[i] = IsTrend;
                else if (cho[i - 1] - lower[i - 2] < -0.000001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
            else
            {
                if (cho[i - 1] - signalLine[i - 1] > 0.000001) isGrow[i] = IsTrend;
                else if (cho[i - 1] - signalLine[i - 1] < -0.000001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
        }

        if (oneLevel) Result = new(ScriptType.OSC, isGrow, [cho], iBars.DateTime[^1], 0, 0, OnlyLimit);
        else if (UseChannel)
            Result = new(ScriptType.OSC, isGrow, [cho, upper, lower], iBars.DateTime[^1], OnlyLimit);
        else Result = new(ScriptType.OSC, isGrow, [cho, signalLine], iBars.DateTime[^1], OnlyLimit);
    }
}
