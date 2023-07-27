using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace ProSystem.Services;

public interface IScriptManager
{
    public void InitializeScripts(IEnumerable<Script> scripts, TabItem tab);

    public void IdentifyOrders(IEnumerable<Script> scripts, IEnumerable<Order> orders, string seccode);

    public void IdentifyTrades(IEnumerable<Script> scripts, IEnumerable<Trade> trades, string seccode);

    public bool UpdateOrdersAndPosition(Script script);

    public bool Calculate(Script script, Security security);

    public void WriteLog(Script script, string toolName);
}
