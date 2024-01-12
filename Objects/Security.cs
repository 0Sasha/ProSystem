using Newtonsoft.Json;

namespace ProSystem;

[Serializable]
public class Security
{
    public string Seccode { get; set; }
    public string? Currency { get; set; }
    public string? Market { get; set; }
    public string? Board { get; set; }
    public string? ShortName { get; set; }
    public string? TradingStatus { get; set; }

    public double TickSize { get; set; }
    public int TickPrecision { get; set; }
    public double TickCost { get; set; }

    public double LotSize { get; set; }
    public int LotPrecision { get; set; }

    public double MinQty { get; set; }
    public double MinPrice { get; set; }
    public double MaxPrice { get; set; }

    public Trade LastTrade { get; set; }
    public double InitReqLong { get; set; }
    public double InitReqShort { get; set; }

    public double RiskrateLong { get; set; }
    public double RiskrateShort { get; set; }
    public double Deposit { get; set; }
    public double Notional { get; set; }

    public Bars? Bars { get; set; }
    public Bars? SourceBars { get; set; }

    [JsonConstructor]
    public Security(string seccode)
    {
        Seccode = seccode;
        LastTrade = new(seccode, DateTime.MinValue, 0, 0);
        MinQty = 1;
    }

    public Security(string board, string seccode) : this(seccode) => Board = board;
}
