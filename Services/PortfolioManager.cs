using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProSystem.Services;

internal class PortfolioManager : IPortfolioManager
{
    private MainWindow Window { get => TradingSystem.Window; }
    private Settings Settings { get => TradingSystem.Settings; }
    private UnitedPortfolio Portfolio { get => TradingSystem.Portfolio; }
    private Connector Connector { get => TradingSystem.Connector; }
    private IToolManager ToolManager { get => TradingSystem.ToolManager; }

    public TradingSystem TradingSystem { get; }

    public PortfolioManager(TradingSystem tradingSystem) =>
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));

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

    public void UpdateShares()
    {
        double sumPotInitReqs = 0;
        double sumInitReqsBaseAssets = 0;
        double sumReqsBaseAssets = 0;
        foreach (var tool in TradingSystem.Tools)
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

    public bool CheckEquity()
    {
        int range = Portfolio.AverageEquity / 100 * Settings.ToleranceEquity;
        if (Portfolio.Saldo < Portfolio.AverageEquity - range || Portfolio.Saldo > Portfolio.AverageEquity + range)
        {
            Window.AddInfo("Стоимость портфеля за пределами допустимого отклонения.");
            return false;
        }
        return true;
    }

    public bool CheckShares()
    {
        if (Portfolio.PotentialShareInitReqs > Settings.MaxShareInitReqsPortfolio)
        {
            Window.AddInfo("Portfolio: Потенциальные начальные требования портфеля превышают норму: " +
               Settings.MaxShareInitReqsPortfolio + "%. PotentialInitReqs: " +
               Portfolio.PotentialShareInitReqs + "%");
            return false;
        }

        if (Portfolio.ShareBaseAssets > Settings.OptShareBaseAssets + Settings.ToleranceBaseAssets ||
            Portfolio.ShareBaseAssets < Settings.OptShareBaseAssets - Settings.ToleranceBaseAssets)
        {
            Window.AddInfo("Portfolio: Доля базовых активов за пределами допустимого отклонения: " +
                Portfolio.ShareBaseAssets + "%");
            return false;
        }
        return true;
    }

    public async Task NormalizePortfolioAsync()
    {
        if (!CheckEquity()) return;

        if (CheckRequirements())
        {
            UpdateShares();
            CheckShares();
            NotifyIndependentPositions(TradingSystem.Tools.ToArray());
            return;
        }
        else
        {
            Window.AddInfo("Требования портфеля выше нормы: " + Settings.MaxShareMinReqsPortfolio + "%/" +
                Settings.MaxShareInitReqsPortfolio + "% MinReqs/InitReqs: " +
                Math.Round(Portfolio.MinReqs / Portfolio.Saldo * 100, 2) + "%/" +
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2) + "%", notify: true);

            if (Connector.Connection != ConnectionState.Connected)
            {
                Window.AddInfo("Соединение отсутствует.");
                return;
            }
        }

        await Connector.OrderPortfolioInfoAsync(Portfolio);
        Thread.Sleep(5000);
        if (CheckRequirements()) return;

        try
        {
            var tools = TradingSystem.Tools.ToArray();
            await CloseIndependentPositions(tools);
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            Thread.Sleep(5000);
            if (CheckRequirements()) return;

            await DeactivateLargePositionToolsAsync(tools);
            await Connector.OrderPortfolioInfoAsync(Portfolio);
            Thread.Sleep(5000);
            if (CheckRequirements()) return;

            await DeactivateLessPriorityToolsAsync(tools);
        }
        catch (Exception e)
        {
            Window.AddInfo("CheckPortfolio исключение: " + e.Message);
            Window.AddInfo("Трассировка стека: " + e.StackTrace);
            if (e.InnerException != null)
            {
                Window.AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                Window.AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
            }
        }
    }

    private bool CheckRequirements()
    {
        double maxMinReqs = Portfolio.Saldo / 100 * Settings.MaxShareMinReqsPortfolio;
        double maxInitReqs = Portfolio.Saldo / 100 * Settings.MaxShareInitReqsPortfolio;
        return Portfolio.MinReqs < maxMinReqs && Portfolio.InitReqs < maxInitReqs;
    }

    private void NotifyIndependentPositions(IEnumerable<Tool> tools)
    {
        foreach (var position in Portfolio.Positions.ToArray())
        {
            if ((int)position.Saldo != 0 &&
                tools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == position.Seccode) == null)
                Window.AddInfo("Независимый актив: " + position.Seccode, notify: true);
        }
    }

    private async Task CloseIndependentPositions(IEnumerable<Tool> tools)
    {
        foreach (var position in Portfolio.Positions.ToArray())
        {
            if ((int)position.Saldo != 0 &&
                tools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == position.Seccode) == null)
            {
                if (!await CancelActiveOrders(position.Seccode))
                {
                    Window.AddInfo("Не удалось отменить заявки перед закрытием позиции: " + position.Seccode);
                    continue;
                }

                if (await ClosePositionByMarket(position))
                {
                    await CancelActiveOrders(position.Seccode);
                    Window.AddInfo("Закрыта неизвестная позиция: " + position.Seccode);
                }
                else Window.AddInfo("Не удалось закрыть неизвестную позицию: " + position.Seccode);
            }
        }
    }

    private async Task DeactivateLargePositionToolsAsync(IEnumerable<Tool> tools)
    {
        double maxShare = Portfolio.Saldo / 100 * Settings.MaxShareInitReqsTool;
        foreach (var position in Portfolio.Positions.ToArray().Where(p => (int)p.Saldo != 0))
        {
            var tool = tools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == position.Seccode);
            if (tool == null) continue;

            var isLong = (int)position.Saldo > 0;
            var vol = (int)Math.Abs(position.Saldo);
            if (isLong && vol * tool.MySecurity.InitReqLong > maxShare ||
                !isLong && vol * tool.MySecurity.InitReqShort > maxShare)
            {
                Window.AddInfo("Позиция превышает MaxShareInitReqsTool: " + position.Seccode);
                if (!await CancelActiveOrders(position.Seccode))
                {
                    Window.AddInfo("Не удалось отменить заявки перед закрытием позиции.");
                    continue;
                }

                bool initStopTrading = tool.StopTrading;
                tool.StopTrading = true;
                if (await ClosePositionByMarket(position))
                {
                    if (!await CancelActiveOrders(position.Seccode))
                    {
                        Window.AddInfo("Позиция закрыта, но не удалось отменить заявки. Инструмент не отключен.");
                        continue;
                    }
                    if (tool.Active) await ToolManager.ChangeActivityAsync(tool);
                    Window.AddInfo("Позиция закрыта. Инструмент отключен: " + tool.Name);
                }
                else
                {
                    tool.StopTrading = initStopTrading;
                    Window.AddInfo("Не удалось закрыть позицию по рынку.");
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
            if (CheckRequirements()) return;
            if (Connector.Connection != ConnectionState.Connected)
            {
                Window.AddInfo("Cоединение отсутствует.");
                return;
            }

            var tool = tools.SingleOrDefault(x => x.Active && x.Name == Settings.ToolsByPriority[i]);
            if (tool != null)
            {
                Window.AddInfo("Отключение наименее приоритетного инструмента: " + tool.Name);
                if (!await CancelActiveOrders(tool.MySecurity.Seccode))
                {
                    Window.AddInfo("Не удалось отменить заявки наименее приоритетного активного инструмента.");
                    continue;
                }

                var initStopTrading = tool.StopTrading;
                tool.StopTrading = true;
                var position = Portfolio.Positions.ToArray()
                    .SingleOrDefault(x => x.Seccode == tool.MySecurity.Seccode);
                if (position != null && (int)position.Saldo != 0)
                {
                    if (await ClosePositionByMarket(position))
                    {
                        if (!await CancelActiveOrders(position.Seccode))
                        {
                            Window.AddInfo("Позиция закрыта, но не удалось отменить заявки. Инструмент активен.");
                            continue;
                        }
                    }
                    else
                    {
                        Window.AddInfo("Не удалось закрыть позицию инструмента. Инструмент активен.");
                        tool.StopTrading = initStopTrading;
                        continue;
                    }
                }

                if (tool.Active) await ToolManager.ChangeActivityAsync(tool);
                Window.AddInfo("Позиция закрыта. Наименее приоритетный инструмент отключен: " + tool.Name);
            }
        }
    }

    private async Task<bool> ClosePositionByMarket(Position position)
    {
        var symbol = Connector.Securities.Single(x => x.Seccode == position.Seccode && x.Market == position.Market);
        if (await Connector.SendOrderAsync(symbol, OrderType.Market,
            (int)position.Saldo < 0, 100, (int)Math.Abs(position.Saldo), "ClosingPositionByMarket"))
        {
            for (int i = 0; i < 10; i++)
            {
                if ((int)position.Saldo == 0) break;
                Thread.Sleep(500);
            }

            if ((int)position.Saldo == 0) return true;
            else Window.AddInfo("Заявка отправлена, но позиция всё ещё не закрыта: " + symbol.Seccode);
        }
        return false;
    }

    private async Task<bool> CancelActiveOrders(string seccode)
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
