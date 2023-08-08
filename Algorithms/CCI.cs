using System;

namespace ProSystem.Algorithms;

[Serializable]
internal class CCI : Script
{
    private int period = 5;
    private int periodEx = 30;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;
    private bool useChannel = true;

    public int Period
    {
        get => period;
        set { period = value; Notify(); }
    }

    public int PeriodEx
    {
        get => periodEx;
        set { periodEx = value; Notify(); }
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

    public bool UseChannel
    {
        get => useChannel;
        set { useChannel = value; Notify(); }
    }

    public CCI(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(PeriodEx), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit), nameof(UseChannel) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var oneLevel = PeriodEx < 1;
        double[] upper = null, lower = null, signalLine = null;
        double[] cci = Indicators.CCI(iBars.High, iBars.Low, iBars.Close, Period);
        if (!oneLevel)
        {
            if (UseChannel)
            {
                var lines = Indicators.BBands(cci, PeriodEx, 1.5);
                upper = Indicators.Synchronize(lines.Item1, iBars, symbol.Bars);
                lower = Indicators.Synchronize(lines.Item2, iBars, symbol.Bars);
            }
            else signalLine = Indicators.Synchronize(Indicators.EMA(cci, PeriodEx), iBars, symbol.Bars);
        }
        cci = Indicators.Synchronize(cci, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 2; i < symbol.Bars.Close.Length; i++)
        {
            if (oneLevel)
            {
                if (cci[i - 1] > 0.00001) isGrow[i] = IsTrend;
                else if (cci[i - 1] < -0.00001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
            else if (UseChannel)
            {
                if (cci[i - 1] - upper[i - 2] > 0.00001) isGrow[i] = IsTrend;
                else if (cci[i - 1] - lower[i - 2] < -0.00001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
            else
            {
                if (cci[i - 1] - signalLine[i - 1] > 0.00001) isGrow[i] = IsTrend;
                else if (cci[i - 1] - signalLine[i - 1] < -0.00001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
        }

        if (oneLevel)
            Result = new(ScriptType.OSC, isGrow, new double[][] { cci }, iBars.DateTime[^1], 0, 0, OnlyLimit);
        else if (UseChannel)
            Result = new(ScriptType.OSC, isGrow, new double[][] { cci, upper, lower }, iBars.DateTime[^1], OnlyLimit);
        else Result = new(ScriptType.OSC, isGrow, new double[][] { cci, signalLine }, iBars.DateTime[^1], OnlyLimit);
    }
}
