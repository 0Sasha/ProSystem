namespace ProSystem.Algorithms;

[Serializable]
internal class PARS : Script
{
    private double coefAccel = 0.02;
    private double maxCoef = 0.2;
    private int tf = 60;

    public double CoefAccel
    {
        get => coefAccel;
        set { coefAccel = value; NotifyChange(); }
    }

    public double MaxCoef
    {
        get => maxCoef;
        set { maxCoef = value; NotifyChange(); }
    }

    public int IndicatorTF
    {
        get => tf;
        set { tf = value; NotifyChange(); }
    }

    public PARS(string name) : base(name)
    {
        var isOSC = false;
        var upper = new[] { nameof(CoefAccel), nameof(MaxCoef), nameof(IndicatorTF) };
        properties = new(isOSC, upper);
    }

    public override void Calculate(Security symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol.Bars, nameof(symbol.Bars));
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var parStop = Indicators.PARLine(iBars.High, iBars.Low, CoefAccel, MaxCoef, symbol.TickPrecision);
        parStop = Indicators.Synchronize(parStop, iBars, symbol.Bars);

        var isGrow = GetGrowLineForStop(symbol.Bars, parStop);
        Result = new(ScriptType.StopLine, isGrow, [parStop], iBars.DateTime[^1]);
    }
}
