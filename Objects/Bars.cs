using Newtonsoft.Json;

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
    public int TF { get; set; }

    [JsonConstructor]
    public Bars(DateTime[] dateTime, double[] open, double[] high,
        double[] low, double[] close, double[] volume, int tf)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tf);
        ArgumentOutOfRangeException.ThrowIfZero(dateTime.Length);
        var l = dateTime.Length;
        if (open.Length != l || high.Length != l || low.Length != l || close.Length != l || volume.Length != l)
            throw new ArgumentException("Lengths are different");

        TF = tf;
        DateTime = dateTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }

    public Bars GetCopy() => (Bars)MemberwiseClone();
}
