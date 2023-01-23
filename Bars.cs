using System;
using System.Linq;
using System.Collections.Generic;
using static ProSystem.MainWindow;
namespace ProSystem;

[Serializable]
public class Bars
{
    public DateTime[] DateTime { get; set; }
    public double[] Open { get; set; }
    public double[] High { get; set; }
    public double[] Low { get; set; }
    public double[] Close { get; set; }
    public double[] Volume { get; set; }
    public int TF { get; private set; }

    public Bars() { }
    public Bars(int TF) { this.TF = TF; }
    public Bars(DateTime[] DateTime, double[] Open, double[] High, double[] Low, double[] Close, double[] Volume, int TF)
    {
        this.DateTime = DateTime;
        this.Open = Open;
        this.High = High;
        this.Low = Low;
        this.Close = Close;
        this.Volume = Volume;
        this.TF = TF;
    }

    public Bars GetCopy() => (Bars)MemberwiseClone();
    public void TrimBars(int FirstBar)
    {
        if (FirstBar > -1 && FirstBar < DateTime.Length)
        {
            DateTime = DateTime[FirstBar..];
            Open = Open[FirstBar..];
            High = High[FirstBar..];
            Low = Low[FirstBar..];
            Close = Close[FirstBar..];
            Volume = Volume[FirstBar..];
        }
        else AddInfo("TrimBars: обрезка баров невозможна.");
    }

    public static Bars Compress(Bars SourceBars, int TF)
    {
        // Проверка соответствия таймфреймов
        if (TF == SourceBars.TF) return SourceBars;
        if (TF % SourceBars.TF != 0)
        {
            AddInfo("Compress: Сжатие в заданный ТФ невозможно. Возвращение исходных баров.");
            return SourceBars;
        }

        List<DateTime> bDateTime = new();
        List<double> bOpen = new();
        List<double> bHigh = new();
        List<double> bLow = new();
        List<double> bClose = new();
        List<double> bVolume = new();

        // Создание массива временных меток заданного таймфрейма
        int CountMarks = (int)Math.Ceiling(1440D / TF);
        TimeSpan TimeSpanTF = TimeSpan.FromMinutes(TF);
        TimeSpan[] TimeMarks = new TimeSpan[CountMarks];
        TimeMarks[0] = TimeSpan.Zero.Add(TimeSpanTF);
        for (int i = 1; i < CountMarks; i++) TimeMarks[i] = TimeMarks[i - 1].Add(TimeSpanTF);

        // Получение зеркальной копии и её проверка
        Bars Bars = SourceBars.GetCopy();
        if (Bars.DateTime.Length != Bars.Open.Length || Bars.DateTime.Length != Bars.High.Length ||
            Bars.DateTime.Length != Bars.Low.Length || Bars.DateTime.Length != Bars.Close.Length ||
            Bars.DateTime.Length != Bars.Volume.Length)
        {
            AddInfo("Compress: Несоответствие массивов DT/O/H/L/C/V: " + Bars.DateTime.Length + "/" + Bars.Open.Length + "/" +
                Bars.High.Length + "/" + Bars.Low.Length + "/" + Bars.Close.Length + "/" + Bars.Volume.Length);
            System.Threading.Thread.Sleep(2000);

            Bars = SourceBars.GetCopy();
            AddInfo("Compress: Длины массивов DT/O/H/L/C/V: " + Bars.DateTime.Length + "/" + Bars.Open.Length + "/" +
                Bars.High.Length + "/" + Bars.Low.Length + "/" + Bars.Close.Length + "/" + Bars.Volume.Length);
        }

        // Сжатие в заданный таймфрейм
        double Max, Min, Sum;
        int StartBigBar = 0, EndBigBar = 0;
        TimeSpan EndTimeBigBar = TimeMarks.First(x => x > Bars.DateTime[0].TimeOfDay);
        for (int i = 0, j; i < Bars.DateTime.Length; i++)
        {
            if (i + 1 == Bars.DateTime.Length || Bars.DateTime[i].Date != Bars.DateTime[i + 1].Date) // Последний бар дня/истории
            {
                if (Bars.DateTime[i].TimeOfDay >= EndTimeBigBar) // Формирование доп. большого бара перед последним большим баром
                {
                    for (j = StartBigBar + 1, Min = Bars.Low[StartBigBar], Max = Bars.High[StartBigBar],
                        Sum = Bars.Volume[StartBigBar]; j < i; j++)
                    {
                        if (Bars.High[j] > Max) Max = Bars.High[j];
                        if (Bars.Low[j] < Min) Min = Bars.Low[j];
                        Sum += Bars.Volume[j];
                    }

                    bDateTime.Add(Bars.DateTime[StartBigBar].Date.Add(EndTimeBigBar.Add(-TimeSpanTF)));
                    bOpen.Add(Bars.Open[StartBigBar]);
                    bHigh.Add(Max);
                    bLow.Add(Min);
                    bClose.Add(Bars.Close[i - 1]);
                    bVolume.Add(Sum);

                    StartBigBar = i;
                    EndTimeBigBar = TimeMarks.First(x => x > Bars.DateTime[i].TimeOfDay);
                }
                for (j = StartBigBar + 1, Min = Bars.Low[StartBigBar], Max = Bars.High[StartBigBar],
                    Sum = Bars.Volume[StartBigBar]; j < i + 1; j++)
                {
                    if (Bars.High[j] > Max) Max = Bars.High[j];
                    if (Bars.Low[j] < Min) Min = Bars.Low[j];
                    Sum += Bars.Volume[j];
                }

                bDateTime.Add(Bars.DateTime[StartBigBar].Date.Add(EndTimeBigBar.Add(-TimeSpanTF)));
                bOpen.Add(Bars.Open[StartBigBar]);
                bHigh.Add(Max);
                bLow.Add(Min);
                bClose.Add(Bars.Close[i]);
                bVolume.Add(Sum);

                StartBigBar = i + 1;
                EndTimeBigBar = i + 1 < Bars.Close.Length ? TimeMarks.First(x => x > Bars.DateTime[i + 1].TimeOfDay) : TimeMarks[0];
            }
            else if (Bars.DateTime[i].TimeOfDay >= EndTimeBigBar) // Открылся следующий большой бар, сжатие предыдущего
            {
                EndBigBar = i - 1;
                for (j = StartBigBar + 1, Min = Bars.Low[StartBigBar], Max = Bars.High[StartBigBar],
                    Sum = Bars.Volume[StartBigBar]; j < EndBigBar + 1; j++)
                {
                    if (Bars.High[j] > Max) Max = Bars.High[j];
                    if (Bars.Low[j] < Min) Min = Bars.Low[j];
                    Sum += Bars.Volume[j];
                }

                bDateTime.Add(Bars.DateTime[StartBigBar].Date.Add(EndTimeBigBar.Add(-TimeSpanTF)));
                bOpen.Add(Bars.Open[StartBigBar]);
                bHigh.Add(Max);
                bLow.Add(Min);
                bClose.Add(Bars.Close[EndBigBar]);
                bVolume.Add(Sum);

                StartBigBar = i;
                EndTimeBigBar = TimeMarks.First(x => x > Bars.DateTime[i].TimeOfDay);
            }
        }

        return new Bars(bDateTime.ToArray(), bOpen.ToArray(),
            bHigh.ToArray(), bLow.ToArray(), bClose.ToArray(), bVolume.ToArray(), TF);
    }
    public static void WriteBars(Bars Bars, string Name)
    {
        var Cu = System.Globalization.CultureInfo.InvariantCulture;
        string[] Data = new string[Bars.Close.Length];

        for (int i = 0; i < Bars.Close.Length; i++)
            Data[i] = Bars.DateTime[i].ToString("yyyyMMdd,HH:mm") + "," +
                Bars.Open[i].ToString(Cu) + "," + Bars.High[i].ToString(Cu) + "," +
                Bars.Low[i].ToString(Cu) + "," + Bars.Close[i].ToString(Cu) + "," + Bars.Volume[i].ToString(Cu);

        System.IO.File.WriteAllLines("Data/" + Name + ".csv", Data);
    }
}
