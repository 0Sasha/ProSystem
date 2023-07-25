using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace ProSystem.Services;

internal class ScriptManager : IScriptManager
{
    private readonly MainWindow Window;
    private readonly Func<Order, bool> CancelOrder;
    private readonly Action<string> Inform;

    public ScriptManager(MainWindow window, Func<Order, bool> cancelOrder, Action<string> inform)
    {
        Window = window;
        CancelOrder = cancelOrder;
        Inform = inform;
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

    public bool UpdateOrdersAndPosition(Script script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));

        script.LastExecuted = script.Orders.ToArray().LastOrDefault(x => x.Status == "matched" && x.Note != "NM");

        var activeOrders = script.Orders.ToArray()
            .Where(x => x.Sender == script.Name && (x.Status is "active" or "watching")).ToArray();
        if (activeOrders.Length > 1)
        {
            script.ActiveOrder = null;
            Inform(script.Name + ": Отмена активных заявок скрипта: " + activeOrders.Length);
            foreach (Order MyOrder in activeOrders) CancelOrder(MyOrder);

            for (int timeout = 500; timeout <= 1500; timeout += 500)
            {
                Thread.Sleep(timeout);
                if (!activeOrders.Where(x => x.Status is "active" or "watching").Any()) return true;
            }

            Inform(script.Name + ": Не удалось вовремя отменить активные заявки.");
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
        catch (Exception e) { Inform(toolName + ": Исключение логирования скрипта: " + e.Message); }
    }
}
