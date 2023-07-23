using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class PARS : Script
{
    private double CA = 0.02;
    private double MCA = 0.2;
    private int TF = 60;

    public double CoefAccel
    {
        get { return CA; }
        set { CA = value; Notify(); }
    }

    public double MaxCoef
    {
        get { return MCA; }
        set { MCA = value; Notify(); }
    }

    public int IndicatorTF
    {
        get { return TF; }
        set { TF = value; Notify(); }
    }

    public PARS(string name) : base(name) { }

    public override void Initialize(Tool MyTool, TabItem TabTool)
    {
        bool IsOSC = false;
        string[] UpperProperties = new string[] { "CoefAccel", "MaxCoef", "IndicatorTF" };
        MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Symbol.Bars.Compress(IndicatorTF);
        double[] PARStop = Indicators.PARLine(iBars.High, iBars.Low, CoefAccel, MaxCoef, Symbol.Decimals);
        PARStop = Indicators.Synchronize(PARStop, iBars, Symbol.Bars);

        // Вычисление индикаторов
        double PastPARStop = 0;
        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 1; i < Symbol.Bars.Close.Length; i++)
        {
            if (Math.Abs(PastPARStop - PARStop[i - 1]) > 0.00001 || PastPARStop < 0.00001)
            {
                if (!IsGrow[i - 1] && Symbol.Bars.High[i] - PARStop[i - 1] > 0.00001 || IsGrow[i - 1] && Symbol.Bars.Low[i] - PARStop[i - 1] < -0.00001)
                {
                    IsGrow[i] = !IsGrow[i - 1];
                    PastPARStop = PARStop[i - 1];
                }
                else IsGrow[i] = IsGrow[i - 1];
            }
            else IsGrow[i] = IsGrow[i - 1];
        }
        Result = new ScriptResult(ScriptType.StopLine, IsGrow, new double[][] { PARStop }, iBars.DateTime[^1]);
    }
}
