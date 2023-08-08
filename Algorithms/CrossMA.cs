using System;

namespace ProSystem.Algorithms;

[Serializable]
internal class CrossMA : Script
{
    private int period = 10;
    private int mult = 2;
    private int tf = 60;
    private bool onlyLimit = true;
    private bool isCrossMALimit = false;
    private NameMA nameMA = NameMA.SMA;

    public int Period
    {
        get => period;
        set { period = value; Notify(); }
    }

    public int Mult
    {
        get => mult;
        set { mult = value; Notify(); }
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

    public bool IsCrossMALim
    {
        get => isCrossMALimit;
        set { isCrossMALimit = value; Notify(); }
    }

    public NameMA NameMA
    {
        get => nameMA;
        set { nameMA = value; Notify(); }
    }

    public CrossMA(string name) : base(name)
    {
        var isOSC = false;
        var upper = new[] { nameof(Period), nameof(Mult), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsCrossMALim), nameof(OnlyLimit) };
        var maObjects = new[] { NameMA.SMA, NameMA.EMA, NameMA.SMMA, NameMA.DEMA, NameMA.KAMA };
        properties = new(isOSC, upper, middle, nameof(NameMA), maObjects);
    }

    public override void Calculate(Security symbol)
    {
        var iBars = symbol.Bars.Compress(IndicatorTF);
        double[] shortMA, longMA;
        if (NameMA == NameMA.SMA)
        {
            shortMA = Indicators.SMA(iBars.Close, Period, symbol.Decimals);
            longMA = Indicators.SMA(iBars.Close, Period * Mult, symbol.Decimals);
        }
        else if (NameMA == NameMA.EMA)
        {
            shortMA = Indicators.EMA(iBars.Close, Period, symbol.Decimals);
            longMA = Indicators.EMA(iBars.Close, Period * Mult, symbol.Decimals);
        }
        else if (NameMA == NameMA.SMMA)
        {
            shortMA = Indicators.SMMA(iBars.Close, Period, symbol.Decimals);
            longMA = Indicators.SMMA(iBars.Close, Period * Mult, symbol.Decimals);
        }
        else if (NameMA == NameMA.DEMA)
        {
            shortMA = Indicators.DEMA(iBars.Close, Period, symbol.Decimals);
            longMA = Indicators.DEMA(iBars.Close, Period * Mult, symbol.Decimals);
        }
        else if (NameMA == NameMA.KAMA)
        {
            shortMA = Indicators.KAMA(iBars.Close, Period, symbol.Decimals);
            longMA = Indicators.KAMA(iBars.Close, Period * Mult, symbol.Decimals);
        }
        else throw new Exception("Непредвиденный тип MA");
        shortMA = Indicators.Synchronize(shortMA, iBars, symbol.Bars);
        longMA = Indicators.Synchronize(longMA, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 1; i < symbol.Bars.Close.Length; i++)
        {
            if (shortMA[i - 1] - longMA[i - 1] > 0.00001) isGrow[i] = true;
            else if (shortMA[i - 1] - longMA[i - 1] < -0.00001) isGrow[i] = false;
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(IsCrossMALim ? ScriptType.LimitLine : ScriptType.Line,
            isGrow, new double[][] { shortMA, longMA }, iBars.DateTime[^1], OnlyLimit);
    }
}
