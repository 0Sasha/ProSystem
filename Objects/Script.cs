using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;

namespace ProSystem;

[Serializable]
public abstract class Script : INotifyPropertyChanged
{
    protected Order? lastExecuted;
    protected PositionType curPosition;
    protected ScriptProperties? properties;

    [JsonIgnore]
    [field: NonSerialized]
    protected TextBlock infoBlock = new();

    [field: NonSerialized]
    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual string Name { get; set; }

    public virtual Order? ActiveOrder { get; set; }

    public virtual Order? LastExecuted
    {
        get => lastExecuted;
        set
        {
            lastExecuted = value;
            if (lastExecuted != null)
                CurrentPosition = lastExecuted.Side == "B" ? PositionType.Long : PositionType.Short;
        }
    }

    public virtual PositionType CurrentPosition
    {
        get => curPosition;
        set { curPosition = value; NotifyChange(); }
    }

    [JsonIgnore]
    [field: NonSerialized]
    public virtual ScriptResult? Result { get; set; }

    [JsonIgnore]
    public virtual TextBlock InfoBlock
    {
        get => infoBlock;
        set { infoBlock = value; NotifyChange(); }
    }

    public virtual ObservableCollection<Order> Orders { get; set; } = [];

    public virtual ObservableCollection<Trade> Trades { get; set; } = [];

    public ScriptProperties? Properties { get => properties; }

    public Script(string name) => Name = name;

    public abstract void Calculate(Security symbol);

    public override string ToString() => GetType().Name;

    protected virtual void NotifyChange(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected virtual bool[] GetGrowLineForLevels(int length, bool isTrend, double[] indicator, int upper, int lower)
    {
        var isGrow = new bool[length];
        for (int i = 2; i < isGrow.Length; i++)
        {
            if (indicator[i - 1].More(upper)) isGrow[i] = isTrend;
            else if (indicator[i - 1].Less(lower)) isGrow[i] = !isTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        return isGrow;
    }

    protected virtual bool[] GetGrowLineForSignal(int length, bool isTrend, double[] indicator, double[] signal)
    {
        var isGrow = new bool[length];
        for (int i = 2; i < isGrow.Length; i++)
        {
            if (indicator[i - 1].More(signal[i - 1])) isGrow[i] = isTrend;
            else if (indicator[i - 1].Less(signal[i - 1])) isGrow[i] = !isTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        return isGrow;
    }

    protected virtual bool[] GetGrowLineForChannel(int length, bool isTrend, double[] indicator, double[] upper, double[] lower)
    {
        var isGrow = new bool[length];
        for (int i = 2; i < isGrow.Length; i++)
        {
            if (indicator[i - 1].More(upper[i - 2])) isGrow[i] = isTrend;
            else if (indicator[i - 1].Less(lower[i - 2])) isGrow[i] = !isTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        return isGrow;
    }

    protected virtual bool[] GetGrowLineForChannel(Bars bars, bool isTrend, double[] upper, double[] lower)
    {
        var isGrow = new bool[bars.Close.Length];
        for (int i = 2; i < isGrow.Length; i++)
        {
            if (isGrow[i - 1] != isTrend &&
                bars.High[i - 1].More(upper[i - 2]) && bars.Close[i - 1].More(lower[i - 2])) isGrow[i] = isTrend;
            else if (isGrow[i - 1] == isTrend &&
                bars.Low[i - 1].Less(lower[i - 2]) && bars.Close[i - 1].Less(upper[i - 2])) isGrow[i] = !isTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        return isGrow;
    }

    protected virtual bool[] GetGrowLineForStop(Bars bars, double[] stopLine)
    {
        var pastStopLine = 0D;
        var isGrow = new bool[bars.Close.Length];
        for (int i = 1; i < isGrow.Length; i++)
        {
            if (!pastStopLine.Eq(stopLine[i - 1]) || pastStopLine.LessEq(0))
            {
                if (!isGrow[i - 1] && bars.High[i].More(stopLine[i - 1]) ||
                    isGrow[i - 1] && bars.Low[i].Less(stopLine[i - 1]))
                {
                    isGrow[i] = !isGrow[i - 1];
                    pastStopLine = stopLine[i - 1];
                }
                else isGrow[i] = isGrow[i - 1];
            }
            else isGrow[i] = isGrow[i - 1];
        }
        return isGrow;
    }

    protected virtual bool[] GetGrowLineForCross(int length, bool isTrend, double[] shortMA, double[] longMA)
    {
        var isGrow = new bool[length];
        for (int i = 1; i < isGrow.Length; i++)
        {
            if (shortMA[i - 1].More(longMA[i - 1])) isGrow[i] = isTrend;
            else if (shortMA[i - 1].Less(longMA[i - 1])) isGrow[i] = !isTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        return isGrow;
    }

    protected virtual bool[] GetGrowLineForDirection(int length, bool isTrend, double[] line)
    {
        var isGrow = new bool[length];
        for (int i = 2; i < isGrow.Length; i++)
        {
            if (line[i - 1].More(line[i - 2])) isGrow[i] = isTrend;
            else if (line[i - 1].Less(line[i - 2])) isGrow[i] = !isTrend;
            else isGrow[i] = isGrow[i - 1];
        }
        return isGrow;
    }

    protected virtual void CalculateEndlessOSC(Bars bars, Bars iBars,
        double[] indicator, int periodEx, bool useChannel, bool isTrend, bool onlyLimit)
    {
        var oneLevel = periodEx < 1;
        if (oneLevel)
        {
            indicator = Indicators.Synchronize(indicator, iBars, bars);

            var isGrow = GetGrowLineForLevels(bars.Close.Length, isTrend, indicator, 0, 0);
            Result = new(ScriptType.OSC, isGrow, [indicator], iBars.DateTime[^1], 0, 0, onlyLimit);
        }
        else if (useChannel)
        {
            var lines = Indicators.BBands(indicator, periodEx, 1.5);
            var upper = Indicators.Synchronize(lines.Item1, iBars, bars);
            var lower = Indicators.Synchronize(lines.Item2, iBars, bars);
            indicator = Indicators.Synchronize(indicator, iBars, bars);

            var isGrow = GetGrowLineForChannel(bars.Close.Length, isTrend, indicator, upper, lower);
            Result = new(ScriptType.OSC, isGrow, [indicator, upper, lower], iBars.DateTime[^1], onlyLimit);
        }
        else
        {
            var signalLine = Indicators.Synchronize(Indicators.EMA(indicator, periodEx), iBars, bars);
            indicator = Indicators.Synchronize(indicator, iBars, bars);

            var isGrow = GetGrowLineForSignal(bars.Close.Length, isTrend, indicator, signalLine);
            Result = new(ScriptType.OSC, isGrow, [indicator, signalLine], iBars.DateTime[^1], onlyLimit);
        }
    }

    protected virtual void CalculateTotalOSC(Bars bars, Bars iBars,
        double[] indicator, int period, bool useChannel, bool channelBands, bool isTrend, bool onlyLimit)
    {
        if (useChannel)
        {
            var lines = channelBands ?
                Indicators.BBands(indicator, period, 1.5) : Indicators.Extremes(indicator, indicator, period);
            var upper = Indicators.Synchronize(lines.Item1, iBars, bars);
            var lower = Indicators.Synchronize(lines.Item2, iBars, bars);
            indicator = Indicators.Synchronize(indicator, iBars, bars);

            var isGrow = GetGrowLineForChannel(bars.Close.Length, isTrend, indicator, upper, lower);
            Result = new(ScriptType.OSC, isGrow, [indicator, upper, lower], iBars.DateTime[^1], onlyLimit);
        }
        else
        {
            var ma = Indicators.Synchronize(Indicators.EMA(indicator, period), iBars, bars);
            indicator = Indicators.Synchronize(indicator, iBars, bars);

            var isGrow = GetGrowLineForSignal(bars.Close.Length, isTrend, indicator, ma);
            Result = new(ScriptType.OSC, isGrow, [indicator, ma], iBars.DateTime[^1], onlyLimit);
        }
    }
}
