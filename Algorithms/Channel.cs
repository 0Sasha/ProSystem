using System;

namespace ProSystem.Algorithms;

[Serializable]
internal class Channel : Script
{
    private int period = 20;
    private double mult = 2;
    private int tf = 60;
    private bool isTrend = true;
    private bool useSD = true;
    private NameMA nameMa = NameMA.SMA;

    public int Period
    {
        get => period;
        set { period = value; Notify(); }
    }

    public double Mult
    {
        get => mult;
        set { mult = value; Notify(); }
    }

    public int IndicatorTF
    {
        get => tf;
        set { tf = value; Notify(); }
    }

    public bool IsTrend
    {
        get => isTrend;
        set { isTrend = value; Notify(); }
    }

    public bool UseSD
    {
        get => useSD;
        set { useSD = value; Notify(); }
    }

    public NameMA NameMA
    {
        get => nameMa;
        set { nameMa = value; Notify(); }
    }

    public Channel(string name) : base(name)
    {
        var isOSC = false;
        var upper = new[] { nameof(Period), nameof(Mult), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(UseSD) };
        var maObjects = new[] { NameMA.SMA, NameMA.WMA, NameMA.DEMA, NameMA.KAMA, NameMA.LR };
        properties = new(isOSC, upper, middle, nameof(NameMA), maObjects);
    }

    public override void Calculate(Security symbol)
    {
        var iBars = symbol.Bars.Compress(IndicatorTF);
        double[] line;
        if (NameMA == NameMA.SMA) line = Indicators.SMA(iBars.Close, Period);
        else if (NameMA == NameMA.WMA) line = Indicators.WMA(iBars.Close, Period);
        else if (NameMA == NameMA.DEMA) line = Indicators.DEMA(iBars.Close, Period);
        else if (NameMA == NameMA.KAMA) line = Indicators.KAMA(iBars.Close, Period);
        else if (NameMA == NameMA.LR) line = Indicators.LinearRegression(iBars.Close, Period);
        else throw new Exception("Непредвиденный тип MA");

        var bands = UseSD ? Indicators.ChannelSD(line, Period, Mult) : Indicators.ChannelPC(line, Mult);
        var upper = Indicators.Synchronize(bands.Item1, iBars, symbol.Bars);
        var lower = Indicators.Synchronize(bands.Item2, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 2; i < symbol.Bars.Close.Length; i++)
        {
            if (isGrow[i - 1] != IsTrend &&
                symbol.Bars.High[i - 1] - upper[i - 2] > 0.00001 &&
                symbol.Bars.Close[i - 1] - lower[i - 2] > 0.00001) isGrow[i] = IsTrend;
            else if (isGrow[i - 1] == IsTrend &&
                symbol.Bars.Low[i - 1] - lower[i - 2] < -0.00001 &&
                symbol.Bars.Close[i - 1] - upper[i - 2] < -0.00001) isGrow[i] = !IsTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(ScriptType.Line, isGrow, new double[][] { upper, lower }, iBars.DateTime[^1], true);
    }
}
