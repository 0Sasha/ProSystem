using System;
using System.Collections.Generic;
using System.Linq;

namespace ProSystem;

public static class BarsExtensions
{
    public static Bars Trim(this Bars bars, int firstBar)
    {
        if (bars == null || bars.DateTime == null) throw new ArgumentNullException(nameof(bars));
        if (firstBar < 0 || firstBar >= bars.DateTime.Length)
            throw new ArgumentException("The first bar is < 0 or >= length", nameof(firstBar));

        return new(bars.TF)
        {
            DateTime = bars.DateTime[firstBar..],
            Open = bars.Open[firstBar..],
            High = bars.High[firstBar..],
            Low = bars.Low[firstBar..],
            Close = bars.Close[firstBar..],
            Volume = bars.Volume[firstBar..]
        };
    }

    public static void Write(this Bars bars, string name)
    {
        if (bars == null || bars.DateTime == null) throw new ArgumentNullException(nameof(bars));
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string[] data = new string[bars.Close.Length];

        for (int i = 0; i < bars.Close.Length; i++)
            data[i] = bars.DateTime[i].ToString("yyyyMMdd,HH:mm") + "," +
                bars.Open[i].ToString(ic) + "," + bars.High[i].ToString(ic) + "," +
                bars.Low[i].ToString(ic) + "," + bars.Close[i].ToString(ic) + "," + bars.Volume[i].ToString(ic);

        System.IO.File.WriteAllLines("Data/" + name + ".csv", data);
    }

    public static Bars Compress(this Bars sourceBars, int tf)
    {
        if (sourceBars == null || sourceBars.DateTime == null) throw new ArgumentNullException(nameof(sourceBars));
        if (tf == sourceBars.TF) return sourceBars;
        if (tf < 5 || tf > 780 || tf % sourceBars.TF != 0)
            throw new ArgumentException("Сжатие в заданный ТФ невозможно", nameof(sourceBars));

        List<DateTime> bDateTime = new();
        List<double> bOpen = new(), bHigh = new(), bLow = new(), bClose = new(), bVolume = new();

        int countMarks = (int)Math.Ceiling(1440D / tf);
        TimeSpan timeSpanTF = TimeSpan.FromMinutes(tf);
        TimeSpan[] timeMarks = new TimeSpan[countMarks];
        timeMarks[0] = TimeSpan.Zero.Add(timeSpanTF);
        for (int i = 1; i < countMarks; i++) timeMarks[i] = timeMarks[i - 1].Add(timeSpanTF);

        double max, min, sum;
        int startBigBar = 0, endBigBar = 0;
        Bars bars = GetCopyAndCheck(sourceBars);
        TimeSpan endTimeBigBar = timeMarks.First(x => x > bars.DateTime[0].TimeOfDay);
        for (int i = 0, j; i < bars.DateTime.Length; i++)
        {
            if (i + 1 == bars.DateTime.Length || bars.DateTime[i].Date != bars.DateTime[i + 1].Date) // Последний бар дня/истории
            {
                if (bars.DateTime[i].TimeOfDay >= endTimeBigBar) // Формирование доп. большого бара перед последним большим баром
                {
                    for (j = startBigBar + 1, min = bars.Low[startBigBar], max = bars.High[startBigBar],
                        sum = bars.Volume[startBigBar]; j < i; j++)
                    {
                        if (bars.High[j] > max) max = bars.High[j];
                        if (bars.Low[j] < min) min = bars.Low[j];
                        sum += bars.Volume[j];
                    }

                    bDateTime.Add(bars.DateTime[startBigBar].Date.Add(endTimeBigBar.Add(-timeSpanTF)));
                    bOpen.Add(bars.Open[startBigBar]);
                    bHigh.Add(max);
                    bLow.Add(min);
                    bClose.Add(bars.Close[i - 1]);
                    bVolume.Add(sum);

                    startBigBar = i;
                    endTimeBigBar = timeMarks.First(x => x > bars.DateTime[i].TimeOfDay);
                }
                for (j = startBigBar + 1, min = bars.Low[startBigBar], max = bars.High[startBigBar],
                    sum = bars.Volume[startBigBar]; j < i + 1; j++)
                {
                    if (bars.High[j] > max) max = bars.High[j];
                    if (bars.Low[j] < min) min = bars.Low[j];
                    sum += bars.Volume[j];
                }

                bDateTime.Add(bars.DateTime[startBigBar].Date.Add(endTimeBigBar.Add(-timeSpanTF)));
                bOpen.Add(bars.Open[startBigBar]);
                bHigh.Add(max);
                bLow.Add(min);
                bClose.Add(bars.Close[i]);
                bVolume.Add(sum);

                startBigBar = i + 1;
                endTimeBigBar = i + 1 < bars.Close.Length ? timeMarks.First(x => x > bars.DateTime[i + 1].TimeOfDay) : timeMarks[0];
            }
            else if (bars.DateTime[i].TimeOfDay >= endTimeBigBar) // Открылся следующий большой бар, сжатие предыдущего
            {
                endBigBar = i - 1;
                for (j = startBigBar + 1, min = bars.Low[startBigBar], max = bars.High[startBigBar],
                    sum = bars.Volume[startBigBar]; j < endBigBar + 1; j++)
                {
                    if (bars.High[j] > max) max = bars.High[j];
                    if (bars.Low[j] < min) min = bars.Low[j];
                    sum += bars.Volume[j];
                }

                bDateTime.Add(bars.DateTime[startBigBar].Date.Add(endTimeBigBar.Add(-timeSpanTF)));
                bOpen.Add(bars.Open[startBigBar]);
                bHigh.Add(max);
                bLow.Add(min);
                bClose.Add(bars.Close[endBigBar]);
                bVolume.Add(sum);

                startBigBar = i;
                endTimeBigBar = timeMarks.First(x => x > bars.DateTime[i].TimeOfDay);
            }
        }

        return new Bars(bDateTime.ToArray(), bOpen.ToArray(),
            bHigh.ToArray(), bLow.ToArray(), bClose.ToArray(), bVolume.ToArray(), tf);
    }

    private static Bars GetCopyAndCheck(Bars sourceBars)
    {
        Bars bars = sourceBars.GetCopy();
        for (int i = 0; i < 2; i++)
        {
            if (bars.DateTime.Length != bars.Open.Length || bars.DateTime.Length != bars.High.Length ||
                bars.DateTime.Length != bars.Low.Length || bars.DateTime.Length != bars.Close.Length ||
                bars.DateTime.Length != bars.Volume.Length)
            {
                if (i == 0)
                {
                    System.Threading.Thread.Sleep(1000);
                    bars = sourceBars.GetCopy();
                }
                else throw new ArgumentException("Несоответствие массивов DT/O/H/L/C/V: " +
                    bars.DateTime.Length + "/" + bars.Open.Length + "/" + bars.High.Length + "/" +
                    bars.Low.Length + "/" + bars.Close.Length + "/" + bars.Volume.Length);
            }
            else break;
        }
        return bars;
    }
}
