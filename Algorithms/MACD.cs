using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class MACD : Script
{
    private int Per = 10;
    private int Mul = 2;
    private int TF = 60;
    private bool OnlyLim = true;
    private bool IsTr = true;

    public int Period
    {
        get => Per;
        set { Per = value; Notify(); }
    }

    public int Mult
    {
        get => Mul;
        set { Mul = value; Notify(); }
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

    public MACD(string name) : base(name) { }

    public override void Initialize(Tool MyTool, TabItem TabTool)
    {
        bool IsOSC = true;
        string[] UpperProperties = new string[] { "Period", "Mult", "IndicatorTF" };
        string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit" };
        MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
        double[] MACDLine = Indicators.MACD(iBars.Close, Period, Period * Mult);
        double[] SignalLine = Indicators.EMA(MACDLine, (int)(Period * 0.75));
        MACDLine = Indicators.Synchronize(MACDLine, iBars, Symbol.Bars);
        SignalLine = Indicators.Synchronize(SignalLine, iBars, Symbol.Bars);

        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 1; i < Symbol.Bars.Close.Length; i++)
        {
            if (MACDLine[i - 1] - SignalLine[i - 1] > 0.00001) IsGrow[i] = IsTrend;
            else if (MACDLine[i - 1] - SignalLine[i - 1] < -0.00001) IsGrow[i] = !IsTrend;
            else IsGrow[i] = IsGrow[i - 1];
        }
        Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { MACDLine, SignalLine }, iBars.DateTime[^1], OnlyLimit);
    }
}
