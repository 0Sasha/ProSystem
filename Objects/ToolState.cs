namespace ProSystem;

public class ToolState
{
    public bool ReadyToTrade { get; set; }
    public bool IsLogging { get; set; }
    public bool IsBidding { get; set; }
    public bool IsNormalPrice { get; set; }

    public double AveragePrice { get; set; }
    public double ATR { get; set; }

    public int Balance { get; set; }
    public int RealBalance { get; set; }

    public double LongReqs { get; set; }
    public double ShortReqs { get; set; }

    public int LongVolume { get; set; }
    public int ShortVolume { get; set; }

    public double LongRealVolume { get; set; }
    public double ShortRealVolume { get; set; }

    public int LongOrderVolume { get; set; }
    public int ShortOrderVolume { get; set; }

    public ToolState(bool readyToTrade, bool isLogging, bool isBidding)
    {
        ReadyToTrade = readyToTrade;
        IsLogging = isLogging;
        IsBidding = isBidding;
    }
}
