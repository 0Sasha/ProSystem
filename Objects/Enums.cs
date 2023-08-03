namespace ProSystem;

public enum NameMA
{
    SMA, EMA, WMA, VMA, SMMA,
    DEMA, TEMA, KAMA, LR, Median
}

public enum ScriptType
{
    OSC, StopLine, LimitLine, Line
}

public enum ConnectionState
{
    Disconnected, Connecting, Connected, Disconnecting
}

public enum PositionType
{
    Neutral, Long, Short
}

public enum OrderType
{
    Limit, Conditional, Market
}
