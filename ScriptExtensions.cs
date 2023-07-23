using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Controls;

namespace ProSystem;

public static class ScriptExtensions
{
    public static bool UpdateOrdersAndPosition(this Script script, Func<Order, bool> cancelOrder, Action<string> notify)
    {
        script.LastExecuted = script.Orders.ToArray().LastOrDefault(x => x.Status == "matched" && x.Note != "NM");

        var activeOrders = script.Orders.ToArray()
            .Where(x => x.Sender == script.Name && (x.Status is "active" or "watching")).ToArray();
        if (activeOrders.Length > 1)
        {
            script.ActiveOrder = null;
            notify(script.Name + ": Отмена активных заявок скрипта: " + activeOrders.Length);
            foreach (Order MyOrder in activeOrders) cancelOrder(MyOrder);

            Thread.Sleep(500);
            if (!activeOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

            Thread.Sleep(1000);
            if (!activeOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

            Thread.Sleep(1500);
            if (!activeOrders.Where(x => x.Status is "active" or "watching").Any()) return true;

            notify(script.Name + ": Не удалось вовремя отменить активные заявки.");
            return false;
        }
        script.ActiveOrder = activeOrders.SingleOrDefault();
        return true;
    }
}
