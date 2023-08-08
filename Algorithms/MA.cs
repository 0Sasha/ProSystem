using System;

namespace ProSystem.Algorithms;

[Serializable]
internal class MA : Script
{
    private int period = 20;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;
    private NameMA nameMA = NameMA.SMA;

    public int Period
    {
        get => period;
        set { period = value; Notify(); }
    }

    public int IndicatorTF
    {
        get => tf;
        set { tf = value; Notify(); }
    }

    public bool OnlyLimit
    {
        get => onlyLimit;
        set { onlyLimit = value; Notify(); }
    }

    public bool IsTrend
    {
        get => isTrend;
        set { isTrend = value; Notify(); }
    }

    public NameMA NameMA
    {
        get => nameMA;
        set { nameMA = value; Notify(); }
    }

    public MA(string name) : base(name)
    {
        var isOSC = false;
        var upper = new[] { nameof(Period), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        var maObjects = new[] { NameMA.SMA, NameMA.EMA, NameMA.DEMA, NameMA.KAMA, NameMA.Median };
        properties = new(isOSC, upper, middle, nameof(NameMA), maObjects);
    }

    public override void Calculate(Security symbol)
    {
        var iBars = symbol.Bars.Compress(IndicatorTF);
        double[] ma;
        if (NameMA == NameMA.SMA) ma = Indicators.SMA(iBars.Close, Period);
        else if (NameMA == NameMA.EMA) ma = Indicators.EMA(iBars.Close, Period);
        else if (NameMA == NameMA.DEMA) ma = Indicators.DEMA(iBars.Close, Period);
        else if (NameMA == NameMA.KAMA) ma = Indicators.KAMA(iBars.Close, Period);
        else if (NameMA == NameMA.Median) ma = Indicators.Median(iBars.Close, Period);
        else throw new Exception("Непредвиденный тип MA");
        ma = Indicators.Synchronize(ma, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 2; i < symbol.Bars.Close.Length; i++)
        {
            if (ma[i - 1] - ma[i - 2] > 0.00001) isGrow[i] = IsTrend;
            else if (ma[i - 1] - ma[i - 2] < -0.00001) isGrow[i] = !IsTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(ScriptType.Line, isGrow, new double[][] { ma }, iBars.DateTime[^1], OnlyLimit);
    }
}
