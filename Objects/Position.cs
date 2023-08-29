using System;
using System.ComponentModel;

namespace ProSystem;

[Serializable]
public class Position : INotifyPropertyChanged
{
    private string market;
    private string marketName;
    private string seccode;
    private string shortName;
    private double saldoIn;
    private double saldo;
    private double pl;
    private double amount;
    private double equity;

    public string Market
    {
        get => market;
        set
        {
            if (value == null || value == "") throw new ArgumentNullException(nameof(Market));
            market = value;
            NotifyChange(nameof(Market));
        }
    }

    public string MarketName
    {
        get => marketName;
        set
        {
            if (value == null || value == "") throw new ArgumentNullException(nameof(MarketName));
            marketName = value;
            NotifyChange(nameof(MarketName));
        }
    }

    public string Seccode
    {
        get => seccode;
        set
        {
            if (value == null || value == "") throw new ArgumentNullException(nameof(Seccode));
            seccode = value;
            NotifyChange(nameof(Seccode));
        }
    }

    public string ShortName
    {
        get => shortName;
        set
        {
            if (value == null || value == "") throw new ArgumentNullException(nameof(ShortName));
            shortName = value;
            NotifyChange(nameof(ShortName));
        }
    }

    public double SaldoIn
    {
        get => saldoIn;
        set
        {
            saldoIn = value;
            NotifyChange(nameof(SaldoIn));
        }
    } // Входящая позиция

    public double Saldo
    {
        get => saldo;
        set
        {
            saldo = value;
            NotifyChange(nameof(Saldo));
        }
    } // Текущая позиция

    public double PL
    {
        get => pl;
        set
        {
            pl = value;
            NotifyChange(nameof(PL));
        }
    } // Прибыль/убыток

    public double Amount
    {
        get => amount;
        set
        {
            amount = value;
            NotifyChange(nameof(Amount));
        }
    } // Текущая оценка стоимости позиции в валюте инструмента (за исключением FORTS)

    public double Equity
    {
        get => equity;
        set
        {
            equity = value;
            NotifyChange(nameof(Equity));
        }
    } // Текущая оценка стоимости позиции в рублях (за исключением FORTS)

    public Position() { }

    public Position(string seccode, double saldo)
    {
        Seccode = seccode;
        Saldo = saldo;
    }

    public Position(string seccode, string shortName, string market, string marketName)
    {
        Seccode = seccode;
        ShortName = shortName;
        Market = market;
        MarketName = marketName;
    }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

    private void NotifyChange(string propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
