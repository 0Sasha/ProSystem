using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProSystem.Services;

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

    private async Task CheckPortfolio(IEnumerable<Tool> tools, Settings settings)
    {
        bool CancelActiveOrders(string Seccode)
        {
            Order[] ActiveOrders = Orders.ToArray().Where(x => x.Seccode == Seccode && x.Status is "active" or "watching").ToArray();
            if (ActiveOrders.Length == 0) return true;
            else
            {
                foreach (Order MyOrder in ActiveOrders) CancelOrder(MyOrder);
                System.Threading.Thread.Sleep(500);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

                System.Threading.Thread.Sleep(1000);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

                System.Threading.Thread.Sleep(1500);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

                System.Threading.Thread.Sleep(2000);
                if (!ActiveOrders.Where(x => x.Status is "active" or "watching").Any()) return true;
                return false;
            }
        }
        bool ClosePositionByMarket(Security Symbol, Position MyPosition)
        {
            if (SendOrder(Symbol, OrderType.Market,
                (int)MyPosition.Saldo < 0, 100, (int)Math.Abs(MyPosition.Saldo), "ClosingPositionByMarket"))
            {
                System.Threading.Thread.Sleep(500);
                if ((int)MyPosition.Saldo != 0)
                {
                    System.Threading.Thread.Sleep(1000);
                    if ((int)MyPosition.Saldo != 0) System.Threading.Thread.Sleep(1500);
                    if ((int)MyPosition.Saldo != 0) System.Threading.Thread.Sleep(2000);
                    if ((int)MyPosition.Saldo != 0) System.Threading.Thread.Sleep(5000);
                }
                if ((int)MyPosition.Saldo == 0) return true;
                else AddInfo("CheckPortfolio: Заявка отправлена, но позиция всё ещё не закрыта: " + Symbol.Seccode);
            }
            return false;
        }

        int UpperBorder = Portfolio.AverageEquity + Portfolio.AverageEquity / 100 * settings.ToleranceEquity;
        int LowerBorder = Portfolio.AverageEquity - Portfolio.AverageEquity / 100 * settings.ToleranceEquity;
        if (Portfolio.Saldo < LowerBorder || Portfolio.Saldo > UpperBorder)
        {
            AddInfo("CheckPortfolio: Стоимость портфеля за пределами допустимого отклонения.", notify: true);
            return;
        }
        try
        {
            Tool[] MyTools = tools.ToArray();
            foreach (Position MyPosition in Portfolio.Positions.ToArray())
            {
                if ((int)MyPosition.Saldo != 0 &&
                    MyTools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == MyPosition.Seccode) == null)
                    AddInfo("CheckPortfolio: обнаружен независимый актив: " + MyPosition.Seccode, notify: true);
            }

            double MaxMinReqs = Portfolio.Saldo / 100 * settings.MaxShareMinReqsPortfolio;
            double MaxInitReqs = Portfolio.Saldo / 100 * settings.MaxShareInitReqsPortfolio;
            if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs)
            {
                UpdateShares(MyTools);
                CheckShares(settings);
                return;
            }

            // Запрос информации
            GetPortfolio(Clients[0].Union);
            System.Threading.Thread.Sleep(5000);

            // Повторная проверка объёма требований
            MaxMinReqs = Portfolio.Saldo / 100 * settings.MaxShareMinReqsPortfolio;
            MaxInitReqs = Portfolio.Saldo / 100 * settings.MaxShareInitReqsPortfolio;
            if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;

            // Балансировка портфеля
            AddInfo("CheckPortfolio: Требования портфеля превысили нормы: " +
                settings.MaxShareMinReqsPortfolio.ToString(IC) + "%/" +
                settings.MaxShareInitReqsPortfolio.ToString(IC) + "% MinReqs/InitReqs: " +
                Math.Round(Portfolio.MinReqs / Portfolio.Saldo * 100, 2).ToString(IC) + "%/" +
                Math.Round(Portfolio.InitReqs / Portfolio.Saldo * 100, 2).ToString(IC) + "%", notify: true);
            if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }

            // Поиск и закрытие неизвестных позиций, независящих от активных инструментов
            foreach (Position MyPosition in Portfolio.Positions.ToArray())
            {
                if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }
                if ((int)MyPosition.Saldo != 0 &&
                    MyTools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == MyPosition.Seccode) == null)
                {
                    // Отмена соответствующих заявок
                    if (!CancelActiveOrders(MyPosition.Seccode))
                    {
                        AddInfo("CheckPortfolio: Не удалось отменить заявки перед закрытием неизвестной позиции: " +
                            MyPosition.Seccode);
                        continue;
                    }

                    // Закрытие неизвестной позиции по рынку
                    Security Symbol = AllSecurities.Single(x => x.Seccode == MyPosition.Seccode && x.Market == MyPosition.Market);
                    if (ClosePositionByMarket(Symbol, MyPosition))
                    {
                        CancelActiveOrders(MyPosition.Seccode);
                        AddInfo("CheckPortfolio: Закрыта неизвестная позиция: " + MyPosition.Seccode);
                        System.Threading.Thread.Sleep(2000);

                        GetPortfolio(Clients[0].Union);
                        System.Threading.Thread.Sleep(3000);
                        if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;
                    }
                    else AddInfo("CheckPortfolio: Не удалось закрыть неизвестную позицию: " + MyPosition.Seccode);
                }
            }

            // Проверка объёмов открытых позиций активных инструментов
            double MaxShare = Portfolio.Saldo / 100 * settings.MaxShareInitReqsTool;
            foreach (Position MyPosition in Portfolio.Positions.ToArray())
            {
                if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }
                if ((int)MyPosition.Saldo != 0)
                {
                    Tool MyTool = MyTools.SingleOrDefault(x => x.Active && x.MySecurity.Seccode == MyPosition.Seccode);
                    if (MyTool == null) continue;

                    bool Long = (int)MyPosition.Saldo > 0;
                    int Vol = (int)Math.Abs(MyPosition.Saldo);
                    if (Long && Vol * MyTool.MySecurity.InitReqLong > MaxShare ||
                        !Long && Vol * MyTool.MySecurity.InitReqShort > MaxShare)
                    {
                        AddInfo("CheckPortfolio: Позиция превышает MaxShareInitReqsTool: " + MyPosition.Seccode);

                        // Отмена соответствующих заявок
                        if (!CancelActiveOrders(MyPosition.Seccode))
                        {
                            AddInfo("CheckPortfolio: Не удалось отменить заявки перед закрытием позиции.");
                            continue;
                        }

                        // Приостановка торговли, закрытие позиции по рынку и отключение инструмента
                        bool SourceStopTrading = MyTool.StopTrading;
                        MyTool.StopTrading = true;
                        if (ClosePositionByMarket(MyTool.MySecurity, MyPosition))
                        {
                            // Проверка отсутствия соответствующих заявок
                            if (!CancelActiveOrders(MyPosition.Seccode))
                            {
                                AddInfo("CheckPortfolio: Позиция закрыта, но не удалось отменить заявки. Инструмент не отключен.");
                                continue;
                            }

                            // Отключение инструмента
                            if (MyTool.Active) await ToolManager.ChangeActivityAsync(MyTool);
                            AddInfo("CheckPortfolio: Позиция закрыта, заявок нет. Инструмент отключен: " + MyTool.Name);
                            System.Threading.Thread.Sleep(2000);

                            // Проверка требований портфеля
                            GetPortfolio(Clients[0].Union);
                            System.Threading.Thread.Sleep(5000);
                            if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;
                        }
                        else
                        {
                            MyTool.StopTrading = SourceStopTrading;
                            AddInfo("CheckPortfolio: Не удалось закрыть позицию по рынку.");
                        }
                    }
                }
            }

            // Отключение наименее приоритетных активных инструментов
            for (int i = settings.ToolsByPriority.Count - 1; i > 0; i--)
            {
                GetPortfolio(Clients[0].Union);
                System.Threading.Thread.Sleep(5000);
                MaxMinReqs = Portfolio.Saldo / 100 * settings.MaxShareMinReqsPortfolio;
                MaxInitReqs = Portfolio.Saldo / 100 * settings.MaxShareInitReqsPortfolio;
                if (Portfolio.MinReqs < MaxMinReqs && Portfolio.InitReqs < MaxInitReqs) return;
                if (Connection != ConnectionState.Connected) { AddInfo("CheckPortfolio: соединение отсутствует."); return; }

                Tool MyTool = MyTools.SingleOrDefault(x => x.Active && x.Name == settings.ToolsByPriority[i]);
                if (MyTool != null)
                {
                    AddInfo("CheckPortfolio: Отключение наименее приоритетного инструмента: " + MyTool.Name);

                    // Отмена соответствующих заявок
                    if (!CancelActiveOrders(MyTool.MySecurity.Seccode))
                    {
                        AddInfo("CheckPortfolio: Не удалось отменить заявки наименее приоритетного активного инструмента.");
                        continue;
                    }

                    // Закрытие позиции по рынку, если она существует
                    bool SourceStopTrading = MyTool.StopTrading;
                    MyTool.StopTrading = true;
                    Position MyPosition = Portfolio.Positions.ToArray().SingleOrDefault(x => x.Seccode == MyTool.MySecurity.Seccode);
                    if (MyPosition != null && (int)MyPosition.Saldo != 0)
                    {
                        if (ClosePositionByMarket(MyTool.MySecurity, MyPosition))
                        {
                            if (!CancelActiveOrders(MyPosition.Seccode))
                            {
                                AddInfo("CheckPortfolio: Позиция закрыта, но не удалось отменить заявки. Инструмент активен.");
                                continue;
                            }
                        }
                        else
                        {
                            AddInfo("CheckPortfolio: Не удалось закрыть позицию наименее приоритетного активного инструмента. Инструмент активен.");
                            MyTool.StopTrading = SourceStopTrading;
                            continue;
                        }
                    }

                    // Отключение инструмента
                    if (MyTool.Active) await ToolManager.ChangeActivityAsync(MyTool);
                    AddInfo("CheckPortfolio: Позиция закрыта, заявок нет. Наименее приоритетный инструмент отключен: " + MyTool.Name);
                }
            }
        }
        catch (Exception e)
        {
            AddInfo("CheckPortfolio исключение: " + e.Message);
            AddInfo("Трассировка стека: " + e.StackTrace);
            if (e.InnerException != null)
            {
                AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
            }
        }
    }
}
