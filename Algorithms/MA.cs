using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class MA : Script
{
    private int Per = 20;
    private int TF = 60;
    private bool IsTr = true;
    private bool OnlyLim = true;
    private NameMA NaM = NameMA.SMA;

    public int Period
    {
        get => Per;
        set { Per = value; Notify(); }
    }

    public int IndicatorTF
    {
        get => TF;
        set { TF = value; Notify(); }
    }

    public bool OnlyLimit
    {
        get => OnlyLim;
        set { OnlyLim = value; Notify(); }
    }

    public bool IsTrend
    {
        get => IsTr;
        set { IsTr = value; Notify(); }
    }

    public NameMA NameMA
    {
        get => NaM;
        set { NaM = value; Notify(); }
    }

    public MA(string name) : base(name) { }

    public override ScriptProperties GetScriptProperties()
    {
        bool IsOSC = false;
        string[] UpperProperties = new string[] { "Period", "IndicatorTF" };
        string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit" };
        NameMA[] MAObjects = new NameMA[] { NameMA.SMA, NameMA.EMA, NameMA.DEMA, NameMA.KAMA, NameMA.Median };
        return new(IsOSC, UpperProperties, MiddleProperties, "NameMA", MAObjects);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Symbol.Bars.Compress(IndicatorTF);
        double[] MA;
        if (NameMA == NameMA.SMA) MA = Indicators.SMA(iBars.Close, Period);
        else if (NameMA == NameMA.EMA) MA = Indicators.EMA(iBars.Close, Period);
        else if (NameMA == NameMA.DEMA) MA = Indicators.DEMA(iBars.Close, Period);
        else if (NameMA == NameMA.KAMA) MA = Indicators.KAMA(iBars.Close, Period);
        else if (NameMA == NameMA.Median) MA = Indicators.Median(iBars.Close, Period);
        else throw new Exception("Непредвиденный тип MA");
        MA = Indicators.Synchronize(MA, iBars, Symbol.Bars);

        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 2; i < Symbol.Bars.Close.Length; i++)
        {
            if (MA[i - 1] - MA[i - 2] > 0.00001) IsGrow[i] = IsTrend;
            else if (MA[i - 1] - MA[i - 2] < -0.00001) IsGrow[i] = !IsTrend;
            else IsGrow[i] = IsGrow[i - 1];
        }
        Result = new ScriptResult(ScriptType.Line, IsGrow, new double[][] { MA }, iBars.DateTime[^1], OnlyLimit);
    }
}
