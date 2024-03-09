namespace ProSystem.Services;

internal class PortfolioManager : IPortfolioManager
{
    private readonly AddInformation AddInfo;
    private readonly TradingSystem TradingSystem;

    private Settings Settings { get => TradingSystem.Settings; }
    private Connector Connector { get => TradingSystem.Connector; }
    private Portfolio Portfolio { get => TradingSystem.Portfolio; }

    public PortfolioManager(TradingSystem tradingSystem, AddInformation addInfo)
    {
        TradingSystem = tradingSystem;
        AddInfo = addInfo;
    }


    public bool CheckEquity()
    {
        var range = Portfolio.AverageEquity / 100D * Settings.ToleranceEquity;
        if (Portfolio.Saldo < Portfolio.AverageEquity - range || Portfolio.Saldo > Portfolio.AverageEquity + range)
        {
            AddInfo("Portfolio saldo is out of scope", notify: true);
            return false;
        }
        return true;
    }

    public void UpdateEquity()
    {
        Portfolio.Equity[DateTime.Today.AddDays(-1)] = (int)Portfolio.Saldo;
        Portfolio.NotifyChange(nameof(Portfolio.AverageEquity));
    }

    public void UpdatePositions()
    {
        Portfolio.Positions.RemoveAll(x => x.Saldo == 0);
        Portfolio.AllPositions =
            new(Portfolio.MoneyPositions.Concat(Portfolio.Positions.OrderBy(x => x.ShortName))) { Portfolio };
        Portfolio.NotifyChange(nameof(Portfolio.Positions));
    }

    public async Task CheckPortfolioAsync()
    {
        if (Connector.ServerTime > DateTime.Today.AddMinutes(840) &&
            Connector.ServerTime < DateTime.Today.AddMinutes(845) || !CheckEquity()) return;

        if (RequirementsAreNormal())
        {
            CheckRequirements();
            CheckShares();
            CheckIndependentPositions();
        }
        else
        {
            AddInfo("Requirements are above the norm: " + Settings.MaxShareMinReqsPortfolio + "%/" +
                Settings.MaxShareInitReqsPortfolio + "% MinReqs/InitReqs: " +
                Math.Round(Portfolio.MinReqs / Portfolio.Saldo * 100, 2) + "%/" +
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2) + "%", notify: true);
            await NormalizePortfolioAsync();
        }
    }


    private bool RequirementsAreNormal()
    {
        double maxMinReqs = Portfolio.Saldo / 100 * Settings.MaxShareMinReqsPortfolio;
        double maxInitReqs = Portfolio.Saldo / 100 * Settings.MaxShareInitReqsPortfolio;
        return Portfolio.MinReqs < maxMinReqs && Portfolio.InitReqs < maxInitReqs;
    }

    private void CheckRequirements()
    {
        double closeMinReqs = Portfolio.Saldo / 100 * (Settings.MaxShareMinReqsPortfolio - 7);
        double closeInitReqs = Portfolio.Saldo / 100 * (Settings.MaxShareInitReqsPortfolio - 7);
        if (Portfolio.MinReqs > closeMinReqs || Portfolio.InitReqs > closeInitReqs)
        {
            AddInfo("Requirements are close to limits: " + Settings.MaxShareMinReqsPortfolio + "%/" +
                Settings.MaxShareInitReqsPortfolio + "% MinReqs/InitReqs: " +
                Math.Round(Portfolio.MinReqs / Portfolio.Saldo * 100, 2) + "%/" +
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2) + "%", notify: true);
        }
    }

    private void CheckShares()
    {
        double sumPotInitReqs = 0;
        double sumInitReqsBaseAssets = 0;
        double sumReqsBaseAssets = 0;
        foreach (var tool in TradingSystem.Tools.ToArray())
        {
            if (tool.Active)
            {
                if (tool.TradeShare)
                {
                    var inReqs = Portfolio.Saldo / 100 * tool.ShareOfFunds;
                    sumPotInitReqs += inReqs;
                }
                else sumPotInitReqs +=
                        tool.HardQty * Math.Max(tool.Security.InitReqLong, tool.Security.InitReqShort);

                if (tool.UseShiftBalance)
                {
                    if (tool.Security.LastTrade.Price.More(0))
                    {
                        sumReqsBaseAssets += tool.BaseBalance *
                            (tool.Security.LastTrade.Price / tool.Security.TickSize * tool.Security.TickCost);
                    }
                    else AddInfo("CheckPortfolioAsync: there is no last trade: " + tool.Security.Seccode, true, true);

                    var inReqsBaseAssets = tool.BaseBalance.More(0) ?
                        tool.BaseBalance * tool.Security.InitReqLong :
                        -tool.BaseBalance * tool.Security.InitReqShort;

                    sumPotInitReqs += inReqsBaseAssets;
                    sumInitReqsBaseAssets += inReqsBaseAssets;
                }
            }
        }

        Portfolio.PotentialShareInitReqs = Math.Round(sumPotInitReqs / Portfolio.Saldo * 100, 2);
        Portfolio.ShareBaseAssets = Math.Round(sumReqsBaseAssets / Portfolio.Saldo * 100, 2);
        Portfolio.ShareInitReqsBaseAssets = Math.Round(sumInitReqsBaseAssets / Portfolio.Saldo * 100, 2);

        if (Portfolio.PotentialShareInitReqs > Settings.MaxShareInitReqsPortfolio)
        {
            AddInfo("Potential share of InitReqs are above the norm: " +
               Settings.MaxShareInitReqsPortfolio + "%. PotentialInitReqs: " +
               Portfolio.PotentialShareInitReqs + "%", notify: true);
        }
        if (Portfolio.ShareBaseAssets > Settings.OptShareBaseAssets + Settings.ToleranceBaseAssets ||
            Portfolio.ShareBaseAssets < Settings.OptShareBaseAssets - Settings.ToleranceBaseAssets)
        {
            AddInfo("Share of base assets is out of scope: " + Portfolio.ShareBaseAssets + "%", notify: true);
        }
        if (Portfolio.ShareInitReqs > Portfolio.PotentialShareInitReqs)
        {
            AddInfo("ShareInitReqs > PotentialShareInitReqs: " +
               Portfolio.ShareInitReqs + "/" + Portfolio.PotentialShareInitReqs);
        }
    }

    private void CheckIndependentPositions()
    {
        foreach (var position in Portfolio.Positions.ToArray())
        {
            if ((int)position.Saldo != 0 && TradingSystem.Tools.ToArray()
                .SingleOrDefault(x => x.Active && x.Security.Seccode == position.Seccode) == null)
                AddInfo("Independent position: " + position.Seccode, notify: true);
        }
    }

    private async Task NormalizePortfolioAsync()
    {
        if (Connector.Connection == ConnectionState.Connected)
        {
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            await Task.Delay(5000);
            if (RequirementsAreNormal()) return;

            var tools = TradingSystem.Tools.ToArray();
            await CloseIndependentPositionsAsync(tools);
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            await Task.Delay(5000);
            if (RequirementsAreNormal()) return;

            await DeactivateLargePositionToolsAsync(tools);
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            await Task.Delay(5000);
            if (RequirementsAreNormal()) return;

            await DeactivateLessPriorityToolsAsync(tools);
        }
        else AddInfo("NormalizePortfolio: there is no connection");
    }


    private async Task CloseIndependentPositionsAsync(IEnumerable<Tool> tools)
    {
        foreach (var position in Portfolio.Positions.ToArray())
        {
            if ((int)position.Saldo != 0 &&
                tools.SingleOrDefault(x => x.Active && x.Security.Seccode == position.Seccode) == null)
            {
                if (await CancelActiveOrdersAsync(position.Seccode))
                {
                    if (await ClosePositionByMarketAsync(position))
                    {
                        await CancelActiveOrdersAsync(position.Seccode);
                        AddInfo("Unknown position is closed: " + position.Seccode);
                    }
                    else AddInfo("Failed to close the unknown position: " + position.Seccode);
                }
                else AddInfo("Failed to cancel orders before closing the position: " + position.Seccode);
            }
        }
    }

    private async Task DeactivateLargePositionToolsAsync(IEnumerable<Tool> tools)
    {
        double maxShare = Portfolio.Saldo / 100 * Settings.MaxShareInitReqsTool;
        foreach (var position in Portfolio.Positions.ToArray().Where(p => (int)p.Saldo != 0))
        {
            var tool = tools.SingleOrDefault(x => x.Active && x.Security.Seccode == position.Seccode);
            if (tool == null) continue;

            var isLong = (int)position.Saldo > 0;
            var vol = (int)Math.Abs(position.Saldo);
            if (isLong && vol * tool.Security.InitReqLong > maxShare ||
                !isLong && vol * tool.Security.InitReqShort > maxShare)
            {
                AddInfo("Position is large. It is above MaxShareInitReqsTool: " + position.Seccode);
                bool initStopTrading = tool.StopTrading;
                tool.StopTrading = true;
                if (await CancelActiveOrdersAsync(position.Seccode))
                {
                    if (await ClosePositionByMarketAsync(position))
                    {
                        await CancelActiveOrdersAsync(position.Seccode);
                        if (tool.Active) await TradingSystem.ToolManager.ChangeActivityAsync(tool);
                        AddInfo("Position is closed. Tool is deactivated: " + tool.Name);
                    }
                    else
                    {
                        tool.StopTrading = initStopTrading;
                        AddInfo("Failed to close position. Tool is not deactivated.");
                    }
                }
                else
                {
                    tool.StopTrading = initStopTrading;
                    AddInfo("Failed to cancel orders before closing the large position. Tool is not deactivated.");
                }
            }
        }
    }

    private async Task DeactivateLessPriorityToolsAsync(IEnumerable<Tool> tools)
    {
        for (int i = Settings.ToolsByPriority.Count - 1; i >= 0; i--)
        {
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            await Task.Delay(5000);
            if (RequirementsAreNormal()) return;
            if (Connector.Connection != ConnectionState.Connected)
            {
                AddInfo("DeactivateLessPriorityTools: there is no connection.");
                return;
            }

            var tool = tools.SingleOrDefault(x => x.Active && x.Name == Settings.ToolsByPriority[i]);
            if (tool != null)
            {
                AddInfo("Disabling the least priority tool: " + tool.Name);
                bool initStopTrading = tool.StopTrading;
                tool.StopTrading = true;
                if (await CancelActiveOrdersAsync(tool.Security.Seccode))
                {
                    var position = Portfolio.Positions.ToArray()
                        .SingleOrDefault(x => x.Seccode == tool.Security.Seccode);
                    if (position != null && (int)position.Saldo != 0)
                    {
                        if (!await ClosePositionByMarketAsync(position))
                        {
                            tool.StopTrading = initStopTrading;
                            AddInfo("Failed to close the position of the least priority tool. The tool is active.");
                            continue;
                        }
                    }

                    await CancelActiveOrdersAsync(tool.Security.Seccode);
                    if (tool.Active) await TradingSystem.ToolManager.ChangeActivityAsync(tool);
                    AddInfo("The least priority tool is deactivated: " + tool.Name);
                }
                else
                {
                    tool.StopTrading = initStopTrading;
                    AddInfo("Failed to cancel orders of the least priority tool.");
                }
            }
        }
    }


    private async Task<bool> ClosePositionByMarketAsync(Position position)
    {
        var symbol = Connector.Securities.Single(x => x.Seccode == position.Seccode);
        if (await Connector.SendOrderAsync(symbol, OrderType.Market,
            (int)position.Saldo < 0, 100, (int)Math.Abs(position.Saldo), "ClosePositionByMarket"))
        {
            for (int i = 0; i < 10; i++)
            {
                if ((int)position.Saldo == 0) return true;
                await Task.Delay(500);
            }
            AddInfo("Order is sent, but position is not closed yet: " + symbol.Seccode);
        }
        return false;
    }

    private async Task<bool> CancelActiveOrdersAsync(string seccode)
    {
        var active = TradingSystem.Orders.ToArray().Where(x => x.Seccode == seccode && Connector.OrderIsActive(x));
        if (!active.Any()) return true;

        foreach (var order in active) await Connector.CancelOrderAsync(order);
        for (int i = 0; i < 10; i++)
        {
            if (!active.Where(Connector.OrderIsActive).Any()) return true;
            await Task.Delay(500);
        }
        return false;
    }
}
