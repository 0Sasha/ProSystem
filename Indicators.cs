using System;

namespace ProSystem;

public static class Indicators
{
    // Линии
    public static double[] ATRLine(double[] High, double[] Low, double[] Close, int Period, int Mult, int PeriodEXTR, double Correction, int Round)
    {
        bool Grow = false;
        double TR, Highest, Lowest, Sup, Res;
        double[] Stop = new double[Close.Length];
        double[] ATR = new double[Close.Length];
        for (int i = PeriodEXTR, x; i < Close.Length; i++)
        {
            if (Mult == 0) ATR[i] = 0;
            else
            {
                TR = Math.Max(High[i] - Low[i], Math.Max(Math.Abs(High[i] - Close[i - 1]), Math.Abs(Low[i] - Close[i - 1])));
                ATR[i] = (ATR[i - 1] * (Period - 1) + TR) / Period;
            }

            for (x = 1, Highest = High[i], Lowest = Low[i]; x < PeriodEXTR; x++)
            {
                if (High[i - x] > Highest) Highest = High[i - x];
                if (Low[i - x] < Lowest) Lowest = Low[i - x];
            }
            Sup = Math.Round(Lowest - Lowest / 100 * Correction - Mult * ATR[i], Round);
            Res = Math.Round(Highest + Highest / 100 * Correction + Mult * ATR[i], Round);

            Stop[i] = Stop[i - 1];
            if (Grow && Low[i] - Stop[i - 1] < -0.000001) { Stop[i] = Res; Grow = false; continue; }
            else if (!Grow && High[i] - Stop[i - 1] > 0.000001) { Stop[i] = Sup; Grow = true; continue; }

            if (!Grow && Res - Stop[i - 1] < -0.000001) Stop[i] = Res;
            else if (Grow && Sup - Stop[i - 1] > 0.000001) Stop[i] = Sup;
        }
        return Stop;
    }
    public static double[] PARLine(double[] High, double[] Low, double KF, double MaxKF, int Round)
    {
        bool FirstTime = true, Grow = true;
        double Ex, Highest, Lowest, NewKF = KF;
        double[] StopPAR = new double[High.Length];
        StopPAR[0] = Low[0]; Ex = High[0];
        for (int i = 1; i < High.Length; i++)
        {
            StopPAR[i] = StopPAR[i - 1];
            if (Grow && Low[i] - StopPAR[i] < -0.000001)
            {
                StopPAR[i] = Ex; Grow = false;
                Ex = Low[i]; NewKF = KF;
                FirstTime = true; continue;
            }
            else if (!Grow && High[i] - StopPAR[i] > 0.000001)
            {
                StopPAR[i] = Ex; Grow = true;
                Ex = High[i]; NewKF = KF;
                FirstTime = true; continue;
            }

            Lowest = Math.Min(Low[i], Low[i - 1]);
            Highest = Math.Max(High[i], High[i - 1]);

            if (Grow)
            {
                if (High[i] - Ex > 0.000001)
                {
                    Ex = High[i];
                    if (!FirstTime) { NewKF += KF; if (NewKF > MaxKF) NewKF = MaxKF; }
                }
                FirstTime = false;
                StopPAR[i] = StopPAR[i - 1] + NewKF * (Ex - StopPAR[i - 1]);
                if (StopPAR[i] > Lowest) StopPAR[i] = Lowest;
            }
            else
            {
                if (Low[i] - Ex < -0.000001)
                {
                    Ex = Low[i];
                    if (!FirstTime) { NewKF += KF; if (NewKF > MaxKF) NewKF = MaxKF; }
                }
                FirstTime = false;
                StopPAR[i] = StopPAR[i - 1] - NewKF * (StopPAR[i - 1] - Ex);
                if (StopPAR[i] < Highest) StopPAR[i] = Highest;
            }
            StopPAR[i] = Math.Round(StopPAR[i], Round);
        }
        return StopPAR;
    }

    public static double[] SMA(double[] Close, int Period, int Round = -1)
    {
        double Sum = 0;
        double[] SMA = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            Sum += Close[i];
            if (i >= Period - 1)
            {
                if (Round == -1) SMA[i] = Sum / Period;
                else SMA[i] = Math.Round(Sum / Period, Round);
                Sum -= Close[i - Period + 1];
            }
        }
        return SMA;
    }
    public static double[] EMA(double[] Close, int Period, int Round = -1)
    {
        double Sum = 0, Mult = 2D / (Period + 1);
        double[] EMA = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            if (i > Period - 1)
                EMA[i] = Round == -1 ? Close[i] * Mult + EMA[i - 1] * (1 - Mult) : Math.Round(Close[i] * Mult + EMA[i - 1] * (1 - Mult), Round);
            else
            {
                Sum += Close[i];
                if (i == Period - 1) EMA[i] = Round == -1 ? Sum / Period : Math.Round(Sum / Period, Round);
            }
        }
        return EMA;
    }
    public static double[] WMA(double[] Close, int Period, int Round = -1)
    {
        double Sum, SumN = 0;
        double[] WMA = new double[Close.Length];
        for (int i = Period; i > 0; i--) SumN += i;

        for (int i = Period, j; i < Close.Length; i++)
        {
            for (j = 0, Sum = 0; j < Period; j++) Sum += Close[i - j] * (Period - j);
            WMA[i] = Round == -1 ? Sum / SumN : Math.Round(Sum / SumN, Round);
        }
        return WMA;
    }
    public static double[] VMA(double[] Close, double[] Volume, int Period, int Round = -1)
    {
        double Sum = 0, SumV = 0;
        double[] VMA = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            Sum += Close[i] * Volume[i];
            SumV += Volume[i];
            if (i >= Period - 1)
            {
                if (Round == -1) VMA[i] = Sum / SumV;
                else VMA[i] = Math.Round(Sum / SumV, Round);
                Sum -= Close[i - Period + 1] * Volume[i - Period + 1];
                SumV -= Volume[i - Period + 1];
            }
        }
        return VMA;
    }
    public static double[] SMMA(double[] Close, int Period, int Round = -1)
    {
        double Sum = 0;
        double[] SMMA = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            if (i > Period - 1)
            {
                Sum = SMMA[i - 1] * Period;
                if (Round == -1) SMMA[i] = (Sum - SMMA[i - 1] + Close[i]) / Period;
                else SMMA[i] = Math.Round((Sum - SMMA[i - 1] + Close[i]) / Period, Round);
            }
            else if (i == Period - 1)
            {
                Sum += Close[i];
                if (Round == -1) SMMA[i] = Sum / Period;
                else SMMA[i] = Math.Round(Sum / Period, Round);
            }
            else Sum += Close[i];
        }
        return SMMA;
    }
    public static double[] DEMA(double[] Close, int Period, int Round = -1)
    {
        double[] DataEMA = EMA(Close, Period);
        double[] Error = new double[Close.Length];
        for (int i = Period; i < Close.Length; i++) Error[i] = Close[i] - DataEMA[i];

        double[] ErrorEMA = EMA(Error, Period);
        double[] DEMA = new double[Close.Length];
        for (int i = Period; i < Close.Length; i++)
            DEMA[i] = Round == -1 ? DataEMA[i] + ErrorEMA[i] : Math.Round(DataEMA[i] + ErrorEMA[i], Round);
        return DEMA;
    }
    public static double[] TEMA(double[] Close, int Period, int Round = -1)
    {
        double[] DataEMA = DEMA(Close, Period);
        double[] Error = new double[Close.Length];
        for (int i = Period; i < Close.Length; i++) Error[i] = Close[i] - DataEMA[i];

        double[] ErrorEMA = EMA(Error, Period);
        double[] TEMA = new double[Close.Length];
        for (int i = Period; i < Close.Length; i++)
            TEMA[i] = Round == -1 ? DataEMA[i] + ErrorEMA[i] : Math.Round(DataEMA[i] + ErrorEMA[i], Round);
        return TEMA;
    }
    public static double[] KAMA(double[] Close, int Period, int Round = -1)
    {
        double Sum = 0, SumMA = 0;
        double[] SSI = new double[Close.Length];
        double[] KAMA = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            Sum += Math.Abs(Close[i] - Close[i - 1]);
            if (i > Period)
            {
                SSI[i] = Math.Abs(Close[i] - Close[i - Period]) / Sum * 0.60215 + 0.06425;
                KAMA[i] = Round == -1 ? KAMA[i - 1] + SSI[i] * SSI[i] * (Close[i] - KAMA[i - 1]) :
                    Math.Round(KAMA[i - 1] + SSI[i] * SSI[i] * (Close[i] - KAMA[i - 1]), Round);
                Sum -= Math.Abs(Close[i - Period + 1] - Close[i - Period]);
            }
            else if (i == Period)
            {
                SumMA += Close[i];
                SSI[i] = Math.Abs(Close[i] - Close[i - Period]) / Sum * 0.60215 + 0.06425;
                KAMA[i] = Round == -1 ? SumMA / Period + SSI[i] * SSI[i] * (Close[i] - SumMA / Period) :
                    Math.Round(SumMA / Period + SSI[i] * SSI[i] * (Close[i] - SumMA / Period), Round);
                Sum -= Math.Abs(Close[i - Period + 1] - Close[i - Period]);
            }
            else SumMA += Close[i];
        }
        return KAMA;
    }
    public static double[] LinearRegression(double[] Close, int Period, int Round = -1)
    {
        double SumX = 0, SumX2 = 0;
        double SumY = 0, SumXY = 0, a, b, c;
        double[] Regression = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            SumX += i;
            SumX2 += i * i;
            SumY += Close[i];
            SumXY += Close[i] * i;

            if (i >= Period - 1)
            {
                c = Period * SumX2 - SumX * SumX;
                a = (SumY * SumX2 - SumX * SumXY) / c;
                b = (Period * SumXY - SumX * SumY) / c;
                Regression[i] = Round == -1 ? a + b * i : Math.Round(a + b * i, Round);

                SumX -= i - Period + 1;
                SumX2 -= (i - Period + 1) * (i - Period + 1);
                SumY -= Close[i - Period + 1];
                SumXY -= Close[i - Period + 1] * (i - Period + 1);
            }
        }
        return Regression;
    }
    public static double[] Median(double[] Close, int Period, int Round = -1)
    {
        int Mid = Period / 2;
        bool Even = Period % 2 == 0;
        double[] Ar, Median = new double[Close.Length];
        for (int i = Period; i < Close.Length; i++)
        {
            Ar = Close[(i - Period + 1)..(i + 1)];
            Array.Sort(Ar);

            if (Even) Median[i] = Round == -1 ? (Ar[Mid] + Ar[Mid - 1]) / 2 : Math.Round((Ar[Mid] + Ar[Mid - 1]) / 2, Round);
            else Median[i] = Ar[Mid];
        }
        return Median;
    }
    public static double[] Vidya(double[] Close, int PeriodSD, double Alpha, int Round = -1)
    {
        double[] Vol = CMO(Close, PeriodSD);
        double[] Vidya = new double[Close.Length];
        Vol[PeriodSD] = Math.Abs(Vol[PeriodSD] / 100);
        Vidya[PeriodSD] = Round == -1 ? Close[PeriodSD] * Alpha * Vol[PeriodSD] + Close[PeriodSD - 1] * (1 - Alpha * Vol[PeriodSD]) :
            Math.Round(Close[PeriodSD] * Alpha * Vol[PeriodSD] + Close[PeriodSD - 1] * (1 - Alpha * Vol[PeriodSD]), Round);
        if (Round == -1)
        {
            for (int i = PeriodSD + 1; i < Close.Length; i++)
            {
                Vol[i] = Math.Abs(Vol[i] / 100);
                Vidya[i] = Close[i] * Alpha * Vol[i] + Vidya[i - 1] * (1 - Alpha * Vol[i]);
            }
        }
        else
        {
            for (int i = PeriodSD + 1; i < Close.Length; i++)
            {
                Vol[i] = Math.Abs(Vol[i] / 100);
                Vidya[i] = Math.Round(Close[i] * Alpha * Vol[i] + Vidya[i - 1] * (1 - Alpha * Vol[i]), Round);
            }
        }
        return Vidya;
    }

    // Осцилляторы
    public static double[] SD(double[] Close, int Period)
    {
        double Sum = 0, SumSq = 0;
        double[] SD = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            Sum += Close[i];
            SumSq += Close[i] * Close[i];
            if (i >= Period - 1)
            {
                SD[i] = Math.Sqrt((SumSq - Sum * Sum / Period) / (Period - 1));
                Sum -= Close[i - Period + 1];
                SumSq -= Close[i - Period + 1] * Close[i - Period + 1];
            }
        }
        return SD;
    }
    public static double[] ATR(double[] High, double[] Low, double[] Close, int Period)
    {
        double TR;
        double[] ATR = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            TR = Math.Max(High[i] - Low[i], Math.Max(Math.Abs(High[i] - Close[i - 1]), Math.Abs(Low[i] - Close[i - 1])));
            ATR[i] = (ATR[i - 1] * (Period - 1) + TR) / Period;
        }
        return ATR;
    }

    public static double[] RSI(double[] Close, int Period)
    {
        double Up, Down;
        double[] WilderUp = new double[Close.Length];
        double[] WilderDown = new double[Close.Length];
        double[] RSI = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            if (Close[i] - Close[i - 1] > 0.000001) { Up = Close[i] - Close[i - 1]; Down = 0; }
            else if (Close[i] - Close[i - 1] < -0.000001) { Up = 0; Down = Close[i - 1] - Close[i]; }
            else { Up = 0; Down = 0; }
            WilderUp[i] = (WilderUp[i - 1] * (Period - 1) + Up) / Period;
            WilderDown[i] = (WilderDown[i - 1] * (Period - 1) + Down) / Period;
            if (i >= Period) RSI[i] = 100 - 100 / (1 + WilderUp[i] / WilderDown[i]);
        }
        return RSI;
    }
    public static double[] StochRSI(double[] Close, int Period)
    {
        double Highest, Lowest;
        double[] MainRSI = RSI(Close, Period);
        double[] StochRSI = new double[Close.Length];
        for (int i = Period, x; i < Close.Length; i++)
        {
            for (x = 1, Highest = MainRSI[i], Lowest = MainRSI[i]; x < Period; x++)
            {
                if (MainRSI[i - x] > Highest) Highest = MainRSI[i - x];
                if (MainRSI[i - x] < Lowest) Lowest = MainRSI[i - x];
            }
            StochRSI[i] = (MainRSI[i] - Lowest) / (Highest - Lowest) * 100;
        }
        return StochRSI;
    }
    public static double[] MFI(double[] High, double[] Low, double[] Close, double[] Volume, int Period)
    {
        double SumPos, SumNeg;
        double[] TP = new double[Close.Length];
        double[] PositiveMF = new double[Close.Length];
        double[] NegativeMF = new double[Close.Length];
        double[] MFI = new double[Close.Length];

        TP[0] = (High[0] + Low[0] + Close[0]) / 3;
        for (int i = 1, x; i < Close.Length; i++)
        {
            TP[i] = (High[i] + Low[i] + Close[i]) / 3;
            if (TP[i] - TP[i - 1] > 0.000001) PositiveMF[i] = TP[i] * Volume[i];
            else if (TP[i] - TP[i - 1] < -0.000001) NegativeMF[i] = TP[i] * Volume[i];

            if (i >= Period)
            {
                for (x = 1, SumPos = PositiveMF[i], SumNeg = NegativeMF[i]; x < Period; x++)
                {
                    SumPos += PositiveMF[i - x];
                    SumNeg += NegativeMF[i - x];
                }
                MFI[i] = 100 - 100 / (1 + SumPos / SumNeg);
            }
        }
        return MFI;
    }
    public static double[] DeMarker(double[] High, double[] Low, int Period)
    {
        double SumMax = 0, SumMin = 0;
        double[] DeMax = new double[High.Length];
        double[] DeMin = new double[High.Length];
        double[] DeMarker = new double[High.Length];
        for (int i = 1; i < High.Length; i++)
        {
            if (High[i] - High[i - 1] > 0.00001) DeMax[i] = High[i] - High[i - 1];
            if (Low[i] - Low[i - 1] < -0.00001) DeMin[i] = Low[i - 1] - Low[i];

            SumMax += DeMax[i];
            SumMin += DeMin[i];

            if (i >= Period)
            {
                DeMarker[i] = SumMax / Period / (SumMax / Period + SumMin / Period) * 100;
                SumMax -= DeMax[i - Period + 1];
                SumMin -= DeMin[i - Period + 1];
            }
        }
        return DeMarker;
    }
    public static double[] Stochastic(double[] High, double[] Low, double[] Close, int Period)
    {
        double Highest, Lowest;
        double[] Stoch = new double[Close.Length];
        for (int i = Period, x; i < Close.Length; i++)
        {
            for (x = 1, Highest = High[i], Lowest = Low[i]; x < Period; x++)
            {
                if (High[i - x] > Highest) Highest = High[i - x];
                if (Low[i - x] < Lowest) Lowest = Low[i - x];
            }
            Stoch[i] = (Close[i] - Lowest) / (Highest - Lowest) * 100;
        }
        return Stoch;
    }

    public static double[] CMO(double[] Close, int Period)
    {
        double SumPos = 0, SumNeg = 0;
        double[] CMO = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            if (Close[i] - Close[i - 1] > 0.000001) SumPos += Close[i] - Close[i - 1];
            else if (Close[i] - Close[i - 1] < -0.000001) SumNeg += Close[i - 1] - Close[i];

            if (i >= Period)
            {
                CMO[i] = (SumPos - SumNeg) / (SumPos + SumNeg) * 100;
                if (Close[i - Period + 1] - Close[i - Period] > 0.000001) SumPos -= Close[i - Period + 1] - Close[i - Period];
                else if (Close[i - Period + 1] - Close[i - Period] < -0.000001) SumNeg -= Close[i - Period] - Close[i - Period + 1];
            }
        }
        return CMO;
    }
    public static double[] CMF(double[] High, double[] Low, double[] Close, double[] Volume, int Period, double Step)
    {
        double Sum = 0, SumVol = 0, Dif;
        double[] CMF = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            SumVol += Volume[i];
            Dif = High[i] - Low[i] > 0.000001 ? High[i] - Low[i] : Step;
            Sum += (Close[i] - Low[i] - (High[i] - Close[i])) / Dif * Volume[i];
            if (i >= Period - 1)
            {
                CMF[i] = Sum / SumVol * 100;
                SumVol -= Volume[i - Period + 1];
                Dif = High[i - Period + 1] - Low[i - Period + 1] > 0.000001 ? High[i - Period + 1] - Low[i - Period + 1] : Step;
                Sum -= (Close[i - Period + 1] - Low[i - Period + 1] - (High[i - Period + 1] - Close[i - Period + 1])) / Dif * Volume[i - Period + 1];
            }
        }
        return CMF;
    }
    public static double[] RVI(double[] Open, double[] High, double[] Low, double[] Close, int Period)
    {
        double SumNum = 0, SumDenom = 0;
        double[] RVI = new double[Close.Length];
        double[] Numerator = new double[Close.Length];
        double[] Denominator = new double[Close.Length];
        for (int i = 3; i < Close.Length; i++)
        {
            Numerator[i] = (Close[i] - Open[i] + 2 * (Close[i - 1] - Open[i - 1]) + 2 * (Close[i - 2] - Open[i - 2]) + (Close[i - 3] - Open[i - 3])) / 6;
            Denominator[i] = (High[i] - Low[i] + 2 * (High[i - 1] - Low[i - 1]) + 2 * (High[i - 2] - Low[i - 2]) + (High[i - 3] - Low[i - 3])) / 6;

            SumNum += Numerator[i];
            SumDenom += Denominator[i];

            if (i > Period + 1)
            {
                RVI[i] = SumNum / Period / (SumDenom / Period) * 100;
                SumNum -= Numerator[i - Period + 1];
                SumDenom -= Denominator[i - Period + 1];
            }
        }
        return RVI;
    }

    public static double[] MACD(double[] Close, int ShortPeriod, int LongPeriod)
    {
        double[] ShortEMA = EMA(Close, ShortPeriod);
        double[] LongEMA = EMA(Close, LongPeriod);
        double[] MACD = new double[Close.Length];
        for (int i = LongPeriod; i < Close.Length; i++) MACD[i] = ShortEMA[i] - LongEMA[i];
        return MACD;
    }
    public static double[] DirectionBars(double[] Close)
    {
        double[] DirBars = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            if (Close[i] - Close[i - 1] > 0.000001)
            {
                if (DirBars[i - 1] > 0) DirBars[i] = DirBars[i - 1] + 1;
                else DirBars[i] = 1;
            }
            else if (Close[i] - Close[i - 1] < -0.000001)
            {
                if (DirBars[i - 1] < 0) DirBars[i] = DirBars[i - 1] - 1;
                else DirBars[i] = -1;
            }
            else DirBars[i] = DirBars[i - 1];
        }
        return DirBars;
    }
    public static double[] ROC(double[] Close, int Period)
    {
        double[] ROC = new double[Close.Length];
        for (int i = Period; i < Close.Length; i++)
            ROC[i] = (Close[i] - Close[i - Period]) / Close[i - Period] * 100;
        return ROC;
    }
    public static double[] CHO(double[] High, double[] Low, double[] Close, double[] Volume, int ShortPeriod, int LongPeriod)
    {
        double[] LineAD = AD(High, Low, Close, Volume);
        double[] ShortEMA = EMA(LineAD, ShortPeriod);
        double[] LongEMA = EMA(LineAD, LongPeriod);
        double[] CHO = new double[Close.Length];
        for (int i = LongPeriod; i < Close.Length; i++) CHO[i] = ShortEMA[i] - LongEMA[i];
        return CHO;
    }
    public static (double[], double[]) DI(double[] High, double[] Low, double[] Close, int Period)
    {
        double[] TR = new double[Close.Length];
        double[] DIPlus = new double[Close.Length];
        double[] DIMinus = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            TR[i] = Math.Max(High[i] - Low[i], Math.Max(Math.Abs(High[i] - Close[i - 1]), Math.Abs(Low[i] - Close[i - 1])));
            if (High[i] - High[i - 1] > 0.000001) DIPlus[i] = High[i] - High[i - 1];
            if (Low[i - 1] - Low[i] > 0.000001) DIMinus[i] = Low[i - 1] - Low[i];
        }

        TR = SMMA(TR, Period);
        DIPlus = SMMA(DIPlus, Period);
        DIMinus = SMMA(DIMinus, Period);
        for (int i = 1; i < Close.Length; i++)
        {
            DIPlus[i] = DIPlus[i] / TR[i];
            DIMinus[i] = DIMinus[i] / TR[i];
        }
        return (DIPlus, DIMinus);
    }

    public static double[] SumLine(double[] Close, int Period)
    {
        double Sum = 0;
        double[] SumLine = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            Sum += Close[i];
            if (i >= Period - 1)
            {
                SumLine[i] = Sum;
                Sum -= Close[i - Period + 1];
            }
        }
        return SumLine;
    }
    public static double[] OBV(double[] Close, double[] Volume)
    {
        double[] OBV = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            if (Close[i] - Close[i - 1] > 0.000001) OBV[i] = OBV[i - 1] + Volume[i];
            else if (Close[i] - Close[i - 1] < -0.000001) OBV[i] = OBV[i - 1] - Volume[i];
            else OBV[i] = OBV[i - 1];
        }
        return OBV;
    }
    public static double[] AD(double[] High, double[] Low, double[] Close, double[] Volume)
    {
        double[] AD = new double[Close.Length];
        for (int i = 1; i < Close.Length; i++)
        {
            if (High[i] - Low[i] > 0.000001) AD[i] = (Close[i] - Low[i] - (High[i] - Close[i])) / (High[i] - Low[i]) * Volume[i] + AD[i - 1];
            else AD[i] = AD[i - 1];
        }
        return AD;
    }

    public static double[] DPO(double[] Close, int Period)
    {
        double Sum = 0;
        double[] SMA = new double[Close.Length];
        double[] DPO = new double[Close.Length];
        for (int i = 0; i < Close.Length; i++)
        {
            Sum += Close[i];
            if (i >= Period - 1)
            {
                SMA[i] = Sum / Period;
                DPO[i] = Close[i] - SMA[i - (Period / 2 + 1)];
                Sum -= Close[i - Period + 1];
            }
        }
        return DPO;
    }
    public static double[] FRC(double[] Close, double[] Volume, int Period)
    {
        double[] MA = SMA(Close, Period);
        double[] FRC = new double[Close.Length];
        for (int i = Period; i < Close.Length; i++) FRC[i] = Volume[i] * (MA[i] - MA[i - 1]);
        return FRC;
    }
    public static double[] CCI(double[] High, double[] Low, double[] Close, int Period)
    {
        double SumTP = 0, SumDis;
        double[] CCI = new double[Close.Length];
        double[] TP = new double[Close.Length];
        double[] SMATP = new double[Close.Length];
        for (int i = 0, j; i < Close.Length; i++)
        {
            TP[i] = (High[i] + Low[i] + Close[i]) / 3;
            SumTP += TP[i];

            if (i >= Period - 1)
            {
                SMATP[i] = SumTP / Period;
                for (j = i, SumDis = 0; j > i - Period; j--) SumDis += Math.Abs(TP[j] - SMATP[i]);

                CCI[i] = (TP[i] - SMATP[i]) / (0.015 * (SumDis / Period));
                SumTP -= TP[i - Period + 1];
            }
        }
        return CCI;
    }
    public static double[] EMV(double[] High, double[] Low, double[] Volume, int Period)
    {
        double[] EMV = new double[High.Length];
        for (int i = 1; i < High.Length; i++)
        {
            if (Volume[i] > 0.1 && High[i] - Low[i] > 0.000001)
                EMV[i] = ((High[i] + Low[i]) / 2 - (High[i - 1] + Low[i - 1]) / 2) / (Volume[i] / 1000000 / (High[i] - Low[i]));
            else EMV[i] = EMV[i - 1];
        }
        EMV = SMA(EMV, Period);
        return EMV;
    }
    public static double[] KVO(double[] High, double[] Low, double[] Close, double[] Volume, int Period)
    {
        int[] Trend = new int[Close.Length];
        double[] VF = new double[Close.Length];
        double[] DM = new double[Close.Length];
        double[] CM = new double[Close.Length];
        DM[0] = High[0] - Low[0];
        for (int i = 1; i < Close.Length; i++)
        {
            DM[i] = High[i] - Low[i];

            if (High[i] + Low[i] + Close[i] - (High[i - 1] + Low[i - 1] + Close[i - 1]) > 0.000001) Trend[i] = 1;
            else Trend[i] = -1;

            if (Trend[i] == Trend[i - 1]) CM[i] = CM[i - 1] + DM[i];
            else CM[i] = DM[i - 1] + DM[i];

            if (CM[i] < 0.000001) VF[i] = Volume[i] * -2 * Trend[i] * 100;
            else VF[i] = Volume[i] * (2 * (DM[i] / CM[i] - 1)) * Trend[i] * 100;
        }

        double[] ShortEMA = EMA(VF, Period);
        double[] LongEMA = EMA(VF, (int)(Period * 1.6));
        double[] KVO = new double[Close.Length];
        for (int i = (int)(Period * 1.6); i < Close.Length; i++) KVO[i] = LongEMA[i] - ShortEMA[i];
        return KVO;
    }

    // Каналы
    public static (double[], double[], double[]) BBands(double[] Close, int Period, double Mult, int Round = -1)
    {
        double[] MA = SMA(Close, Period);
        double[] STD = SD(Close, Period);
        double[] Upper = new double[Close.Length];
        double[] Lower = new double[Close.Length];
        if (Round == -1)
        {
            for (int i = Period; i < Close.Length; i++)
            {
                Upper[i] = MA[i] + STD[i] * Mult;
                Lower[i] = MA[i] - STD[i] * Mult;
            }
        }
        else
        {
            for (int i = Period; i < Close.Length; i++)
            {
                Upper[i] = Math.Round(MA[i] + STD[i] * Mult, Round);
                Lower[i] = Math.Round(MA[i] - STD[i] * Mult, Round);
            }
        }
        return (Upper, Lower, MA);
    }
    public static (double[], double[], double[]) Extremes(double[] High, double[] Low, int Period)
    {
        double[] Highest = new double[High.Length];
        double[] Lowest = new double[High.Length];
        double[] Average = new double[High.Length];
        for (int i = Period, x; i < High.Length; i++)
        {
            for (x = 1, Highest[i] = High[i], Lowest[i] = Low[i]; x < Period; x++)
            {
                if (High[i - x] - Highest[i] > 0.000001) Highest[i] = High[i - x];
                if (Low[i - x] - Lowest[i] < -0.000001) Lowest[i] = Low[i - x];
            }
            Average[i] = (Highest[i] + Lowest[i]) / 2;
        }
        return (Highest, Lowest, Average);
    }
    public static (double[], double[]) ChannelSD(double[] Indicator, int PeriodSD, double Mult, int Round = -1)
    {
        double[] STD = SD(Indicator, PeriodSD);
        double[] Upper = new double[Indicator.Length];
        double[] Lower = new double[Indicator.Length];
        if (Round == -1)
        {
            for (int i = PeriodSD * 2; i < Indicator.Length; i++)
            {
                Upper[i] = Indicator[i] + STD[i] * Mult;
                Lower[i] = Indicator[i] - STD[i] * Mult;
            }
        }
        else
        {
            for (int i = PeriodSD * 2; i < Indicator.Length; i++)
            {
                Upper[i] = Math.Round(Indicator[i] + STD[i] * Mult, Round);
                Lower[i] = Math.Round(Indicator[i] - STD[i] * Mult, Round);
            }
        }
        return (Upper, Lower);
    }
    public static (double[], double[]) ChannelPC(double[] Indicator, double Percent, int Round = -1)
    {
        double[] Upper = new double[Indicator.Length];
        double[] Lower = new double[Indicator.Length];
        if (Round == -1)
        {
            for (int i = 0; i < Indicator.Length; i++)
            {
                Upper[i] = Indicator[i] + Indicator[i] / 100 * Percent;
                Lower[i] = Indicator[i] - Indicator[i] / 100 * Percent;
            }
        }
        else
        {
            for (int i = 0; i < Indicator.Length; i++)
            {
                Upper[i] = Math.Round(Indicator[i] + Indicator[i] / 100 * Percent, Round);
                Lower[i] = Math.Round(Indicator[i] - Indicator[i] / 100 * Percent, Round);
            }
        }
        return (Upper, Lower);
    }

    // Вспомогательные
    public static double[] GetLevelSeries(double Level, int Count)
    {
        double[] LevelSeries = new double[Count];
        for (int i = 0; i < Count; i++) LevelSeries[i] = Level;
        return LevelSeries;
    }

    // Методы для обработки индикаторов
    public static double[] Synchronize(double[] Indicator, Bars iBars, Bars Bars)
    {
        if (Bars.TF == iBars.TF) return Indicator;
        double[] SyncIndicator = new double[Bars.DateTime.Length];
        for (int i = 0, x = 0; i < Bars.DateTime.Length; i++)
        {
            if (x < iBars.DateTime.Length && Bars.DateTime[i] >= iBars.DateTime[x])
            {
                x++;
                if (x > 1) SyncIndicator[i - 1] = Indicator[x - 2];
            }

            if (x > 1) SyncIndicator[i] = Indicator[x - 2];
        }
        SyncIndicator[^1] = Bars.DateTime[^1].AddMinutes(Bars.TF) >= iBars.DateTime[^1].AddMinutes(iBars.TF) ? Indicator[^1] : Indicator[^2];
        return SyncIndicator;
    }
}
