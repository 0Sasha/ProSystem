namespace ProSystem;

public class ToolState
{
    public bool ReadyToTrade { get; set; }
    public bool IsLogging { get; set; }
    public bool IsBidding { get; set; }
    public bool IsNormalPrice { get; set; }

    public double AveragePrice { get; set; }
    public double ATR { get; set; }

    public double Balance { get; set; }
    public double RealBalance { get; set; }

    public double LongReqs { get; set; }
    public double ShortReqs { get; set; }

    public double LongVolume { get; set; }
    public double ShortVolume { get; set; }

    public double LongRealVolume { get; set; }
    public double ShortRealVolume { get; set; }

    public double LongOrderVolume { get; set; }
    public double ShortOrderVolume { get; set; }

    public ToolState(bool readyToTrade, bool isLogging, bool isBidding)
    {
        ReadyToTrade = readyToTrade;
        IsLogging = isLogging;
        IsBidding = isBidding;
    }
}
