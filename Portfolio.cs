﻿using System;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using static ProSystem.MainWindow;
namespace ProSystem;

[Serializable]
public class UnitedPortfolio : INotifyPropertyChanged
{
    private double inReqs;
    private double minReqs;

    public string Union { get; set; } // Код юниона
    public double SaldoIn { get; set; } // Входящая оценка стоимости единого портфеля
    public double Saldo { get; set; } // Текущая оценка стоимости единого портфеля
    public double PL { get; set; } // Прибыль/убыток общий
    public double InitReqs
    {
        get => inReqs;
        set
        {
            inReqs = value;
            ShareInitReqs = Math.Round(inReqs / Saldo * 100, 2);
            Task.Run(() =>
            {
                UpdatePositions();
                NotifyChanged();
            });
        }
    } // Начальные требования
    public double MinReqs
    {
        get => minReqs;
        set
        {
            minReqs = value;
            ShareMinReqs = Math.Round(minReqs / Saldo * 100, 2);
            NotifyChanged();
        }
    } // Минимальные требования
    public double Free { get; set; } // Свободные средства
    public double UnrealPL { get; set; } // Нереализованная прибыль/убыток
    public double GO { get; set; } // Размер требуемого ГО FORTS
    public double VarMargin { get; set; } // Вариационная маржа FORTS
    public double FinRes { get; set; } // Финансовый результат последнего клиринга FORTS

    public double ShareBaseAssets { get; private set; } // Доля базовых активов
    public double ShareInitReqsBaseAssets { get; private set; } // Доля начальных требований базовых активов
    public double ShareInitReqs { get; private set; } // Доля начальных требований
    public double ShareMinReqs { get; private set; } // Доля минимальных требования
    public double PotentialShareInitReqs { get; private set; } // Потенциальная доля начальных требований

    public Dictionary<DateTime, int> Equity { get; set; } = new();
    public int AverageEquity
    {
        get
        {
            if (Equity == null || Equity.Count == 0) return 500000;
            if (Equity.Count > 4) return (int)Equity.TakeLast(5).Select(x => x.Value).Average();
            return Equity.Last().Value;
        }
    }

    public List<Position> Positions { get; set; } = new();
    public List<Position> MoneyPositions { get; set; } = new();
    public ObservableCollection<object> AllPositions { get; set; } = new();

    public UnitedPortfolio() { }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;
    private void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public void UpdateSharesAndCheck(Tool[] tools, Settings settings)
    {
        double sumPotInitReqs = 0;
        double sumInitReqsBaseAssets = 0;
        double sumReqsBaseAssets = 0;
        foreach (var tool in tools)
        {
            if (tool.Active)
            {
                if (tool.TradeShare)
                {
                    var inReqs = Saldo / 100 * tool.ShareOfFunds;
                    sumPotInitReqs += inReqs;
                }
                else sumPotInitReqs += tool.NumberOfLots * Math.Max(tool.MySecurity.InitReqLong, tool.MySecurity.InitReqShort);

                if (tool.UseShiftBalance)
                {
                    sumReqsBaseAssets += tool.BaseBalance *
                        (tool.MySecurity.LastTrade.Price / tool.MySecurity.MinStep * tool.MySecurity.MinStepCost);

                    var inReqsBaseAssets = tool.BaseBalance > 0 ?
                        tool.BaseBalance * tool.MySecurity.InitReqLong : -tool.BaseBalance * tool.MySecurity.InitReqShort;

                    sumPotInitReqs += inReqsBaseAssets;
                    sumInitReqsBaseAssets += inReqsBaseAssets;
                }
            }
        }

        PotentialShareInitReqs = Math.Round(sumPotInitReqs / Saldo * 100, 2);
        ShareBaseAssets = Math.Round(sumReqsBaseAssets / Saldo * 100, 2);
        ShareInitReqsBaseAssets = Math.Round(sumInitReqsBaseAssets / Saldo * 100, 2);

        if (ShareBaseAssets > settings.OptShareBaseAssets + settings.ToleranceBaseAssets ||
            ShareBaseAssets < settings.OptShareBaseAssets - settings.ToleranceBaseAssets)
            AddInfo("Portfolio: Доля базовых активов за пределами допустимого отклонения: " +
                ShareBaseAssets + "%", notify: true);

        if (PotentialShareInitReqs > settings.MaxShareInitReqsPortfolio)
            AddInfo("Portfolio: Потенциальные начальные требования портфеля превышают норму: " +
                settings.MaxShareInitReqsPortfolio + "%. PotentialInitReqs: " + PotentialShareInitReqs + "%", notify: true);
    }
    public void UpdatePositions()
    {
        try
        {
            Positions.RemoveAll(x => x.Saldo == 0);
            AllPositions = new(MoneyPositions.Concat(Positions.OrderBy(x => x.ShortName))) { this };
            Window.Dispatcher.Invoke(() =>
            {
                Window.PortfolioView.ItemsSource = AllPositions;
                Window.PortfolioView.ScrollIntoView(this);
            });
        }
        catch (Exception ex) { AddInfo("UpdatePositions исключение: " + ex.Message); }
    }
    public void UpdateEquity(DateTime dateTimeOpenPeriod)
    {
        Equity[dateTimeOpenPeriod] = (int)Portfolio.Saldo;
        NotifyChanged();
    }
    public bool CheckEquity(int toleranceEquity)
    {
        int range = AverageEquity / 100 * toleranceEquity;
        if (Saldo < AverageEquity - range || Saldo > AverageEquity + range)
        {
            AddInfo("Стоимость портфеля за пределами допустимого отклонения.", notify: true);
            return false;
        }
        return true;
    }
}

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
