using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using System.Windows;
using System.Windows.Controls;

namespace ProSystem.Services;

internal class ScriptManager(Window window, TradingSystem tradingSystem, AddInformation addInfo) : IScriptManager
{
    private readonly Window Window = window;
    private readonly AddInformation AddInfo = addInfo;
    private readonly TradingSystem TradingSystem = tradingSystem;
    private readonly Connector Connector = tradingSystem.Connector;

    private DateTime ServerTime { get => Connector.ServerTime; }

    public void IdentifyOrdersAndTrades(Tool tool)
    {
        foreach (var unknownOrder in
            TradingSystem.Orders.ToArray().Where(x => x.Sender == null && x.Seccode == tool.Security.Seccode))
        {
            foreach (var script in tool.Scripts)
            {
                int i = Array.FindIndex(script.Orders.ToArray(),
                    x => x.TrID != 0 && x.TrID == unknownOrder.TrID ||
                    x.Id != 0 && x.Id == unknownOrder.Id);
                if (i > -1)
                {
                    if (unknownOrder.Status == script.Orders[i].Status)
                        unknownOrder.ChangeTime = script.Orders[i].ChangeTime;
                    unknownOrder.Sender = script.Orders[i].Sender;
                    unknownOrder.Signal = script.Orders[i].Signal;
                    unknownOrder.Note = script.Orders[i].Note;
                    Window.Dispatcher.Invoke(() => script.Orders[i] = unknownOrder);
                    break;
                }
            }
        }

        foreach (var unknownTrade in
            TradingSystem.Trades.ToArray().Where(x => x.OrderSender == null && x.Seccode == tool.Security.Seccode))
        {
            foreach (var script in tool.Scripts)
            {
                var order = script.Orders.ToArray().SingleOrDefault(o => o.Id == unknownTrade.OrderId);
                if (order != null)
                {
                    unknownTrade.OrderSender = order.Sender;
                    unknownTrade.OrderSignal = order.Signal;
                    unknownTrade.OrderNote = order.Note;
                    Window.Dispatcher.Invoke(() => script.Trades.Add(unknownTrade));
                    break;
                }
            }
        }
    }

    public void BringOrdersInLine(Tool tool)
    {
        foreach (var script in tool.Scripts)
        {
            for (int i = 0; i < script.Orders.Count; i++)
            {
                if (script.Orders[i].ChangeTime.Date >= ServerTime.Date.AddDays(-3) ||
                    Connector.OrderIsActive(script.Orders[i]))
                {
                    var orders = TradingSystem.Orders.ToArray();
                    var y = script.Orders[i].Id == 0 ?
                        Array.FindIndex(orders, x => x.TrID == script.Orders[i].TrID) :
                        Array.FindIndex(orders, x => x.Id == script.Orders[i].Id);

                    if (y > -1)
                    {
                        if (orders[y].Status == script.Orders[i].Status)
                            orders[y].ChangeTime = script.Orders[i].ChangeTime;
                        orders[y].Sender = script.Orders[i].Sender;
                        orders[y].Signal = script.Orders[i].Signal;
                        orders[y].Note = script.Orders[i].Note;
                        Window.Dispatcher.Invoke(() => script.Orders[i] = orders[y]);
                    }
                    else if (Connector.OrderIsActive(script.Orders[i]) &&
                        ServerTime.Date.DayOfWeek != DayOfWeek.Saturday && ServerTime.Date.DayOfWeek != DayOfWeek.Sunday)
                    {
                        script.Orders[i].Status = "lost";
                        script.Orders[i].ChangeTime = ServerTime.AddDays(-2);
                        AddInfo("BringOrdersInLine: " +
                            script.Name + ": Активная заявка не актуальна. Статус обновлён.");
                    }
                }
            }
        }
    }

    public void ClearObsoleteData(Tool tool)
    {
        foreach (var script in tool.Scripts)
        {
            var oldOrders = script.Orders.ToArray().Where(x => x.ChangeTime.Date < ServerTime.Date.AddDays(-180));
            var oldTrades = script.Trades.ToArray().Where(x => x.Time.Date < ServerTime.Date.AddDays(-180));
            Window.Dispatcher.Invoke(() =>
            {
                foreach (var order in oldOrders) script.Orders.Remove(order);
                foreach (var trade in oldTrades) script.Trades.Remove(trade);
            });
        }
    }

    public void UpdateView(Tool tool, Script script)
    {
        ArgumentNullException.ThrowIfNull(tool.Tab);
        ArgumentNullException.ThrowIfNull(tool.MainModel);
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);
        ArgumentNullException.ThrowIfNull(script.Result);

        string selectedScript = "AllScripts";
        var mainModel = tool.MainModel;
        Window.Dispatcher.Invoke(() =>
        {
            var grid = (Grid)((Grid)((Grid)tool.Tab.Content).Children[1]).Children[0];
            selectedScript = grid.Children.OfType<ComboBox>().Last().Text;

            script.InfoBlock.Text = "IsGrow[i] " + script.Result.IsGrow[^1] +
            "     IsGrow[i-1] " + script.Result.IsGrow[^2] + "\nType " + script.Result.Type;
        });

        foreach (var ann in mainModel.Annotations.ToArray())
            if (ann.ToolTip == script.Name || ann.ToolTip is "System" or null) mainModel.Annotations.Remove(ann);

        if (script.Result.Type != ScriptType.OSC)
            foreach (Series MySeries in mainModel.Series.ToArray())
                if (MySeries.Title == script.Name) mainModel.Series.Remove(MySeries);

        if (selectedScript == script.Name || selectedScript == "AllScripts")
        {
            if (script.Result.Type == ScriptType.OSC) Plot.UpdateMiniModel(tool, Window, script);
            else foreach (double[] Indicator in script.Result.Indicators)
                    mainModel.Series.Add(MakeLineSeries(Indicator, script.Name));

            if (!tool.ShowBasicSecurity)
            {
                var trades = script.Trades.ToArray()
                    .Concat(TradingSystem.SystemTrades.ToArray()
                    .Where(x => x.Seccode == tool.Security.Seccode)).ToArray();
                var orders =
                    TradingSystem.Orders.ToArray()
                    .Where(x => x.Seccode == tool.Security.Seccode && Connector.OrderIsActive(x)).ToArray();
                if (trades.Length > 0 || orders.Length > 0)
                    AddAnnotations(tool.MainModel, tool.Security.Bars, trades, orders);
            }
        }
    }

    public async Task<bool> UpdateStateAsync(Tool tool)
    {
        var result = true;
        foreach (var script in tool.Scripts)
        {
            script.LastExecuted = script.Orders.ToArray().LastOrDefault(x => Connector.OrderIsExecuted(x) && x.Note != "NM");

            var activeOrders = script.Orders.ToArray()
                .Where(x => x.Sender == script.Name && Connector.OrderIsActive(x));
            if (activeOrders.Count() > 1)
            {
                script.ActiveOrder = null;
                AddInfo(script.Name + ": Отмена активных заявок скрипта: " + activeOrders.Count());
                foreach (var order in activeOrders) await Connector.CancelOrderAsync(order);

                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(250);
                    if (!activeOrders.Where(Connector.OrderIsActive).Any()) continue;
                    if (i == 11)
                    {
                        AddInfo(script.Name + ": Не удалось вовремя отменить активные заявки.");
                        result = false;
                    }
                }
            }
            else script.ActiveOrder = activeOrders.SingleOrDefault();
        }
        return result;
    }

    public async Task ProcessOrdersAsync(Tool tool, ToolState toolState)
    {
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);
        if (!await UpdateStateAsync(tool)) return;

        var security = tool.Security;
        var basicSecurity = tool.BasicSecurity ?? security;
        foreach (var script in tool.Scripts)
        {
            ArgumentNullException.ThrowIfNull(script.Result);
            script.Calculate(basicSecurity);

            UpdateView(tool, script);
            if (toolState.IsLogging) WriteLog(script, tool.Name);

            if (basicSecurity != security && !AlignData(tool, script)) continue;
            if (!toolState.ReadyToTrade ||
                script.Result.Type != ScriptType.StopLine && !toolState.IsBidding) continue;
            if (script.Result.IsGrow.Length != security.Bars.Close.Length)
            {
                AddInfo(script.Name + "IsGrow.Length != Close.Length", true, true);
                continue;
            }

            var volume = script.CurrentPosition == PositionType.Long ?
                toolState.ShortOrderVolume : toolState.LongOrderVolume;

            if (script.Result.Type is ScriptType.OSC or ScriptType.Line)
                await ProcessOSCAsync(tool, script, volume, toolState.IsNormalPrice);
            else if (script.Result.Type is ScriptType.LimitLine)
                await ProcessLimitLineAsync(tool, script, volume);
            else if (script.Result.Type is ScriptType.StopLine)
                await ProcessStopLineAsync(tool, script, volume,
                    toolState.IsNormalPrice, toolState.IsBidding, toolState.ATR);
            else AddInfo(script.Name + ": Неизвестный тип скрипта: " + script.Result.Type, notify: true);
        }
    }


    private async Task ProcessOSCAsync(Tool tool, Script script, double volume, bool normalPrice)
    {
        ArgumentNullException.ThrowIfNull(script.Result);
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);
        ArgumentOutOfRangeException.ThrowIfLessThan(volume, 0.000001);

        var basicSecurity = tool.BasicSecurity;
        var security = tool.Security;
        var prevClose = basicSecurity?.Bars?.DateTime[^1] > security.Bars.DateTime[^1] ?
            security.Bars.Close[^1] : security.Bars.Close[^2];

        var isGrow = script.Result.IsGrow;
        var lastExecuted = script.LastExecuted;

        if (script.ActiveOrder != null)
        {
            var activeOrder = script.ActiveOrder;
            if (script.Result.OnlyLimit)
            {
                if (activeOrder.Quantity - activeOrder.Balance > 0.000001 || activeOrder.Note == "PartEx")
                {
                    if (Math.Abs(activeOrder.Price - prevClose) > 0.000001)
                        await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit,
                            prevClose, activeOrder.Balance, activeOrder.Signal ?? "", script, "PartEx");
                }
                else if ((activeOrder.Side == "B") != isGrow[^1] ||
                    (script.CurrentPosition == PositionType.Short) != isGrow[^1] || volume < 0.000001)
                {
                    await Connector.CancelOrderAsync(activeOrder);
                }
                else if (Math.Abs(activeOrder.Price - prevClose) > 0.000001 ||
                    Math.Abs(activeOrder.Balance - volume) - tool.Security.LotSize > 0.000001)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit,
                        prevClose, volume, activeOrder.Signal ?? "", script, activeOrder.Note);
                }
            }
            else if (ServerTime >= activeOrder.Time.AddMinutes(5))
            {
                if (normalPrice)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Market, prevClose,
                        activeOrder.Balance, activeOrder.Signal + "AtMarket", script, activeOrder.Note);
                }
                else AddInfo(script.Name + ": price is out of normal range", notify: true);
            }
            else if ((activeOrder.Side == "B") != isGrow[^1] || volume < 0.000001)
            {
                await Connector.CancelOrderAsync(activeOrder);
            }
            return;
        }

        if (lastExecuted?.ChangeTime >= script.Result.IndLastDT) return;
        if (security.Bars.DateTime[^1].Date != security.Bars.DateTime[^2].Date) return;
        if (basicSecurity != null)
        {
            if (basicSecurity.Bars?.DateTime[^1].Date > security.Bars.DateTime[^1].Date) return;
            if (basicSecurity.Bars?.DateTime[^1].Date != basicSecurity.Bars?.DateTime[^2].Date) return;
        }

        if (script.CurrentPosition == PositionType.Short && isGrow[^1] ||
            script.CurrentPosition == PositionType.Long && !isGrow[^1])
        {
            var type = script.Result.OnlyLimit ? OrderType.Limit : OrderType.Market;
            await Connector.SendOrderAsync(security, type, isGrow[^1],
                prevClose, volume, (isGrow[^1] ? "BuyAt" : "SellAt") + type, script);
            WriteLog(script, tool.Name);
        }
    }

    private async Task ProcessLimitLineAsync(Tool tool, Script script, double volume)
    {
        ArgumentNullException.ThrowIfNull(script.Result);
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);

        var security = tool.Security;
        var basicSecurity = tool.BasicSecurity;
        var orderPrice = basicSecurity?.Bars?.DateTime[^1] > security.Bars.DateTime[^1] ?
            script.Result.Indicators[0][^1] : script.Result.Indicators[0][^2];

        var isGrow = script.Result.IsGrow;
        var activeOrder = script.ActiveOrder;
        var lastExecuted = script.LastExecuted;

        if (activeOrder != null)
        {
            if (activeOrder.Quantity - activeOrder.Balance > 0.000001)
            {
                if (Math.Abs(activeOrder.Price - orderPrice) > 0.000001)
                {
                    var vol = activeOrder.Quantity - activeOrder.Balance;
                    await Connector.ReplaceOrderAsync(activeOrder, security,
                        OrderType.Market, orderPrice, vol, "CancelPartEx", script, "NM");
                }
            }
            else if (volume < 0.000001) await Connector.CancelOrderAsync(activeOrder);
            else if (Math.Abs(activeOrder.Price - orderPrice) > 0.000001 ||
                Math.Abs(activeOrder.Balance - volume) - tool.Security.LotSize > 0.000001)
            {
                if (orderPrice - security.MinPrice > -0.000001 && orderPrice - security.MaxPrice < 0.000001)
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit,
                        orderPrice, volume, activeOrder.Signal ?? "", script, activeOrder.Note);
                else await Connector.CancelOrderAsync(activeOrder);
            }
            return;
        }

        if (lastExecuted != null && lastExecuted.ChangeTime >= script.Result.IndLastDT || volume < 0.000001 ||
            basicSecurity == null && security.Bars.DateTime[^1].Date != security.Bars.DateTime[^2].Date ||
            basicSecurity != null && basicSecurity.Bars?.DateTime[^1].Date != basicSecurity.Bars?.DateTime[^2].Date)
            return;

        if ((script.CurrentPosition == PositionType.Short && isGrow[^1] ||
            script.CurrentPosition == PositionType.Long && !isGrow[^1]) &&
            orderPrice - security.MinPrice > -0.000001 && orderPrice - security.MaxPrice < 0.000001)
        {
            await Connector.SendOrderAsync(security, OrderType.Limit,
                isGrow[^1], orderPrice, volume, isGrow[^1] ? "BuyAtLimit" : "SellAtLimit", script);
            WriteLog(script, tool.Name);
        }
    }

    private async Task ProcessStopLineAsync(Tool tool, Script script,
        double volume, bool normalPrice, bool nowBidding, double atr)
    {
        ArgumentNullException.ThrowIfNull(script.Result);
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);

        var security = tool.Security;
        var basicSecurity = tool.BasicSecurity;
        var prevClose = security.Bars.Close[^2];
        var prevStopLine = script.Result.Indicators[0][^2];
        if (basicSecurity?.Bars?.DateTime[^1] > security.Bars.DateTime[^1])
        {
            prevClose = security.Bars.Close[^1];
            prevStopLine = script.Result.Indicators[0][^1];
        }

        var isGrow = script.Result.IsGrow;
        if (script.ActiveOrder != null)
        {
            var activeOrder = script.ActiveOrder;
            if (activeOrder.InitType == OrderType.Conditional)
            {
                double price = activeOrder.Side == "B" ? prevStopLine + security.TickSize : prevStopLine - security.TickSize;
                if (Connector.OrderIsTriggered(activeOrder))
                {
                    if (ServerTime > activeOrder.Time.AddSeconds(60) && nowBidding)
                    {
                        if (normalPrice)
                        {
                            await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Market,
                                prevClose, activeOrder.Balance, activeOrder.Signal + "AtMarket", script);
                        }
                        else AddInfo(script.Name + ": price is out of normal range", notify: true);
                    }
                }
                else if (volume < 0.000001 || Connector.FirstBar && activeOrder.Note == null ||
                    (activeOrder.Side == "B") == isGrow[^1]) await Connector.CancelOrderAsync(activeOrder);
                else if (Math.Abs(activeOrder.Price - price) > 0.000001 || activeOrder.Balance - volume - tool.Security.LotSize > 0.000001)
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Conditional, price, volume, activeOrder.Signal ?? "", script);
            }
            else if (nowBidding)
            {
                if (activeOrder.Note == "NB")
                {
                    if (!Connector.FirstBar)
                    {
                        if (normalPrice)
                        {
                            await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit, prevClose,
                                activeOrder.Balance, activeOrder.Signal + "AtClose", script, "CloseNB");
                        }
                        else AddInfo(script.Name + ": price is out of normal range", notify: true);
                    }
                }
                else if (ServerTime >= activeOrder.Time.AddSeconds(60))
                {
                    if (normalPrice)
                    {
                        await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Market, prevClose,
                            activeOrder.Balance, activeOrder.Signal + "AtMarket", script, activeOrder.Note);
                    }
                    else AddInfo(script.Name + ": price is out of normal range", notify: true);
                }
            }
            return;
        }

        var lastExecuted = script.LastExecuted;
        if (!nowBidding || volume < 0.000001) return;
        if (lastExecuted?.ChangeTime >= script.Result.IndLastDT)
        {
            if (lastExecuted.Note == null || lastExecuted.Note == "NB") return;

            for (int bar = script.Result.Indicators[0].Length - 1; bar > 1; bar--)
            {
                if (lastExecuted.Side == "B" && !isGrow[bar - 1] || lastExecuted.Side == "S" && isGrow[bar - 1])
                {
                    if (Math.Abs(script.Result.Indicators[0][bar - 1] - script.Result.Indicators[0][^2]) < 0.000001)
                        return;
                    else break;
                }
            }
        }

        var curPosition = script.CurrentPosition;
        if (Connector.FirstBar)
        {
            if (curPosition == PositionType.Short && isGrow[^1] || curPosition == PositionType.Long && !isGrow[^1])
            {
                double Price = isGrow[^1] ? prevStopLine - atr : prevStopLine + atr;
                await Connector.SendOrderAsync(security, OrderType.Limit,
                    isGrow[^1], Price, volume, isGrow[^1] ? "BuyAtLimitNB" : "SellAtLimitNB", script, "NB");
                WriteLog(script, tool.Name);
            }
        }
        else if (curPosition == PositionType.Short && !isGrow[^1] || curPosition == PositionType.Long && isGrow[^1])
        {
            double Price = isGrow[^1] ? prevStopLine - security.TickSize : prevStopLine + security.TickSize;
            await Connector.SendOrderAsync(security, OrderType.Conditional,
                !isGrow[^1], Price, volume, !isGrow[^1] ? "BuyAtStop" : "SellAtStop", script);
            WriteLog(script, tool.Name);
        }
        else
        {
            AddInfo(script.Name + ": Текущая позиция скрипта не соответствует IsGrow.", notify: true);
            if (Connector.FirstBar)
            {
                double Price = isGrow[^1] ? prevStopLine - atr : prevStopLine + atr;
                await Connector.SendOrderAsync(security, OrderType.Limit,
                    isGrow[^1], Price, volume, isGrow[^1] ? "BuyAtLimitNB" : "SellAtLimitNB", script, "NB");
            }
            else
            {
                double Price = isGrow[^1] ? prevStopLine - security.TickSize : prevStopLine + security.TickSize;
                await Connector.SendOrderAsync(security, OrderType.Limit, isGrow[^1], Price, volume, "UnknowEvent", script);
            }
            WriteLog(script, tool.Name);
        }
    }


    private bool AlignData(Tool tool, Script script)
    {
        ArgumentNullException.ThrowIfNull(tool.BasicSecurity?.SourceBars);
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);
        ArgumentNullException.ThrowIfNull(tool.Security.SourceBars);
        ArgumentNullException.ThrowIfNull(script.Result);

        var security = tool.Security;
        var initLength = tool.Security.Bars.Close.Length;
        var x = script.Result.IsGrow.Length - initLength;
        if (x > 0)
        {
            script.Result.IsGrow = script.Result.IsGrow[x..];
            for (int i = 0; i < script.Result.Indicators.Length; i++)
                if (script.Result.Indicators[i].Length > 0)
                    script.Result.Indicators[i] = script.Result.Indicators[i][x..];
        }
        else if (x < 0)
        {
            if (script.Result.IsGrow.Length > 500)
            {
                AddInfo(tool.Name + ": Количество торговых баров больше базисных: " +
                    initLength + "/" + script.Result.IsGrow.Length + " Обрезка.", notify: true);

                x = security.SourceBars.Close.Length - tool.BasicSecurity.SourceBars.Close.Length + 100;
                if (x > -1 && x < security.SourceBars.DateTime.Length)
                    security.SourceBars = security.SourceBars.Trim(x);
                else AddInfo(tool.Name + ": обрезка баров невозможна.");

                x = initLength - script.Result.IsGrow.Length;
                if (x > -1 && x < security.Bars.DateTime.Length)
                    security.Bars = security.Bars.Trim(x);
                else AddInfo(tool.Name + ": обрезка баров невозможна.");
            }
            else AddInfo(tool.Name + ": Количество торговых баров больше базисных: " +
                initLength + "/" + script.Result.IsGrow.Length, notify: true);
            return false;
        }
        return true;
    }

    private void WriteLog(Script script, string toolName)
    {
        ArgumentNullException.ThrowIfNull(script.Result);

        var data = ServerTime + ": /////////////////// Script: " + script.Name + "\nType " + script.Result.Type +
            "\nCurPosition " + script.CurrentPosition + "\nIsGrow[^1] " + script.Result.IsGrow[^1] +
            "\nIsGrow[^2] " + script.Result.IsGrow[^2] + "\nOnlyLimit " + script.Result.OnlyLimit +
            "\nCentre " + script.Result.Centre + "\nLevel " + script.Result.Level;

        for (int i = 0; i < script.Result.Indicators.Length; i++)
            if (script.Result.Indicators[i].Length > 2)
                data += "\nIndicator" + (i + 1) + "[^2] " + script.Result.Indicators[i][^2];
        try
        {
            System.IO.File.AppendAllText("Logs/Tools/" + toolName + ".txt", data + "\n");
        }
        catch (Exception e) { AddInfo(toolName + ": Исключение логирования скрипта: " + e.Message); }
    }


    private void AddAnnotations(PlotModel model, Bars bars, Trade[] trades, Order[] orders)
    {
        int i;
        OxyColor color;
        double yStartPoint, yEndPoint;
        List<(Trade, int)> myTrades = [];
        List<(Trade, int)> tradesOnlyWithPoint = [];
        foreach (var trade in trades)
        {
            i = Array.FindIndex(bars.DateTime, x => x.AddMinutes(bars.TF) > trade.Time);
            if (i < 0)
            {
                AddInfo("AddAnnotations: bar is not found.");
                continue;
            }

            var sameTrade = myTrades.SingleOrDefault(x => x.Item2 == i && x.Item1.Side == trade.Side);
            if (sameTrade.Item1 != null)
            {
                sameTrade.Item1.Quantity += trade.Quantity;
                if (sameTrade.Item1.Price != trade.Price) tradesOnlyWithPoint.Add((trade, i));
            }
            else myTrades.Add((trade.GetCopy(), i));
        }
        foreach ((Trade, int) myTrade in myTrades)
        {
            var trade = myTrade.Item1;
            i = myTrade.Item2;

            if (trade.Side == "B")
            {
                color = Theme.GreenBar;
                yStartPoint = bars.Low[i] - bars.Low[i] * 0.001;
                yEndPoint = bars.Low[i];
            }
            else
            {
                color = Theme.RedBar;
                yStartPoint = bars.High[i] + bars.High[i] * 0.001;
                yEndPoint = bars.High[i];
            }

            string text = trade.OrderSignal is "BuyAtLimit" or "SellAtLimit" or "BuyAtStop" or "SellAtStop" ?
                trade.Price + "; " + trade.Quantity : trade.OrderSignal + "; " + trade.Price + "; " + trade.Quantity;
            model.Annotations.Add(new ArrowAnnotation()
            {
                Color = color,
                Text = text,
                StartPoint = new DataPoint(i, yStartPoint),
                EndPoint = new DataPoint(i, yEndPoint),
                ToolTip = trade.OrderSender
            });
            model.Annotations.Add(new PointAnnotation()
            {
                X = i,
                Y = trade.Price,
                Fill = OxyColors.WhiteSmoke,
                Stroke = OxyColors.Black,
                StrokeThickness = 1,
                ToolTip = trade.OrderSender
            });
        }
        foreach ((Trade, int) MyTrade in tradesOnlyWithPoint)
        {
            model.Annotations.Add(new PointAnnotation()
            {
                X = MyTrade.Item2,
                Y = MyTrade.Item1.Price,
                Fill = OxyColors.WhiteSmoke,
                Stroke = OxyColors.Black,
                StrokeThickness = 1,
                ToolTip = MyTrade.Item1.OrderSender
            });
        }
        foreach (Order ActiveOrder in orders)
        {
            model.Annotations.Add(new LineAnnotation()
            {
                Type = LineAnnotationType.Horizontal,
                Y = ActiveOrder.Price,
                MinimumX = model.Axes[0].ActualMaximum - 20,
                Color = ActiveOrder.Side == "B" ? Theme.GreenBar : Theme.RedBar,
                ToolTip = ActiveOrder.Sender,
                Text = ActiveOrder.Signal,
                StrokeThickness = 2
            });
        }
    }

    private static LineSeries MakeLineSeries(double[] indicator, string name)
    {
        var points = new DataPoint[indicator.Length];
        for (int i = 0; i < indicator.Length; i++) points[i] = new DataPoint(i, indicator[i]);
        return new() { ItemsSource = points, Color = Theme.Indicator, Title = name };
    }
}
