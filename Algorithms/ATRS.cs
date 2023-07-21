using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class ATRS : Script
{
    private int Per = 10;
    private int Mul = 0;
    private int PerEx = 1;
    private double Cor = 0;
    private int TF = 60;

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

    public int PeriodEx
    {
        get => PerEx;
        set { PerEx = value; Notify(); }
    }

    public double Correction
    {
        get => Cor;
        set { Cor = value; Notify(); }
    }

    public int IndicatorTF
    {
        get => TF;
        set { TF = value; Notify(); }
    }

    public ATRS(string name) : base(name) { }

    public override void Initialize(Tool MyTool, TabItem TabTool)
    {
        bool IsOSC = false;
        string[] UpperProperties = new string[] { "Period", "Mult", "PeriodEx", "Correction", "IndicatorTF" };
        MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
        double[] StopATR = Indicators.ATRLine(iBars.High, iBars.Low, iBars.Close, Period, Mult, PeriodEx, Correction, Symbol.Decimals);
        StopATR = Indicators.Synchronize(StopATR, iBars, Symbol.Bars);

        double PastStopATR = 0;
        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 1; i < Symbol.Bars.Close.Length; i++)
        {
            if (Math.Abs(PastStopATR - StopATR[i - 1]) > 0.00001 || PastStopATR < 0.00001)
            {
                if (!IsGrow[i - 1] && Symbol.Bars.High[i] - StopATR[i - 1] > 0.00001 || IsGrow[i - 1] && Symbol.Bars.Low[i] - StopATR[i - 1] < -0.00001)
                {
                    IsGrow[i] = !IsGrow[i - 1];
                    PastStopATR = StopATR[i - 1];
                }
                else IsGrow[i] = IsGrow[i - 1];
            }
            else IsGrow[i] = IsGrow[i - 1];
        }
        Result = new ScriptResult(ScriptType.StopLine, IsGrow, new double[][] { StopATR }, iBars.DateTime[^1]);
    }
}
