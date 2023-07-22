using System;
using System.Linq;
using static ProSystem.MainWindow;

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
    public Position(string seccode)
    {
        Seccode = seccode;
        Security sec = AllSecurities.SingleOrDefault(x => x.Seccode == Seccode);
        if (sec != null)
        {
            ShortName = sec.ShortName;
            Market = sec.Market;

            Market market = Markets.SingleOrDefault(x => x.ID == Market);
            if (market != null) MarketName = market.Name;
            else AddInfo("Position constructor: Не найден рынок по Market актива.");
        }
        else AddInfo("Position constructor: Не найден актив по Seccode позиции.");
    }
}
