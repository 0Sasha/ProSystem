using System;

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
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var parStop = Indicators.PARLine(iBars.High, iBars.Low, CoefAccel, MaxCoef, symbol.Decimals);
        parStop = Indicators.Synchronize(parStop, iBars, symbol.Bars);

        var pastParStop = 0D;
        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 1; i < isGrow.Length; i++)
        {
            if (Math.Abs(pastParStop - parStop[i - 1]) > 0.00001 || pastParStop < 0.00001)
            {
                if (!isGrow[i - 1] && symbol.Bars.High[i] - parStop[i - 1] > 0.00001 ||
                    isGrow[i - 1] && symbol.Bars.Low[i] - parStop[i - 1] < -0.00001)
                {
                    isGrow[i] = !isGrow[i - 1];
                    pastParStop = parStop[i - 1];
                }
                else isGrow[i] = isGrow[i - 1];
            }
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(ScriptType.StopLine, isGrow, new double[][] { parStop }, iBars.DateTime[^1]);
    }
}
