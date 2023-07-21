using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class Channel : Script
{
    private int Per = 20;
    private double Mu = 2;
    private int TF = 60;
    private bool IsTr = true;
    private bool UsS = true;
    private NameMA NaM = NameMA.SMA;

    public int Period
    {
        get => Per;
        set { Per = value; Notify(); }
    }

    public double Mult
    {
        get => Mu;
        set { Mu = value; Notify(); }
    }

    public int IndicatorTF
    {
        get => TF;
        set { TF = value; Notify(); }
    }

    public bool IsTrend
    {
        get => IsTr;
        set { IsTr = value; Notify(); }
    }

    public bool UseSD
    {
        get => UsS;
        set { UsS = value; Notify(); }
    }

    public NameMA NameMA
    {
        get => NaM;
        set { NaM = value; Notify(); }
    }

    public Channel(string name) : base(name) { }

    public override void Initialize(Tool MyTool, TabItem TabTool)
    {
        bool IsOSC = false;
        string[] UpperProperties = new string[] { "Period", "Mult", "IndicatorTF" };
        string[] MiddleProperties = new string[] { "IsTrend", "UseSD" };
        MyTool.InitializeScript(this, TabTool, IsOSC, UpperProperties, MiddleProperties,
            "NameMA", new NameMA[] { NameMA.SMA, NameMA.WMA, NameMA.DEMA, NameMA.KAMA, NameMA.LR });
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Bars.Compress(Symbol.Bars, IndicatorTF);
        double[] Line;
        if (NameMA == NameMA.SMA) Line = Indicators.SMA(iBars.Close, Period);
        else if (NameMA == NameMA.WMA) Line = Indicators.WMA(iBars.Close, Period);
        else if (NameMA == NameMA.DEMA) Line = Indicators.DEMA(iBars.Close, Period);
        else if (NameMA == NameMA.KAMA) Line = Indicators.KAMA(iBars.Close, Period);
        else if (NameMA == NameMA.LR) Line = Indicators.LinearRegression(iBars.Close, Period);
        else throw new Exception("Непредвиденный тип MA");

        var Bands = UseSD ? Indicators.ChannelSD(Line, Period, Mult) : Indicators.ChannelPC(Line, Mult);
        double[] Upper = Indicators.Synchronize(Bands.Item1, iBars, Symbol.Bars);
        double[] Lower = Indicators.Synchronize(Bands.Item2, iBars, Symbol.Bars);

        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 2; i < Symbol.Bars.Close.Length; i++)
        {
            if (IsGrow[i - 1] != IsTrend &&
                Symbol.Bars.High[i - 1] - Upper[i - 2] > 0.00001 && Symbol.Bars.Close[i - 1] - Lower[i - 2] > 0.00001) IsGrow[i] = IsTrend;
            else if (IsGrow[i - 1] == IsTrend &&
                Symbol.Bars.Low[i - 1] - Lower[i - 2] < -0.00001 && Symbol.Bars.Close[i - 1] - Upper[i - 2] < -0.00001) IsGrow[i] = !IsTrend;
            else IsGrow[i] = IsGrow[i - 1];
        }
        Result = new ScriptResult(ScriptType.Line, IsGrow, new double[][] { Upper, Lower }, iBars.DateTime[^1], true);
    }
}
