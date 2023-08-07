using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using HarfBuzzSharp;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Series;
using OxyPlot.SkiaSharp.Wpf;
using static ProSystem.Controls;

namespace ProSystem.Services;

internal class ScriptManager : IScriptManager
{
    private readonly Window Window;
    private readonly AddInformation AddInfo;
    private readonly TradingSystem TradingSystem;

    private Connector Connector { get => TradingSystem.Connector; }

    public ScriptManager(Window window, TradingSystem tradingSystem, AddInformation addInfo)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
    }

    public void InitializeScripts(Tool tool, TabItem tabTool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));
        if (tabTool == null) throw new ArgumentNullException(nameof(tabTool));

        var scripts = tool.Scripts;
        for (int i = 0; i < scripts.Length; i++)
        {
            var props = scripts[i].GetScriptProperties();
            Window.Dispatcher.Invoke(() =>
            {
                if (props.IsOSC)
                {
                    var plot = ((tabTool.Content as Grid).Children[0] as Grid).Children[0] as PlotView;
                    if (plot.Visibility == System.Windows.Visibility.Hidden)
                    {
                        Grid.SetRow(((tabTool.Content as Grid).Children[0] as Grid).Children[1] as PlotView, 1);
                        plot.Visibility = System.Windows.Visibility.Visible;
                    }
                }
                var collection = (((tabTool.Content as Grid).Children[1] as Grid).Children[i + 1] as Grid).Children;

                collection.Clear();
                collection.Add(GetTextBlock(scripts[i].Name, 5, 0));
                AddUpperControls(scripts[i], collection, props);
                if (props.MiddleProperties != null) AddMiddleControls(scripts[i], collection, props);

                var textBlock = GetTextBlock("Block Info", 5, 170);
                scripts[i].BlockInfo = textBlock;
                collection.Add(textBlock);
            });
        }
    }

    public void IdentifyOrdersAndTrades(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));

        foreach (var unknownOrder in
            TradingSystem.Orders.ToArray().Where(x => x.Sender == null && x.Seccode == tool.MySecurity.Seccode))
        {
            foreach (var script in tool.Scripts)
            {
                int i = Array.FindIndex(script.Orders.ToArray(), x => x.TrID == unknownOrder.TrID);
                if (i > -1)
                {
                    if (unknownOrder.Status == script.Orders[i].Status)
                        unknownOrder.DateTime = script.Orders[i].DateTime;
                    unknownOrder.Sender = script.Orders[i].Sender;
                    unknownOrder.Signal = script.Orders[i].Signal;
                    unknownOrder.Note = script.Orders[i].Note;
                    Window.Dispatcher.Invoke(() => script.Orders[i] = unknownOrder);
                    break;
                }
            }
        }

        foreach (var unknownTrade in
            TradingSystem.Trades.ToArray().Where(x => x.SenderOrder == null && x.Seccode == tool.MySecurity.Seccode))
        {
            foreach (var script in tool.Scripts)
            {
                int i = Array.FindIndex(script.Orders.ToArray(), x => x.OrderNo == unknownTrade.OrderNo);
                if (i > -1)
                {
                    unknownTrade.SenderOrder = script.Orders[i].Sender;
                    unknownTrade.SignalOrder = script.Orders[i].Signal;
                    unknownTrade.NoteOrder = script.Orders[i].Note;
                    Window.Dispatcher.Invoke(() => script.Trades.Add(unknownTrade));
                    break;
                }
            }
        }
    }

    public void BringOrdersInLine(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));

        foreach (var script in tool.Scripts)
        {
            for (int i = 0; i < script.Orders.Count; i++)
            {
                if (script.Orders[i].DateTime.Date >= DateTime.Now.Date.AddDays(-3) ||
                    script.Orders[i].Status == "active" || script.Orders[i].Status == "watching")
                {
                    // Поиск заявки скрипта в коллекции всех заявок торговой сессии
                    var orders = TradingSystem.Orders.ToArray();
                    var y = script.Orders[i].OrderNo == 0 ?
                        Array.FindIndex(orders, x => x.TrID == script.Orders[i].TrID) :
                        Array.FindIndex(orders, x => x.OrderNo == script.Orders[i].OrderNo);

                    // Обновление свойств заявки из коллекции всех заявок и приведение обеих заявок к одному объекту
                    if (y > -1)
                    {
                        if (orders[y].Status == script.Orders[i].Status)
                            orders[y].DateTime = script.Orders[i].DateTime;
                        orders[y].Sender = script.Orders[i].Sender;
                        orders[y].Signal = script.Orders[i].Signal;
                        orders[y].Note = script.Orders[i].Note;
                        Window.Dispatcher.Invoke(() => script.Orders[i] = orders[y]);
                    }
                    else if (script.Orders[i].Status == "watching" || script.Orders[i].Status == "active" &&
                        DateTime.Today.DayOfWeek != DayOfWeek.Saturday && DateTime.Today.DayOfWeek != DayOfWeek.Sunday)
                    {
                        script.Orders[i].Status = "lost";
                        script.Orders[i].DateTime = DateTime.Now.AddDays(-2);
                        AddInfo("BringOrdersInLine: " +
                            script.Name + ": Активная заявка не актуальна. Статус обновлён.");
                    }
                }
            }
        }
    }

    public void ClearObsoleteData(Tool tool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));

        foreach (var script in tool.Scripts)
        {
            var obsoleteOrders = script.Orders.Where(x => 
                x.DateTime.Date < DateTime.Today.AddDays(-TradingSystem.Settings.ShelfLifeOrdersScripts));
            var obsoleteTrades = script.Trades.Where(x => 
                x.DateTime.Date < DateTime.Today.AddDays(-TradingSystem.Settings.ShelfLifeTradesScripts));
            Window.Dispatcher.Invoke(() =>
            {
                foreach (var order in obsoleteOrders) script.Orders.Remove(order);
                foreach (var trade in obsoleteTrades) script.Trades.Remove(trade);
            });
        }
    }

    public async Task<bool> UpdateOrdersAndPositionAsync(Script script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));

        script.LastExecuted = script.Orders.ToArray().LastOrDefault(x => x.Status == "matched" && x.Note != "NM");

        var activeOrders = script.Orders.ToArray()
            .Where(x => x.Sender == script.Name && (x.Status is "active" or "watching")).ToArray();
        if (activeOrders.Length > 1)
        {
            script.ActiveOrder = null;
            AddInfo(script.Name + ": Отмена активных заявок скрипта: " + activeOrders.Length);
            foreach (var order in activeOrders) await Connector.CancelOrderAsync(order);

            for (int i = 0; i < 12; i++)
            {
                Thread.Sleep(250);
                if (!activeOrders.Where(x => x.Status is "active" or "watching").Any()) return true;
            }

            AddInfo(script.Name + ": Не удалось вовремя отменить активные заявки.");
            return false;
        }
        script.ActiveOrder = activeOrders.SingleOrDefault();
        return true;
    }

    public async Task<bool> CalculateAsync(Script script, Security security)
    {
        if (security == null) throw new ArgumentNullException(nameof(security));
        if (!await UpdateOrdersAndPositionAsync(script)) return false;
        script.Calculate(security);
        return true;
    }

    public async Task ProcessOrdersAsync(Tool tool, Script script, int volume, bool nowBidding, double atr)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));
        if (script == null) throw new ArgumentNullException(nameof(script));
        if (volume < 1) throw new ArgumentOutOfRangeException(nameof(volume), "volume must be >= 1");
        if (atr < double.Epsilon) throw new ArgumentOutOfRangeException(nameof(atr), "atr must be > 0");

        if (script.Result.Type is ScriptType.OSC or ScriptType.Line) await ProcessOSCAsync(tool, script, volume);
        else if (script.Result.Type is ScriptType.LimitLine) await ProcessLimitLineAsync(tool, script, volume);
        else if (script.Result.Type is ScriptType.StopLine)
            await ProcessStopLineAsync(tool, script, volume, nowBidding, atr);
        else AddInfo(script.Name + ": Неизвестный тип скрипта: " + script.Result.Type, notify: true);
    }

    private async Task ProcessOSCAsync(Tool tool, Script script, int volume)
    {
        var basicSecurity = tool.BasicSecurity;
        var security = tool.MySecurity;
        var prevClose = basicSecurity?.Bars.DateTime[^1] > security.Bars.DateTime[^1] ?
            security.Bars.Close[^1] : security.Bars.Close[^2];

        var isGrow = script.Result.IsGrow;
        var lastExecuted = script.LastExecuted;

        // Работа с активной заявкой
        if (script.ActiveOrder != null)
        {
            var activeOrder = script.ActiveOrder;
            if (script.Result.OnlyLimit)
            {
                if (activeOrder.Quantity - activeOrder.Balance > 0.00001 || activeOrder.Note == "PartEx")
                {
                    if (Math.Abs(activeOrder.Price - prevClose) > 0.00001)
                        await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit,
                            prevClose, activeOrder.Balance, activeOrder.Signal, script, "PartEx");
                }
                else if ((activeOrder.BuySell == "B") != isGrow[^1] ||
                    (script.CurrentPosition == PositionType.Short) != isGrow[^1] || volume == 0)
                {
                    await Connector.CancelOrderAsync(activeOrder);
                }
                else if (Math.Abs(activeOrder.Price - prevClose) > 0.00001 ||
                    Math.Abs(activeOrder.Balance - volume) > 1)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit,
                        prevClose, volume, activeOrder.Signal, script, activeOrder.Note);
                }
            }
            else if (DateTime.Now >= activeOrder.Time.AddMinutes(5))
            {
                await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Market, prevClose,
                    activeOrder.Balance, activeOrder.Signal + "AtMarket", script, activeOrder.Note);
            }
            else if ((activeOrder.BuySell == "B") != isGrow[^1] || volume == 0)
            {
                await Connector.CancelOrderAsync(activeOrder);
            }
            return;
        }

        // Проверка условий для выхода
        if (lastExecuted != null && lastExecuted.DateTime >= script.Result.iLastDT || volume == 0 ||
            basicSecurity == null && security.Bars.DateTime[^1].Date != security.Bars.DateTime[^2].Date ||
            basicSecurity != null && basicSecurity.Bars.DateTime[^1].Date != basicSecurity.Bars.DateTime[^2].Date)
            return;

        // Проверка условий для выставления новой заявки
        if (script.CurrentPosition == PositionType.Short && isGrow[^1] ||
            script.CurrentPosition == PositionType.Long && !isGrow[^1])
        {
            await Connector.SendOrderAsync(security, OrderType.Limit,
                isGrow[^1], prevClose, volume, isGrow[^1] ? "BuyAtLimit" : "SellAtLimit", script);
            WriteLog(script, tool.Name);
        }
    }

    private async Task ProcessLimitLineAsync(Tool tool, Script script, int volume)
    {
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity;
        var orderPrice = basicSecurity?.Bars.DateTime[^1] > security.Bars.DateTime[^1] ?
            script.Result.Indicators[0][^1] : script.Result.Indicators[0][^2];

        var isGrow = script.Result.IsGrow;
        var activeOrder = script.ActiveOrder;
        var lastExecuted = script.LastExecuted;

        // Работа с активной заявкой // Нужно сделать уверенную замену через ReplaceOrder
        if (activeOrder != null)
        {
            if (activeOrder.Quantity - activeOrder.Balance > 0.00001)
            {
                if (Math.Abs(activeOrder.Price - orderPrice) > 0.00001)
                {
                    int Quantity = activeOrder.Quantity - activeOrder.Balance;
                    if (await Connector.CancelOrderAsync(activeOrder))
                        await Connector.SendOrderAsync(security, OrderType.Market,
                            activeOrder.BuySell == "S", orderPrice, Quantity, "CancelPartEx", script, "NM");
                }
            }
            else if (volume == 0) await Connector.CancelOrderAsync(activeOrder);
            else if (Math.Abs(activeOrder.Price - orderPrice) > 0.00001 || Math.Abs(activeOrder.Balance - volume) > 1)
            {
                if (orderPrice - security.MinPrice > -0.00001 && orderPrice - security.MaxPrice < 0.00001)
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit,
                        orderPrice, volume, activeOrder.Signal, script, activeOrder.Note);
                else await Connector.CancelOrderAsync(activeOrder);
            }
            return;
        }

        // Проверка условий для выхода
        if (lastExecuted != null && lastExecuted.DateTime >= script.Result.iLastDT || volume == 0 ||
            basicSecurity == null && security.Bars.DateTime[^1].Date != security.Bars.DateTime[^2].Date ||
            basicSecurity != null && basicSecurity.Bars.DateTime[^1].Date != basicSecurity.Bars.DateTime[^2].Date)
            return;

        // Проверка условий для выставления новой заявки
        if ((script.CurrentPosition == PositionType.Short && isGrow[^1] ||
            script.CurrentPosition == PositionType.Long && !isGrow[^1]) &&
            orderPrice - security.MinPrice > -0.00001 && orderPrice - security.MaxPrice < 0.00001)
        {
            await Connector.SendOrderAsync(security, OrderType.Limit,
                isGrow[^1], orderPrice, volume, isGrow[^1] ? "BuyAtLimit" : "SellAtLimit", script);
            WriteLog(script, tool.Name);
        }
    }

    private async Task ProcessStopLineAsync(Tool tool, Script script, int volume, bool nowBidding, double atr)
    {
        var security = tool.MySecurity;
        var basicSecurity = tool.BasicSecurity;
        var prevClose = security.Bars.Close[^2];
        var prevStopLine = script.Result.Indicators[0][^2];
        if (basicSecurity?.Bars.DateTime[^1] > security.Bars.DateTime[^1])
        {
            prevClose = security.Bars.Close[^1];
            prevStopLine = script.Result.Indicators[0][^1];
        }

        // Работа с активной заявкой
        var isGrow = script.Result.IsGrow;
        if (script.ActiveOrder != null)
        {
            var activeOrder = script.ActiveOrder;
            if (activeOrder.Condition != "None")
            {
                double Price = activeOrder.BuySell == "B" ? prevStopLine + security.MinStep : prevStopLine - security.MinStep;
                if (activeOrder.Status == "active")
                {
                    if (DateTime.Now > activeOrder.Time.AddSeconds(tool.WaitingLimit) && nowBidding)
                    {
                        await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Market,
                            prevClose, activeOrder.Balance, activeOrder.Signal + "AtMarket", script);
                    }
                }
                else if (volume == 0 || DateTime.Now < DateTime.Today.AddHours(7.5) && activeOrder.Note == null ||
                    (activeOrder.BuySell == "B") == isGrow[^1]) await Connector.CancelOrderAsync(activeOrder);
                else if (Math.Abs(activeOrder.Price - Price) > 0.00001 || activeOrder.Balance - volume > 1)
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Conditional, Price, volume, activeOrder.Signal, script);
            }
            else if (nowBidding)
            {
                if (activeOrder.Note == "NB")
                {
                    if (DateTime.Now > DateTime.Today.AddHours(7.5) && security.LastTrade.DateTime > DateTime.Today.AddHours(7.5) &&
                        DateTime.Now < DateTime.Today.AddHours(9.5) ||
                        DateTime.Now > DateTime.Today.AddHours(10.5) && security.LastTrade.DateTime > DateTime.Today.AddHours(10.5))
                    {
                        await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Limit, prevClose,
                            activeOrder.Balance, activeOrder.Signal + "AtClose", script, "CloseNB");
                    }
                }
                else if (DateTime.Now >= activeOrder.Time.AddSeconds(tool.WaitingLimit))
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security, OrderType.Market, prevClose,
                        activeOrder.Balance, activeOrder.Signal + "AtMarket", script, activeOrder.Note);
                }
            }
            return;
        }

        // Проверка условий для выхода
        var lastExecuted = script.LastExecuted;
        if (!nowBidding || volume == 0) return;
        if (lastExecuted != null && 
            lastExecuted.DateTime.Date == DateTime.Today && lastExecuted.DateTime >= script.Result.iLastDT)
        {
            if (lastExecuted.Note == null ||
                lastExecuted.Note == "NB" && lastExecuted.DateTime < DateTime.Today.AddHours(7.5)) return;

            for (int bar = script.Result.Indicators[0].Length - 1; bar > 1; bar--)
            {
                if (lastExecuted.BuySell == "B" && !isGrow[bar - 1] || lastExecuted.BuySell == "S" && isGrow[bar - 1])
                {
                    if (Math.Abs(script.Result.Indicators[0][bar - 1] - script.Result.Indicators[0][^2]) < 0.00001)
                        return;
                    else break;
                }
            }
        }

        // Проверка условий для выставления новой заявки
        var curPosition = script.CurrentPosition;
        if (security.LastTrade.DateTime < DateTime.Today.AddHours(7.5) && DateTime.Now < DateTime.Today.AddHours(7.5))
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
            double Price = isGrow[^1] ? prevStopLine - security.MinStep : prevStopLine + security.MinStep;
            await Connector.SendOrderAsync(security, OrderType.Conditional,
                !isGrow[^1], Price, volume, !isGrow[^1] ? "BuyAtStop" : "SellAtStop", script);
            WriteLog(script, tool.Name);
        }
        else
        {
            AddInfo(script.Name + ": Текущая позиция скрипта не соответствует IsGrow.", notify: true);
            if (security.LastTrade.DateTime < DateTime.Today.AddHours(10.5) && DateTime.Now < DateTime.Today.AddHours(10.5))
            {
                double Price = isGrow[^1] ? prevStopLine - atr : prevStopLine + atr;
                await Connector.SendOrderAsync(security, OrderType.Limit,
                    isGrow[^1], Price, volume, isGrow[^1] ? "BuyAtLimitNB" : "SellAtLimitNB", script, "NB");
            }
            else
            {
                double Price = isGrow[^1] ? prevStopLine - security.MinStep : prevStopLine + security.MinStep;
                await Connector.SendOrderAsync(security, OrderType.Limit, isGrow[^1], Price, volume, "UnknowEvent", script);
            }
            WriteLog(script, tool.Name);
        }
    }

    public bool AlignData(Tool tool, Script script)
    {
        var security = tool.MySecurity;
        var initLength = tool.MySecurity.Bars.Close.Length;
        var x = script.Result.IsGrow.Length - initLength;
        if (x > 0)
        {
            script.Result.IsGrow = script.Result.IsGrow[x..];
            for (int i = 0; i < script.Result.Indicators.Length; i++)
                if (script.Result.Indicators[i] != null)
                    script.Result.Indicators[i] = script.Result.Indicators[i][x..];
        }
        else if (x < 0)
        {
            if (script.Result.IsGrow.Length > 500)
            {
                AddInfo(tool.Name + ": Количество торговых баров больше базисных: " +
                    initLength + "/" + script.Result.IsGrow.Length + " Обрезка.", notify: true);

                x = security.SourceBars.Close.Length - tool.BasicSecurity.SourceBars.Close.Length + 20;
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

    public void UpdateView(Tool tool, Script script)
    {
        string selectedScript = null;
        var mainModel = tool.MainModel;
        Window.Dispatcher.Invoke(() =>
        {
            Grid MyGrid = (((TradingSystem.Window.TabsTools.Items[TradingSystem.Tools.IndexOf(tool)]
            as TabItem).Content as Grid).Children[1] as Grid).Children[0] as Grid;
            selectedScript = MyGrid.Children.OfType<ComboBox>().Last().Text;

            script.BlockInfo.Text = "IsGrow[i] " + script.Result.IsGrow[^1] +
            "     IsGrow[i-1] " + script.Result.IsGrow[^2] + "\nType " + script.Result.Type;
        });

        foreach (var ann in mainModel.Annotations.ToArray())
            if (ann.ToolTip == script.Name || ann.ToolTip is "System" or null) mainModel.Annotations.Remove(ann);

        if (script.Result.Type != ScriptType.OSC)
            foreach (Series MySeries in mainModel.Series.ToArray())
                if (MySeries.Title == script.Name) mainModel.Series.Remove(MySeries);

        if (selectedScript == script.Name || selectedScript == "AllScripts")
        {
            if (script.Result.Type == ScriptType.OSC) TradingSystem.ToolManager.UpdateMiniModel(tool, script);
            else foreach (double[] Indicator in script.Result.Indicators)
                    mainModel.Series.Add(MakeLineSeries(Indicator, script.Name));

            if (!tool.ShowBasicSecurity)
            {
                var trades = script.Trades.ToArray()
                    .Concat(TradingSystem.SystemTrades.ToArray()
                    .Where(x => x.Seccode == tool.MySecurity.Seccode)).ToArray();
                var orders =
                    TradingSystem.Orders.ToArray()
                    .Where(x => x.Seccode == tool.MySecurity.Seccode && x.Status is "active" or "watching").ToArray();
                if (trades.Length > 0 || orders.Length > 0) AddAnnotations(tool, trades, orders);
            }
        }
    }

    public void WriteLog(Script script, string toolName)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));
        if (toolName == null) throw new ArgumentNullException(nameof(toolName));

        var data = DateTime.Now + ": /////////////////// Script: " + script.Name + "\nType " + script.Result.Type +
            "\nCurPosition " + script.CurrentPosition + "\nIsGrow[^1] " + script.Result.IsGrow[^1] +
            "\nIsGrow[^2] " + script.Result.IsGrow[^2] + "\nOnlyLimit " + script.Result.OnlyLimit +
            "\nCentre " + script.Result.Centre + "\nLevel " + script.Result.Level;

        for (int i = 0; i < script.Result.Indicators.Length; i++)
            if (script.Result.Indicators[i] != null)
                data += "\nIndicator" + (i + 1) + "[^2] " + script.Result.Indicators[i][^2];
        try
        {
            System.IO.File.AppendAllText("Logs/LogsTools/" + toolName + ".txt", data + "\n");
        }
        catch (Exception e) { AddInfo(toolName + ": Исключение логирования скрипта: " + e.Message); }
    }

    private void AddUpperControls(Script script, UIElementCollection uiCollection, ScriptProperties properties)
    {
        var props = properties.UpperProperties;

        uiCollection.Add(GetTextBlock(props[0], 5, 20));
        uiCollection.Add(GetTextBox(script, props[0], 65, 20));

        uiCollection.Add(GetTextBlock(props[1], 5, 40));
        uiCollection.Add(GetTextBox(script, props[1], 65, 40));

        if (props.Length > 2)
        {
            uiCollection.Add(GetTextBlock(props[2], 5, 60));
            uiCollection.Add(GetTextBox(script, props[2], 65, 60));
        }
        if (props.Length > 3)
        {
            uiCollection.Add(GetTextBlock(props[3], 105, 20));
            uiCollection.Add(GetTextBox(script, props[3], 165, 20));
        }
        if (props.Length > 4)
        {
            uiCollection.Add(GetTextBlock(props[4], 105, 40));
            uiCollection.Add(GetTextBox(script, props[4], 165, 40));
        }
        if (props.Length > 5)
        {
            uiCollection.Add(GetTextBlock(props[5], 105, 60));
            uiCollection.Add(GetTextBox(script, props[5], 165, 60));
        }
        if (props.Length > 6) AddInfo(script.Name + ": Непредвиденное количество верхних контролов.");

        ComboBox comboBox = new()
        {
            Width = 90,
            Margin = new System.Windows.Thickness(5, 80, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            ItemsSource = new PositionType[] { PositionType.Long, PositionType.Short, PositionType.Neutral }
        };
        Binding binding = new() { Source = script, Path = new System.Windows.PropertyPath("CurrentPosition"), Mode = BindingMode.TwoWay };
        comboBox.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, binding);
        uiCollection.Add(comboBox);

        if (properties.MAProperty != null)
        {
            ComboBox comboBox2 = new()
            {
                Width = 90,
                Margin = new System.Windows.Thickness(105, 80, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                ItemsSource = properties.MAObjects
            };
            Binding binding2 = new()
            {
                Source = script,
                Path = new System.Windows.PropertyPath(properties.MAProperty),
                Mode = BindingMode.TwoWay
            };
            comboBox2.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, binding2);
            uiCollection.Add(comboBox2);
        }
    }

    private void AddMiddleControls(Script script, UIElementCollection uiCollection, ScriptProperties properties)
    {
        var props = properties.MiddleProperties;
        uiCollection.Add(GetCheckBox(script, props[0], props[0], 5, 110));
        if (props.Length > 1) uiCollection.Add(GetCheckBox(script, props[1], props[1], 5, 130));
        if (props.Length > 2) uiCollection.Add(GetCheckBox(script, props[2], props[2], 5, 150));
        if (props.Length > 3) uiCollection.Add(GetCheckBox(script, props[3], props[3], 105, 110));
        if (props.Length > 4) uiCollection.Add(GetCheckBox(script, props[4], props[4], 105, 130));
        if (props.Length > 5) uiCollection.Add(GetCheckBox(script, props[5], props[5], 105, 150));
        if (props.Length > 6) AddInfo(script.Name + ": Непредвиденное количество средних контролов.");
    }

    private void AddAnnotations(Tool tool, Trade[] MyTrades, Order[] MyOrders)
    {
        int i;
        OxyColor MyColor;
        var security = tool.MySecurity;
        double yStartPoint, yEndPoint;
        List<(Trade, int)> trades = new();
        List<(Trade, int)> tradesOnlyWithPoint = new();
        foreach (Trade trade in MyTrades)
        {
            i = Array.FindIndex(security.Bars.DateTime, x => x.AddMinutes(security.Bars.TF) > trade.DateTime);
            if (i < 0)
            {
                AddInfo("AddAnnotations: Не найден бар, на котором произошла сделка.");
                continue;
            }

            var sameTrade = trades.SingleOrDefault(x => x.Item2 == i && x.Item1.BuySell == trade.BuySell);
            if (sameTrade.Item1 != null)
            {
                sameTrade.Item1.Quantity += trade.Quantity;
                if (sameTrade.Item1.Price != trade.Price) tradesOnlyWithPoint.Add((trade, i));
            }
            else trades.Add((trade.GetCopy(), i));
        }
        foreach ((Trade, int) MyTrade in trades)
        {
            var trade = MyTrade.Item1;
            i = MyTrade.Item2;

            if (trade.BuySell == "B")
            {
                MyColor = Theme.GreenBar;
                yStartPoint = security.Bars.Low[i] - security.Bars.Low[i] * 0.001;
                yEndPoint = security.Bars.Low[i];
            }
            else
            {
                MyColor = Theme.RedBar;
                yStartPoint = security.Bars.High[i] + security.Bars.High[i] * 0.001;
                yEndPoint = security.Bars.High[i];
            }

            string text = trade.SignalOrder is "BuyAtLimit" or "SellAtLimit" or "BuyAtStop" or "SellAtStop" ?
                trade.Price + "; " + trade.Quantity : trade.SignalOrder + "; " + trade.Price + "; " + trade.Quantity;
            tool.MainModel.Annotations.Add(new ArrowAnnotation()
            {
                Color = MyColor,
                Text = text,
                StartPoint = new DataPoint(i, yStartPoint),
                EndPoint = new DataPoint(i, yEndPoint),
                ToolTip = trade.SenderOrder
            });
            tool.MainModel.Annotations.Add(new PointAnnotation()
            {
                X = i,
                Y = trade.Price,
                Fill = OxyColors.WhiteSmoke,
                Stroke = OxyColors.Black,
                StrokeThickness = 1,
                ToolTip = trade.SenderOrder
            });
        }
        foreach ((Trade, int) MyTrade in tradesOnlyWithPoint)
        {
            tool.MainModel.Annotations.Add(new PointAnnotation()
            {
                X = MyTrade.Item2,
                Y = MyTrade.Item1.Price,
                Fill = OxyColors.WhiteSmoke,
                Stroke = OxyColors.Black,
                StrokeThickness = 1,
                ToolTip = MyTrade.Item1.SenderOrder
            });
        }
        foreach (Order ActiveOrder in MyOrders)
        {
            tool.MainModel.Annotations.Add(new LineAnnotation()
            {
                Type = LineAnnotationType.Horizontal,
                Y = ActiveOrder.Price,
                MinimumX = tool.MainModel.Axes[0].ActualMaximum - 20,
                Color = ActiveOrder.BuySell == "B" ? Theme.GreenBar : Theme.RedBar,
                ToolTip = ActiveOrder.Sender,
                Text = ActiveOrder.Signal,
                StrokeThickness = 2
            });
        }
    }

    private static LineSeries MakeLineSeries(double[] Indicator, string Name)
    {
        DataPoint[] Points = new DataPoint[Indicator.Length];
        for (int i = 0; i < Indicator.Length; i++) Points[i] = new DataPoint(i, Indicator[i]);
        return new LineSeries() { ItemsSource = Points, Color = Theme.Indicator, Title = Name };
    }
}
