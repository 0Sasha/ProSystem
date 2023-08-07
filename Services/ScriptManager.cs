using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using static ProSystem.Controls;

namespace ProSystem.Services;

internal class ScriptManager : IScriptManager
{
    private readonly Window Window;
    private readonly Connector Connector;
    private readonly AddInformation AddInfo;

    public ScriptManager(Window window, Connector connector, AddInformation addInfo)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Connector = connector ?? throw new ArgumentNullException(nameof(connector));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
    }

    public void InitializeScripts(IEnumerable<Script> scripts, TabItem tabTool)
    {
        int i = 1;
        foreach (var script in scripts)
        {
            var props = script.GetScriptProperties();
            Window.Dispatcher.Invoke(() =>
            {
                if (props.IsOSC)
                {
                    var plot = ((tabTool.Content as Grid).Children[0] as Grid).Children[0] as OxyPlot.SkiaSharp.Wpf.PlotView;
                    if (plot.Visibility == System.Windows.Visibility.Hidden)
                    {
                        Grid.SetRow(((tabTool.Content as Grid).Children[0] as Grid).Children[1] as OxyPlot.SkiaSharp.Wpf.PlotView, 1);
                        plot.Visibility = System.Windows.Visibility.Visible;
                    }
                }
                var uiCollection = (((tabTool.Content as Grid).Children[1] as Grid).Children[i] as Grid).Children;

                uiCollection.Clear();
                uiCollection.Add(GetTextBlock(script.Name, 5, 0));
                AddUpperControls(script, uiCollection, props);
                if (props.MiddleProperties != null) AddMiddleControls(script, uiCollection, props);

                TextBlock textBlock = GetTextBlock("Block Info", 5, 170);
                script.BlockInfo = textBlock;
                uiCollection.Add(textBlock);
            });
            i++;
        }
    }

    public void IdentifyOrders(IEnumerable<Script> scripts, IEnumerable<Order> allOrders, string seccode)
    {
        if (scripts == null) throw new ArgumentNullException(nameof(scripts));
        if (allOrders == null) throw new ArgumentNullException(nameof(allOrders));
        if (seccode == null || seccode == "") throw new ArgumentNullException(nameof(seccode));

        var orders = allOrders.ToArray().Where(x => x.Sender == null && x.Seccode == seccode);
        foreach (Order unknowOrder in orders)
        {
            foreach (Script script in scripts)
            {
                int i = Array.FindIndex(script.Orders.ToArray(), x => x.TrID == unknowOrder.TrID);
                if (i > -1)
                {
                    if (unknowOrder.Status == script.Orders[i].Status)
                        unknowOrder.DateTime = script.Orders[i].DateTime;
                    unknowOrder.Sender = script.Orders[i].Sender;
                    unknowOrder.Signal = script.Orders[i].Signal;
                    unknowOrder.Note = script.Orders[i].Note;
                    Window.Dispatcher.Invoke(() => script.Orders[i] = unknowOrder);
                    break;
                }
            }
        }
    }

    public void IdentifyTrades(IEnumerable<Script> scripts, IEnumerable<Trade> allTrades, string seccode)
    {
        if (scripts == null) throw new ArgumentNullException(nameof(scripts));
        if (allTrades == null) throw new ArgumentNullException(nameof(allTrades));
        if (seccode == null || seccode == "") throw new ArgumentNullException(nameof(seccode));

        var trades = allTrades.ToArray().Where(x => x.SenderOrder == null && x.Seccode == seccode);
        foreach (Trade unknowTrade in trades)
        {
            foreach (Script script in scripts)
            {
                int i = Array.FindIndex(script.Orders.ToArray(), x => x.OrderNo == unknowTrade.OrderNo);
                if (i > -1)
                {
                    unknowTrade.SenderOrder = script.Orders[i].Sender;
                    unknowTrade.SignalOrder = script.Orders[i].Signal;
                    unknowTrade.NoteOrder = script.Orders[i].Note;
                    Window.Dispatcher.Invoke(() => script.Trades.Add(unknowTrade));
                    break;
                }
            }
        }
    }

    public void BringOrdersInLine(Tool tool, IEnumerable<Order> allOrders)
    {
        foreach (var script in tool.Scripts)
        {
            for (int i = 0; i < script.Orders.Count; i++)
            {
                if (script.Orders[i].DateTime.Date >= DateTime.Now.Date.AddDays(-3) ||
                    script.Orders[i].Status == "active" || script.Orders[i].Status == "watching")
                {
                    // Поиск заявки скрипта в коллекции всех заявок торговой сессии
                    var orders = allOrders.ToArray();
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

    public void ClearObsoleteData(Tool tool, Settings settings)
    {
        foreach (var script in tool.Scripts)
        {
            var obsoleteOrders = script.Orders
                .Where(x => x.DateTime.Date < DateTime.Today.AddDays(-settings.ShelfLifeOrdersScripts));
            var obsoleteTrades = script.Trades
                .Where(x => x.DateTime.Date < DateTime.Today.AddDays(-settings.ShelfLifeTradesScripts));
            Window.Dispatcher.Invoke(() =>
            {
                foreach (var order in obsoleteOrders) script.Orders.Remove(order);
                foreach (var trade in obsoleteTrades) script.Trades.Remove(trade);
            });
        }
    }

    public bool UpdateOrdersAndPosition(Script script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));

        script.LastExecuted = script.Orders.ToArray().LastOrDefault(x => x.Status == "matched" && x.Note != "NM");

        var activeOrders = script.Orders.ToArray()
            .Where(x => x.Sender == script.Name && (x.Status is "active" or "watching")).ToArray();
        if (activeOrders.Length > 1)
        {
            script.ActiveOrder = null;
            AddInfo(script.Name + ": Отмена активных заявок скрипта: " + activeOrders.Length);
            foreach (Order MyOrder in activeOrders) Connector.CancelOrderAsync(MyOrder);

            for (int timeout = 500; timeout <= 1500; timeout += 500)
            {
                Thread.Sleep(timeout);
                if (!activeOrders.Where(x => x.Status is "active" or "watching").Any()) return true;
            }

            AddInfo(script.Name + ": Не удалось вовремя отменить активные заявки.");
            return false;
        }
        script.ActiveOrder = activeOrders.SingleOrDefault();
        return true;
    }

    public bool Calculate(Script script, Security security)
    {
        if (security == null) throw new ArgumentNullException(nameof(security));
        if (!UpdateOrdersAndPosition(script)) return false;
        script.Calculate(security);
        return true;
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
}
