namespace ProSystem.Algorithms;

[Serializable]
internal class ATRS : Script
{
    private int period = 10;
    private int mult = 0;
    private int periodEx = 1;
    private double correction = 0;
    private int tf = 60;

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

    public int PeriodEx
    {
        get => periodEx;
        set { periodEx = value; NotifyChange(); }
    }

    public double Correction
    {
        get => correction;
        set { correction = value; NotifyChange(); }
    }

    public int IndicatorTF
    {
        get => tf;
        set { tf = value; NotifyChange(); }
    }

    public ATRS(string name) : base(name)
    {
        var isOSC = false;
        var upperProperties =
            new[] { nameof(Period), nameof(Mult), nameof(PeriodEx), nameof(Correction), nameof(IndicatorTF) };
        properties = new(isOSC, upperProperties);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var atr = Indicators.ATRLine(iBars.High, iBars.Low,
            iBars.Close, Period, Mult, PeriodEx, Correction, symbol.TickPrecision);
        atr = Indicators.Synchronize(atr, iBars, symbol.Bars);

        var isGrow = GetGrowLineForStop(symbol.Bars, atr);
        Result = new(ScriptType.StopLine, isGrow, [atr], iBars.DateTime[^1]);
    }
}
