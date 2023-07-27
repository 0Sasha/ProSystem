using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class ROC : Script
{
    private int Per = 20;
    private int Lev = 20;
    private int TF = 60;
    private bool IsTr = true;
    private bool OnlyLim = true;

    public int Period
    {
        get => Per;
        set { Per = value; Notify(); }
    }

    public int Level
    {
        get => Lev;
        set { Lev = value; Notify(); }
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

    public ROC(string name) : base(name) { }

    public override ScriptProperties GetScriptProperties()
    {
        bool IsOSC = true;
        string[] UpperProperties = new string[] { "Period", "Level", "IndicatorTF" };
        string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit" };
        return new(IsOSC, UpperProperties, MiddleProperties);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Symbol.Bars.Compress(IndicatorTF);
        double[] ROC = Indicators.ROC(iBars.Close, Period);
        ROC = Indicators.Synchronize(ROC, iBars, Symbol.Bars);

        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 1; i < Symbol.Bars.Close.Length; i++)
        {
            if (ROC[i - 1] - Level > 0.00001) IsGrow[i] = IsTrend;
            else if (ROC[i - 1] - -Level < -0.00001) IsGrow[i] = !IsTrend;
            else IsGrow[i] = IsGrow[i - 1];
        }
        Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { ROC }, iBars.DateTime[^1], 0, Level, OnlyLimit);
    }
}
