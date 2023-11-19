using System;

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
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var atr = Indicators.ATRLine(iBars.High, iBars.Low,
            iBars.Close, Period, Mult, PeriodEx, Correction, symbol.Decimals);
        atr = Indicators.Synchronize(atr, iBars, symbol.Bars);

        var pastStopATR = 0D;
        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 1; i < isGrow.Length; i++)
        {
            if (Math.Abs(pastStopATR - atr[i - 1]) > 0.00001 || pastStopATR < 0.00001)
            {
                if (!isGrow[i - 1] && symbol.Bars.High[i] - atr[i - 1] > 0.00001 ||
                    isGrow[i - 1] && symbol.Bars.Low[i] - atr[i - 1] < -0.00001)
                {
                    isGrow[i] = !isGrow[i - 1];
                    pastStopATR = atr[i - 1];
                }
                else isGrow[i] = isGrow[i - 1];
            }
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(ScriptType.StopLine, isGrow, new double[][] { atr }, iBars.DateTime[^1]);
    }
}
