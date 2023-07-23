using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class DeMarker : Script
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

    public DeMarker(string name) : base(name) { }

    public override void Initialize(Tool MyTool, TabItem TabTool)
    {
        bool IsOSC = true;
        string[] UpperProperties = new string[] { "Period", "Level", "IndicatorTF" };
        string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit" };
        MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Symbol.Bars.Compress(IndicatorTF);
        double[] DeM = Indicators.DeMarker(iBars.High, iBars.Low, Period);
        DeM = Indicators.Synchronize(DeM, iBars, Symbol.Bars);

        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 1; i < Symbol.Bars.Close.Length; i++)
        {
            if (DeM[i - 1] - (50 + Level) > 0.00001) IsGrow[i] = IsTrend;
            else if (DeM[i - 1] - (50 - Level) < -0.00001) IsGrow[i] = !IsTrend;
            else IsGrow[i] = IsGrow[i - 1];
        }
        Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { DeM }, iBars.DateTime[^1], 50, Level, OnlyLimit);
    }
}
