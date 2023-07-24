using System;
using System.Linq;
using System.Collections.Generic;

namespace ProSystem;

internal class PortfolioManager : IPortfolioManager
{
    private readonly Action<string> Inform;

    public UnitedPortfolio Portfolio { get; }

    public PortfolioManager(UnitedPortfolio portfolio, Action<string> inform)
    {
        Portfolio = portfolio;
        Inform = inform;
    }

    public void UpdateEquity()
    {
        Portfolio.Equity[DateTime.Today.AddDays(-1)] = (int)Portfolio.Saldo;
        Portfolio.Notify("Equity");
    }

    public void UpdatePositions()
    {
        Portfolio.Positions.RemoveAll(x => x.Saldo == 0);
        Portfolio.AllPositions =
            new(Portfolio.MoneyPositions.Concat(Portfolio.Positions.OrderBy(x => x.ShortName))) { Portfolio };
        Portfolio.Notify("Positions");
    }

    public void UpdateShares(IEnumerable<Tool> tools)
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
                    var inReqs = Portfolio.Saldo / 100 * tool.ShareOfFunds;
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

        Portfolio.PotentialShareInitReqs = Math.Round(sumPotInitReqs / Portfolio.Saldo * 100, 2);
        Portfolio.ShareBaseAssets = Math.Round(sumReqsBaseAssets / Portfolio.Saldo * 100, 2);
        Portfolio.ShareInitReqsBaseAssets = Math.Round(sumInitReqsBaseAssets / Portfolio.Saldo * 100, 2);
        Portfolio.Notify("Shares");
    }

    public bool CheckEquity(Settings settings)
    {
        int range = Portfolio.AverageEquity / 100 * settings.ToleranceEquity;
        if (Portfolio.Saldo < Portfolio.AverageEquity - range || Portfolio.Saldo > Portfolio.AverageEquity + range)
        {
            Inform("Стоимость портфеля за пределами допустимого отклонения.");
            return false;
        }
        return true;
    }

    public bool CheckShares(Settings settings)
    {
        if (Portfolio.PotentialShareInitReqs > settings.MaxShareInitReqsPortfolio)
        {
            Inform("Portfolio: Потенциальные начальные требования портфеля превышают норму: " +
               settings.MaxShareInitReqsPortfolio + "%. PotentialInitReqs: " +
               Portfolio.PotentialShareInitReqs + "%");
            return false;
        }

        if (Portfolio.ShareBaseAssets > settings.OptShareBaseAssets + settings.ToleranceBaseAssets ||
            Portfolio.ShareBaseAssets < settings.OptShareBaseAssets - settings.ToleranceBaseAssets)
        {
            Inform("Portfolio: Доля базовых активов за пределами допустимого отклонения: " +
                Portfolio.ShareBaseAssets + "%");
            return false;
        }
        return true;
    }
}
