using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProSystem.Services;

internal class PortfolioManager : IPortfolioManager
{
    private readonly AddInformation AddInfo;
    private readonly TradingSystem TradingSystem;

    private Settings Settings { get => TradingSystem.Settings; }
    private Connector Connector { get => TradingSystem.Connector; }
    private UnitedPortfolio Portfolio { get => TradingSystem.Portfolio; }

    public PortfolioManager(TradingSystem tradingSystem, AddInformation addInfo)
    {
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
    }


    public bool CheckEquity()
    {
        var range = Portfolio.AverageEquity / 100 * Settings.ToleranceEquity;
        if (Portfolio.Saldo < Portfolio.AverageEquity - range || Portfolio.Saldo > Portfolio.AverageEquity + range)
        {
            AddInfo("Стоимость портфеля за пределами допустимого отклонения.", notify: true);
            return false;
        }
        return true;
    }

    public void UpdateEquity()
    {
        Portfolio.Equity[DateTime.Today.AddDays(-1)] = (int)Portfolio.Saldo;
        Portfolio.Notify(nameof(Portfolio.Equity));
    }

    public void UpdatePositions()
    {
        Portfolio.Positions.RemoveAll(x => x.Saldo == 0);
        Portfolio.AllPositions =
            new(Portfolio.MoneyPositions.Concat(Portfolio.Positions.OrderBy(x => x.ShortName))) { Portfolio };
        Portfolio.Notify(nameof(Portfolio.Positions));
    }

    public async Task NormalizePortfolioAsync()
    {
        if (DateTime.Now > DateTime.Today.AddMinutes(840) && DateTime.Now < DateTime.Today.AddMinutes(845)) return;
        if (!CheckEquity()) return;

        if (RequirementsAreNormal())
        {
            CheckRequirements();
            CheckShares();
            CheckIndependentPositions();
            return;
        }
        else
        {
            AddInfo("Требования портфеля выше нормы: " + Settings.MaxShareMinReqsPortfolio + "%/" +
                Settings.MaxShareInitReqsPortfolio + "% MinReqs/InitReqs: " +
                Math.Round(Portfolio.MinReqs / Portfolio.Saldo * 100, 2) + "%/" +
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2) + "%", notify: true);

            if (Connector.Connection != ConnectionState.Connected)
            {
                AddInfo("Соединение отсутствует.");
                return;
            }
        }

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


    private bool RequirementsAreNormal()
    {
        double maxMinReqs = Portfolio.Saldo / 100 * Settings.MaxShareMinReqsPortfolio;
        double maxInitReqs = Portfolio.Saldo / 100 * Settings.MaxShareInitReqsPortfolio;
        return Portfolio.MinReqs < maxMinReqs && Portfolio.InitReqs < maxInitReqs;
    }

    private void CheckRequirements()
    {
        double warnMinReqs = Portfolio.Saldo / 100 * (Settings.MaxShareMinReqsPortfolio - 7);
        double warnInitReqs = Portfolio.Saldo / 100 * (Settings.MaxShareInitReqsPortfolio - 7);
        if (Portfolio.MinReqs > warnMinReqs || Portfolio.InitReqs > warnInitReqs)
        {
            AddInfo("Requirements are close to limits: " + Settings.MaxShareMinReqsPortfolio + "%/" +
                Settings.MaxShareInitReqsPortfolio + "% MinReqs/InitReqs: " +
                Math.Round(Portfolio.MinReqs / Portfolio.Saldo * 100, 2) + "%/" +
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2) + "%", notify: true);
        }
    }

    private bool CheckShares()
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
                        tool.NumberOfLots * Math.Max(tool.Security.InitReqLong, tool.Security.InitReqShort);

                if (tool.UseShiftBalance)
                {
                    sumReqsBaseAssets += tool.BaseBalance *
                        (tool.Security.LastTrade.Price / tool.Security.MinStep * tool.Security.MinStepCost);

                    var inReqsBaseAssets = tool.BaseBalance > 0 ?
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
        Portfolio.Notify("Shares");

        var result = true;
        if (Portfolio.PotentialShareInitReqs > Settings.MaxShareInitReqsPortfolio)
        {
            AddInfo("Portfolio: Потенциальные начальные требования портфеля превышают норму: " +
               Settings.MaxShareInitReqsPortfolio + "%. PotentialInitReqs: " +
               Portfolio.PotentialShareInitReqs + "%", notify: true);
            result = false;
        }
        if (Portfolio.ShareBaseAssets > Settings.OptShareBaseAssets + Settings.ToleranceBaseAssets ||
            Portfolio.ShareBaseAssets < Settings.OptShareBaseAssets - Settings.ToleranceBaseAssets)
        {
            AddInfo("Portfolio: Доля базовых активов за пределами допустимого отклонения: " +
                Portfolio.ShareBaseAssets + "%", notify: true);
            result = false;
        }
        return result;
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


    private async Task CloseIndependentPositionsAsync(IEnumerable<Tool> tools)
    {
        foreach (var position in Portfolio.Positions.ToArray())
        {
            if ((int)position.Saldo != 0 &&
                tools.SingleOrDefault(x => x.Active && x.Security.Seccode == position.Seccode) == null)
            {
                if (!await CancelActiveOrdersAsync(position.Seccode))
                {
                    AddInfo("Не удалось отменить заявки перед закрытием позиции: " + position.Seccode);
                    continue;
                }

                if (await ClosePositionByMarketAsync(position))
                {
                    await CancelActiveOrdersAsync(position.Seccode);
                    AddInfo("Закрыта неизвестная позиция: " + position.Seccode);
                }
                else AddInfo("Не удалось закрыть неизвестную позицию: " + position.Seccode);
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
                AddInfo("Позиция превышает MaxShareInitReqsTool: " + position.Seccode);
                if (!await CancelActiveOrdersAsync(position.Seccode))
                {
                    AddInfo("Не удалось отменить заявки перед закрытием позиции.");
                    continue;
                }

                bool initStopTrading = tool.StopTrading;
                tool.StopTrading = true;
                if (await ClosePositionByMarketAsync(position))
                {
                    if (!await CancelActiveOrdersAsync(position.Seccode))
                    {
                        AddInfo("Позиция закрыта, но не удалось отменить заявки. Инструмент не отключен.");
                        continue;
                    }
                    if (tool.Active) await TradingSystem.ToolManager.ChangeActivityAsync(tool);
                    AddInfo("Позиция закрыта. Инструмент отключен: " + tool.Name);
                }
                else
                {
                    tool.StopTrading = initStopTrading;
                    AddInfo("Не удалось закрыть позицию по рынку.");
                }
            }
        }
    }

    private async Task DeactivateLessPriorityToolsAsync(IEnumerable<Tool> tools)
    {
        for (int i = Settings.ToolsByPriority.Count - 1; i > 0; i--)
        {
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            Thread.Sleep(5000);
            if (RequirementsAreNormal()) return;
            if (Connector.Connection != ConnectionState.Connected)
            {
                AddInfo("Cоединение отсутствует.");
                return;
            }

            var tool = tools.SingleOrDefault(x => x.Active && x.Name == Settings.ToolsByPriority[i]);
            if (tool != null)
            {
                AddInfo("Отключение наименее приоритетного инструмента: " + tool.Name);
                if (!await CancelActiveOrdersAsync(tool.Security.Seccode))
                {
                    AddInfo("Не удалось отменить заявки наименее приоритетного активного инструмента.");
                    continue;
                }

                var initStopTrading = tool.StopTrading;
                tool.StopTrading = true;
                var position = Portfolio.Positions.ToArray()
                    .SingleOrDefault(x => x.Seccode == tool.Security.Seccode);
                if (position != null && (int)position.Saldo != 0)
                {
                    if (await ClosePositionByMarketAsync(position))
                    {
                        if (!await CancelActiveOrdersAsync(position.Seccode))
                        {
                            AddInfo("Позиция закрыта, но не удалось отменить заявки. Инструмент активен.");
                            continue;
                        }
                    }
                    else
                    {
                        AddInfo("Не удалось закрыть позицию инструмента. Инструмент активен.");
                        tool.StopTrading = initStopTrading;
                        continue;
                    }
                }

                if (tool.Active) await TradingSystem.ToolManager.ChangeActivityAsync(tool);
                AddInfo("Наименее приоритетный инструмент отключен: " + tool.Name);
            }
        }
    }


    private async Task<bool> ClosePositionByMarketAsync(Position position)
    {
        var symbol = Connector.Securities.Single(x => x.Seccode == position.Seccode && x.Market == position.Market);
        if (await Connector.SendOrderAsync(symbol, OrderType.Market,
            (int)position.Saldo < 0, 100, (int)Math.Abs(position.Saldo), "ClosingPositionByMarket"))
        {
            for (int i = 0; i < 10; i++)
            {
                Thread.Sleep(500);
                if ((int)position.Saldo == 0) return true;
            }
            AddInfo("Заявка отправлена, но позиция всё ещё не закрыта: " + symbol.Seccode);
        }
        return false;
    }

    private async Task<bool> CancelActiveOrdersAsync(string seccode)
    {
        var active = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == seccode && x.Status is "active" or "watching");
        if (!active.Any()) return true;

        foreach (var order in active) await Connector.CancelOrderAsync(order);
        for (int i = 0; i < 10; i++)
        {
            if (!active.Where(x => x.Status is "active" or "watching").Any()) return true;
            Thread.Sleep(500);
        }
        return false;
    }
}
