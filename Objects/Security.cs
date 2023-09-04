using System;

namespace ProSystem;

[Serializable]
public class Security
{
    public string Seccode { get; private set; }
    public string Currency { get; set; }
    public string Board { get; set; }
    public string ShortName { get; set; }
    public string Market { get; set; }
    public int Decimals { get; set; }
    public double MinStep { get; set; }
    public double LotSize { get; set; }
    [field: NonSerialized] public double MinQty { get; set; }
    [field: NonSerialized] public double Notional { get; set; }

    public Trade LastTrade { get; set; }
    public DateTime LastTrDT { get; set; }
    public double InitReqLong { get; set; }
    public double InitReqShort { get; set; }
    public double PointCost { get; set; }
    public double MinStepCost { get; set; }
    public string TradingStatus { get; set; }

    public double RiskrateLong { get; set; }
    public double ReserateLong { get; set; }
    public double RiskrateShort { get; set; }
    public double ReserateShort { get; set; }

    public double MinPrice { get; set; }
    public double MaxPrice { get; set; }
    public double BuyDeposit { get; set; }
    public double SellDeposit { get; set; }

    public Bars Bars { get; set; }
    public Bars SourceBars { get; set; }

    public Security() { }

    public Security(string seccode) => Seccode = seccode;

    public Security(string board, string seccode)
    {
        Board = board;
        Seccode = seccode;
    }
}
