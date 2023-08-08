using System;

namespace ProSystem.Algorithms;

[Serializable]
internal class MACD : Script
{
    private int period = 10;
    private int mult = 2;
    private int tf = 60;
    private bool onlyLimit = true;
    private bool isTrend = true;

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

    public bool IsTrend
    {
        get => isTrend;
        set { isTrend = value; Notify(); }
    }

    public MACD(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(Mult), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var macdLine = Indicators.MACD(iBars.Close, Period, Period * Mult);
        var signalLine = Indicators.EMA(macdLine, (int)(Period * 0.75));
        macdLine = Indicators.Synchronize(macdLine, iBars, symbol.Bars);
        signalLine = Indicators.Synchronize(signalLine, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 1; i < symbol.Bars.Close.Length; i++)
        {
            if (macdLine[i - 1] - signalLine[i - 1] > 0.00001) isGrow[i] = IsTrend;
            else if (macdLine[i - 1] - signalLine[i - 1] < -0.00001) isGrow[i] = !IsTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(ScriptType.OSC, isGrow, new double[][] { macdLine, signalLine }, iBars.DateTime[^1], OnlyLimit);
    }
}
