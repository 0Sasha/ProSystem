using System;

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
    public Bars(int tf) { TF = tf; }
    public Bars(DateTime[] dateTime, double[] open, double[] high,
        double[] low, double[] close, double[] volume, int tf)
    {
        DateTime = dateTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        TF = tf;
    }

    public Bars GetCopy() => (Bars)MemberwiseClone();
}
