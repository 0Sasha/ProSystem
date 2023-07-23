using System;

namespace ProSystem;

[Serializable]
public class Position
{
    public string Market { get; set; }
    public string MarketName { get; set; }
    public string Seccode { get; set; }
    public string ShortName { get; set; }
    public double SaldoIn { get; set; }
    public double Saldo { get; set; }
    public double PL { get; set; } // Прибыль/убыток
    public double Amount { get; set; } // Текущая оценка стоимости позиции в валюте инструмента (за исключением FORTS)
    public double Equity { get; set; } // Текущая оценка стоимости позиции в рублях (за исключением FORTS)
    public Position() { }
    public Position(string seccode, string shortName, string market, string marketName)
    {
        Seccode = seccode;
        ShortName = shortName;
        Market = market;
        MarketName = marketName;
    }
}
