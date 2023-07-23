using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class CHO : Script
{
    private int Per = 5;
    private int PerEx = 20;
    private int TF = 60;
    private bool IsTr = true;
    private bool OnlyLim = true;
    private bool UseCh = true;

    public int Period
    {
        get => Per;
        set { Per = value; Notify(); }
    }

    public int PeriodEx
    {
        get => PerEx;
        set { PerEx = value; Notify(); }
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

    public bool UseChannel
    {
        get => UseCh;
        set { UseCh = value; Notify(); }
    }

    public CHO(string name) : base(name) { }

    public override void Initialize(Tool MyTool, TabItem TabTool)
    {
        bool IsOSC = true;
        string[] UpperProperties = new string[] { "Period", "PeriodEx", "IndicatorTF" };
        string[] MiddleProperties = new string[] { "IsTrend", "OnlyLimit", "UseChannel" };
        MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Symbol.Bars.Compress(IndicatorTF);
        bool OneLevel = PeriodEx < 1;
        double[] Upper = null, Lower = null, SignalLine = null;
        double[] CHO = Indicators.CHO(iBars.High, iBars.Low, iBars.Close, iBars.Volume, Period, Period * 3 + 1);
        if (!OneLevel)
        {
            if (UseChannel)
            {
                var Lines = Indicators.BBands(CHO, PeriodEx, 1.5);
                Upper = Indicators.Synchronize(Lines.Item1, iBars, Symbol.Bars);
                Lower = Indicators.Synchronize(Lines.Item2, iBars, Symbol.Bars);
            }
            else SignalLine = Indicators.Synchronize(Indicators.EMA(CHO, PeriodEx), iBars, Symbol.Bars);
        }
        CHO = Indicators.Synchronize(CHO, iBars, Symbol.Bars);

        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 2; i < Symbol.Bars.Close.Length; i++)
        {
            if (OneLevel)
            {
                if (CHO[i - 1] > 0.00001) IsGrow[i] = IsTrend;
                else if (CHO[i - 1] < -0.00001) IsGrow[i] = !IsTrend;
                else IsGrow[i] = IsGrow[i - 1];
            }
            else if (UseChannel)
            {
                if (CHO[i - 1] - Upper[i - 2] > 0.00001) IsGrow[i] = IsTrend;
                else if (CHO[i - 1] - Lower[i - 2] < -0.00001) IsGrow[i] = !IsTrend;
                else IsGrow[i] = IsGrow[i - 1];
            }
            else
            {
                if (CHO[i - 1] - SignalLine[i - 1] > 0.00001) IsGrow[i] = IsTrend;
                else if (CHO[i - 1] - SignalLine[i - 1] < -0.00001) IsGrow[i] = !IsTrend;
                else IsGrow[i] = IsGrow[i - 1];
            }
        }

        if (OneLevel) Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { CHO }, iBars.DateTime[^1], 0, 0, OnlyLimit);
        else if (UseChannel) Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { CHO, Upper, Lower }, iBars.DateTime[^1], OnlyLimit);
        else Result = new ScriptResult(ScriptType.OSC, IsGrow, new double[][] { CHO, SignalLine }, iBars.DateTime[^1], OnlyLimit);
    }
}
