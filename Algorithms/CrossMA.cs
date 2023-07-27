using System;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

[Serializable]
internal class CrossMA : Script
{
    private int Per = 10;
    private int Mul = 2;
    private int TF = 60;
    private bool OnlyLim = true;
    private bool IsCrMALim = false;
    private NameMA NaM = NameMA.SMA;

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

    public bool IsCrossMALim
    {
        get => IsCrMALim;
        set { IsCrMALim = value; Notify(); }
    }

    public NameMA NameMA
    {
        get => NaM;
        set { NaM = value; Notify(); }
    }

    public CrossMA(string name) : base(name) { }

    public override ScriptProperties GetScriptProperties()
    {
        bool IsOSC = false;
        string[] UpperProperties = new string[] { "Period", "Mult", "IndicatorTF" };
        string[] MiddleProperties = new string[] { "IsCrossMALim", "OnlyLimit" };
        NameMA[] MAObjects = new NameMA[] { NameMA.SMA, NameMA.EMA, NameMA.SMMA, NameMA.DEMA, NameMA.KAMA };
        return new(IsOSC, UpperProperties, MiddleProperties, "NameMA", MAObjects);
    }

    public override void Calculate(Security Symbol)
    {
        Bars iBars = Symbol.Bars.Compress(IndicatorTF);
        double[] ShortMA, LongMA;
        if (NameMA == NameMA.SMA)
        {
            ShortMA = Indicators.SMA(iBars.Close, Period, Symbol.Decimals);
            LongMA = Indicators.SMA(iBars.Close, Period * Mult, Symbol.Decimals);
        }
        else if (NameMA == NameMA.EMA)
        {
            ShortMA = Indicators.EMA(iBars.Close, Period, Symbol.Decimals);
            LongMA = Indicators.EMA(iBars.Close, Period * Mult, Symbol.Decimals);
        }
        else if (NameMA == NameMA.SMMA)
        {
            ShortMA = Indicators.SMMA(iBars.Close, Period, Symbol.Decimals);
            LongMA = Indicators.SMMA(iBars.Close, Period * Mult, Symbol.Decimals);
        }
        else if (NameMA == NameMA.DEMA)
        {
            ShortMA = Indicators.DEMA(iBars.Close, Period, Symbol.Decimals);
            LongMA = Indicators.DEMA(iBars.Close, Period * Mult, Symbol.Decimals);
        }
        else if (NameMA == NameMA.KAMA)
        {
            ShortMA = Indicators.KAMA(iBars.Close, Period, Symbol.Decimals);
            LongMA = Indicators.KAMA(iBars.Close, Period * Mult, Symbol.Decimals);
        }
        else throw new Exception("Непредвиденный тип MA");
        ShortMA = Indicators.Synchronize(ShortMA, iBars, Symbol.Bars);
        LongMA = Indicators.Synchronize(LongMA, iBars, Symbol.Bars);

        bool[] IsGrow = new bool[Symbol.Bars.Close.Length];
        for (int i = 1; i < Symbol.Bars.Close.Length; i++)
        {
            if (ShortMA[i - 1] - LongMA[i - 1] > 0.00001) IsGrow[i] = true;
            else if (ShortMA[i - 1] - LongMA[i - 1] < -0.00001) IsGrow[i] = false;
            else IsGrow[i] = IsGrow[i - 1];
        }
        ScriptType Type = IsCrossMALim ? ScriptType.LimitLine : ScriptType.Line;
        Result = new ScriptResult(Type, IsGrow, new double[][] { ShortMA, LongMA }, iBars.DateTime[^1], OnlyLimit);
    }
}
