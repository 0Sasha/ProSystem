﻿using System;

namespace ProSystem.Algorithms;

[Serializable]
internal class SumLine : Script
{
    private int period = 20;
    private int periodSignal = 20;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;
    private bool useChannel = true;
    private bool channelBands = false;

    public int Period
    {
        get => period;
        set { period = value; NotifyChange(); }
    }

    public int PeriodSignal
    {
        get => periodSignal;
        set { periodSignal = value; NotifyChange(); }
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

    public bool ChannelBands
    {
        get => channelBands;
        set { channelBands = value; NotifyChange(); }
    }

    public SumLine(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(PeriodSignal), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit), nameof(UseChannel), nameof(ChannelBands) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        var iBars = symbol.Bars.Compress(IndicatorTF);
        double[] upper = null, lower = null, ma = null;
        double[] sumLine = Indicators.SumLine(iBars.Close, Period);

        if (UseChannel)
        {
            var lines = ChannelBands ?
                Indicators.BBands(sumLine, Period, 1.5) : Indicators.Extremes(sumLine, sumLine, Period);
            upper = Indicators.Synchronize(lines.Item1, iBars, symbol.Bars);
            lower = Indicators.Synchronize(lines.Item2, iBars, symbol.Bars);
        }
        else ma = Indicators.Synchronize(Indicators.EMA(sumLine, PeriodSignal), iBars, symbol.Bars);
        sumLine = Indicators.Synchronize(sumLine, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 2; i < isGrow.Length; i++)
        {
            if (UseChannel)
            {
                if (sumLine[i - 1] - upper[i - 2] > 0.00001) isGrow[i] = IsTrend;
                else if (sumLine[i - 1] - lower[i - 2] < -0.00001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
            else
            {
                if (sumLine[i - 1] - ma[i - 1] > 0.00001) isGrow[i] = IsTrend;
                else if (sumLine[i - 1] - ma[i - 1] < -0.00001) isGrow[i] = !IsTrend;
                else isGrow[i] = isGrow[i - 1];
            }
        }
        Result =
            new(ScriptType.OSC, isGrow, new double[][] { sumLine, upper, lower, ma }, iBars.DateTime[^1], OnlyLimit);
    }
}
