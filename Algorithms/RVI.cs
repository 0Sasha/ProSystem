﻿using System;

namespace ProSystem.Algorithms;

[Serializable]
internal class RVI : Script
{
    private int period = 20;
    private int level = 20;
    private int tf = 60;
    private bool isTrend = true;
    private bool onlyLimit = true;

    public int Period
    {
        get => period;
        set { period = value; Notify(); }
    }

    public int Level
    {
        get => level;
        set { level = value; Notify(); }
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

    public RVI(string name) : base(name)
    {
        var isOSC = true;
        var upper = new[] { nameof(Period), nameof(Level), nameof(IndicatorTF) };
        var middle = new[] { nameof(IsTrend), nameof(OnlyLimit) };
        properties = new(isOSC, upper, middle);
    }

    public override void Calculate(Security symbol)
    {
        var iBars = symbol.Bars.Compress(IndicatorTF);
        var rvi = Indicators.RVI(iBars.Open, iBars.High, iBars.Low, iBars.Close, Period);
        rvi = Indicators.Synchronize(rvi, iBars, symbol.Bars);

        var isGrow = new bool[symbol.Bars.Close.Length];
        for (int i = 1; i < symbol.Bars.Close.Length; i++)
        {
            if (rvi[i - 1] - Level > 0.00001) isGrow[i] = IsTrend;
            else if (rvi[i - 1] - -Level < -0.00001) isGrow[i] = !IsTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        Result = new(ScriptType.OSC, isGrow, new double[][] { rvi }, iBars.DateTime[^1], 0, Level, OnlyLimit);
    }
}
