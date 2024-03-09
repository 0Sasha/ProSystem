using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ProSystem.Services;

internal class ToolManager : IToolManager
{
    private readonly Window Window;
    private readonly AddInformation AddInfo;
    private readonly TradingSystem TradingSystem;
    private readonly IScriptManager ScriptManager;
    private readonly Connector Connector;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    private DateTime ServerTime { get => Connector.ServerTime; }

    public ToolManager(Window window, TradingSystem tradingSystem, IScriptManager scriptManager, AddInformation addInfo)
    {
        Window = window;
        AddInfo = addInfo;
        ScriptManager = scriptManager;
        TradingSystem = tradingSystem;
        Connector = TradingSystem.Connector;
    }

    public void Initialize(Tool tool)
    {
        if (tool.BaseTF < 1) tool.BaseTF = 30;
        tool.BrushState = tool.Active ? Theme.Green : Theme.Red;
        tool.Tab = Controls.GetTab(tool);
        UpdateControlPanel(tool, true);
        UpdateView(tool, true);
    }

    public void UpdateControlPanel(Tool tool, bool updateScriptPanel) =>
        Controls.UpdateControlPanel(tool, updateScriptPanel, ChangeActivityToolAsync, UpdateViewTool);

    public async Task ChangeActivityAsync(Tool tool)
    {
        if (Connector.Connection == ConnectionState.Connected)
        {
            var security = tool.Security;
            var basicSecurity = tool.BasicSecurity;
            if (tool.Active)
            {
                _ = Task.Run(async () =>
                {
                    await Connector.UnsubscribeFromTradesAsync(security);
                    if (basicSecurity != null) await Connector.UnsubscribeFromTradesAsync(basicSecurity);
                });
            }
            else
            {
                try
                {
                    await Connector.RequestBarsAsync(tool);
                    await Connector.OrderSecurityInfoAsync(security);
                    if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
                    {
                        await Task.Delay(500);
                        if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
                        {
                            AddInfo("Failed to activate the tool because there are no bars");
                            return;
                        }
                    }

                    await Connector.SubscribeToTradesAsync(security);
                    if (basicSecurity != null) await Connector.SubscribeToTradesAsync(basicSecurity);
                    await Connector.RequestBarsAsync(tool);
                }
                catch (Exception ex)
                {
                    AddInfo("ChangeActivity: " + ex.Message);
                    AddInfo("StackTrace: " + ex.StackTrace, false);
                    return;
                }
            }
        }

        tool.Active = !tool.Active;
        tool.BrushState = tool.Active ? (tool.StopTrading ? Theme.Orange : Theme.Green) : Theme.Red;
        var btnContent = tool.Active ? "Deactivate tool" : "Activate tool";

        if (tool.Tab != null)
        {
            Window.Dispatcher.Invoke(() => ((Grid)tool.Tab.Content).Children.OfType<Grid>()
                .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = btnContent);
        }
        tool.NotifyChange();
    }

    public async Task CalculateAsync(Tool tool)
    {
        while (Interlocked.Exchange(ref tool.IsOccupied, 1) != 0)
        {
            AddInfo(tool.Name + ": Calculate: method is occupied by another thread", false);
            await Task.Delay(500);
        }

        while (ServerTime.AddSeconds(-3) < tool.LastRecalc)
        {
            AddInfo(tool.Name + ": Calculate: waiting for data from server", false);
            await Task.Delay(1000);
        }

        try
        {
            if (await Connector.CheckToolAsync(tool)) await CalculateToolAsync(tool);
            AddInfo("Calculated: " + tool.Name, false);
        }
        catch (Exception e)
        {
            AddInfo(tool.Name + ": Calculate: " + e.Message, notify: true);
            AddInfo("StackTrace: " + e.StackTrace);
            if (e.InnerException != null)
            {
                AddInfo("Inner exception: " + e.InnerException.Message);
                AddInfo("StackTrace: " + e.InnerException.StackTrace);
            }
        }
        finally
        {
            tool.LastRecalc = ServerTime;
            tool.NextRecalc = ServerTime.AddSeconds(TradingSystem.RecalcInterval / 2);
            Interlocked.Exchange(ref tool.IsOccupied, 0);
        }
    }

    public async Task ReloadBarsAsync(Tool tool)
    {
        var security = tool.Security;
        var basicSecurity = tool.BasicSecurity;
        if (tool.Active)
        {
            while (Connector.Connection == ConnectionState.Connected && !TradingSystem.ReadyToTrade)
                await Task.Delay(100);
            await ChangeActivityAsync(tool);
            await Task.Delay(250);
        }
        security.SourceBars = null;
        security.Bars = null;
        if (basicSecurity != null)
        {
            basicSecurity.SourceBars = null;
            basicSecurity.Bars = null;
        }

        if (Connector.Connection == ConnectionState.Connected)
        {
            await Connector.RequestBarsAsync(tool);
            await Task.Delay(500);
            if (security.Bars != null) UpdateView(tool, true);
        }
    }

    public void UpdateView(Tool tool, bool updateScriptView)
    {
        if (updateScriptView && tool.Security.Bars != null)
        {
            if (tool.MainModel == null) Plot.UpdateModel(tool);
            foreach (var script in tool.Scripts)
            {
                script.Calculate(tool.BasicSecurity ?? tool.Security);
                ScriptManager.UpdateView(tool, script);
            }
        }
        Plot.UpdateModel(tool);
        if (tool.Model != null) Plot.UpdateMiniModel(tool, Window);
    }


    private async Task CalculateToolAsync(Tool tool)
    {
        await Connector.WaitForCertaintyAsync(tool);
        var toolState = CalculateToolState(tool);

        UpdateControlPanel(tool, toolState);
        UpdateBarsColor(tool, toolState);
        if (toolState.IsLogging) WriteStateLog(tool, toolState);

        IdentifyOrdersAndTrades(tool);
        if (!tool.StopTrading)
        {
            if (!await CancelUnknownOrdersAsync(tool)) return;
            if (toolState.ReadyToTrade)
            {
                await ScriptManager.UpdateStateAsync(tool);
                if (!await CheckPositionMatchingAsync(tool, toolState)) return;
                await NormalizePositionAsync(tool, toolState);
            }
        }
        else if (!await CancelActiveOrdersAsync(tool)) return;

        await ScriptManager.ProcessOrdersAsync(tool, toolState);
    }

    private void IdentifyOrdersAndTrades(Tool tool)
    {
        ScriptManager.IdentifyOrdersAndTrades(tool);

        var systemOrders = TradingSystem.SystemOrders;
        var unknownOrders = TradingSystem.Orders.ToArray()
            .Where(x => x.Sender == null && x.Seccode == tool.Security.Seccode);
        foreach (var unknownOrder in unknownOrders)
        {
            int i = Array.FindIndex(systemOrders.ToArray(),
                x => x.Id != 0 && x.Id == unknownOrder.Id || x.TrID != 0 && x.TrID == unknownOrder.TrID);
            if (i > -1)
            {
                unknownOrder.Sender = systemOrders[i].Sender;
                unknownOrder.Signal = systemOrders[i].Signal;
                unknownOrder.Note = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => systemOrders[i] = unknownOrder);
            }
        }

        var unknownTrades = TradingSystem.Trades.ToArray()
            .Where(x => x.OrderSender == null && x.Seccode == tool.Security.Seccode);
        foreach (var unknownTrade in unknownTrades)
        {
            int i = Array.FindIndex(systemOrders.ToArray(), x => x.Id == unknownTrade.OrderId);
            if (i > -1)
            {
                unknownTrade.OrderSender = systemOrders[i].Sender;
                unknownTrade.OrderSignal = systemOrders[i].Signal;
                unknownTrade.OrderNote = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => TradingSystem.SystemTrades.Add(unknownTrade));
            }
        }
    }


    private ToolState CalculateToolState(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);
        var average = Math.Round(tool.Security.Bars.Close.TakeLast(30).Average(), tool.Security.TickPrecision);
        var atr = Indicators.ATR(tool.Security.Bars.High, tool.Security.Bars.Low, tool.Security.Bars.Close, 50);

        var readyToTrade = !tool.StopTrading & TradingSystem.PortfolioManager.CheckEquity();
        var toolState = new ToolState(readyToTrade, IsLogging(tool), Connector.SecurityIsBidding(tool.Security))
        {
            IsNormalPrice = Connector.PriceIsNormal(tool.Security.Bars.Close[^1], average, atr[^2]),
            AveragePrice = average,
            ATR = atr[^2],
            LongReqs = tool.Security.InitReqLong,
            ShortReqs = tool.Security.InitReqShort
        };
        if (!toolState.IsNormalPrice) AddInfo(tool.Name + ": the price is out of range", notify: true);

        SetPositionVolumes(tool, toolState);
        SetBalance(tool, toolState);
        SetOrdersVolumes(tool, toolState);
        return toolState;
    }

    private bool IsLogging(Tool tool)
    {
        var sec = tool.Security;
        var basicSec = tool.BasicSecurity;
        if (ServerTime.Second >= 30 && (ServerTime.Minute is 0 or 29 or 30 or 59))
        {
            try
            {
                if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
                if (!Directory.Exists("Logs/Tools")) Directory.CreateDirectory("Logs/Tools");

                var path = "Logs/Tools/" + tool.Name + ".txt";
                if (!File.Exists(path)) File.Create(path).Close();

                var data = ServerTime.ToString(IC) + ": /////////////////// RECOUNT SCRIPTS" +
                    "\nLastTrade " + sec.LastTrade.Price.ToString(IC) +
                    "\nDateLastTrade " + sec.LastTrade.Time.ToString(IC) + "\n";

                if (sec.Bars != null)
                    data += "OHLCV[^1] " + sec.Bars.DateTime[^1] + "/" + sec.Bars.Open[^1] + "/" +
                        sec.Bars.High[^1] + "/" + sec.Bars.Low[^1] + "/" +
                        sec.Bars.Close[^1] + "/" + sec.Bars.Volume[^1] + "\n";

                if (basicSec != null && basicSec.Bars != null)
                    data += "BasicOHLCV[^1] " + basicSec.Bars.DateTime[^1] + "/" + basicSec.Bars.Open[^1] + "/" +
                        basicSec.Bars.High[^1] + "/" + basicSec.Bars.Low[^1] + "/" +
                        basicSec.Bars.Close[^1] + "/" + basicSec.Bars.Volume[^1] + "\n";

                File.AppendAllText(path, data);
                return true;
            }
            catch (Exception e) { AddInfo(tool.Name + ": logging exception: " + e.Message); }
        }
        return false;
    }

    private void SetPositionVolumes(Tool tool, ToolState toolState)
    {
        var saldo = TradingSystem.Portfolio.Saldo;
        var maxShare = saldo / 100 * TradingSystem.Settings.MaxShareInitReqsPosition;
        var symbol = tool.Security;
        if (tool.TradeShare)
        {
            var settings = TradingSystem.Settings;
            var optShare = saldo / 100 * tool.ShareOfFunds;
            if (optShare > maxShare)
            {
                AddInfo(tool.Name + ": ShareOfFunds exceeds risk level", settings.DisplaySpecialInfo, true);
                optShare = maxShare;
            }

            var longVol = Math.Floor(optShare / toolState.LongReqs) * symbol.LotSize;
            if (longVol < tool.MinQty)
            {
                AddInfo(tool.Name + ": LongVolume < MinNumberOfLots", settings.DisplaySpecialInfo, true);
                longVol = tool.MinQty;
            }
            if (longVol > tool.MaxQty)
            {
                AddInfo(tool.Name + ": LongVolume > MaxNumberOfLots", settings.DisplaySpecialInfo, true);
                longVol = tool.MaxQty;
            }
            if (longVol * toolState.LongReqs > maxShare)
            {
                AddInfo(tool.Name + ": LongVolume exceeds risk level", settings.DisplaySpecialInfo, true);
                longVol = Math.Floor(optShare / toolState.LongReqs) * symbol.LotSize;
            }

            var shortVol = Math.Floor(optShare / toolState.ShortReqs) * symbol.LotSize;
            if (shortVol < tool.MinQty)
            {
                AddInfo(tool.Name + ": shortVol < MinNumberOfLots", settings.DisplaySpecialInfo, true);
                shortVol = tool.MinQty;
            }
            if (shortVol > tool.MaxQty)
            {
                AddInfo(tool.Name + ": shortVol > MaxNumberOfLots", settings.DisplaySpecialInfo, true);
                shortVol = tool.MaxQty;
            }
            if (shortVol * toolState.ShortReqs > maxShare)
            {
                AddInfo(tool.Name + ": ShortVolume exceeds risk level", settings.DisplaySpecialInfo, true);
                shortVol = Math.Floor(optShare / toolState.ShortReqs) * symbol.LotSize;
            }

            toolState.LongVolume = longVol;
            toolState.ShortVolume = shortVol;

            toolState.LongRealVolume = Math.Round(optShare / toolState.LongReqs * symbol.LotSize, 4);
            toolState.ShortRealVolume = Math.Round(optShare / toolState.ShortReqs * symbol.LotSize, 4);
        }
        else
        {
            if (tool.HardQty * Math.Max(toolState.LongReqs, toolState.ShortReqs) > maxShare)
            {
                AddInfo(tool.Name + ": NumberOfLots превышает допустимый объём риска.", notify: true);
                toolState.LongVolume = Math.Floor(maxShare / toolState.LongReqs / symbol.LotSize) * symbol.LotSize;
                toolState.ShortVolume = Math.Floor(maxShare / toolState.ShortReqs / symbol.LotSize) * symbol.LotSize;
            }
            else
            {
                toolState.LongVolume = tool.HardQty;
                toolState.ShortVolume = tool.HardQty;
            }
            toolState.LongRealVolume = toolState.LongVolume;
            toolState.ShortRealVolume = toolState.ShortVolume;
        }
    }

    private void SetBalance(Tool tool, ToolState toolState)
    {
        toolState.Balance = 0;
        var position = TradingSystem.Portfolio.Positions
            .ToArray().SingleOrDefault(x => x.Seccode == tool.Security.Seccode);
        if (position != null) toolState.Balance = position.Saldo;

        toolState.RealBalance = toolState.Balance;
        if (tool.UseShiftBalance) toolState.Balance -= tool.BaseBalance;

        if (Math.Abs(toolState.Balance) >
            Math.Max(Math.Max(toolState.LongVolume, toolState.ShortVolume), tool.Security.LotSize) *
            TradingSystem.Settings.TolerancePosition)
        {
            if (DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                toolState.ReadyToTrade = false;
                if (tool.TriggerPosition == DateTime.MinValue)
                {
                    AddInfo(tool.Name +
                        ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                    tool.TriggerPosition = ServerTime.AddHours(12);
                }
            }
            else
            {
                AddInfo(tool.Name +
                    ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                if (tool.TriggerPosition == DateTime.MinValue)
                {
                    tool.TriggerPosition = ServerTime.AddHours(4);
                    toolState.ReadyToTrade = false;
                }
                else if (ServerTime < tool.TriggerPosition) toolState.ReadyToTrade = false;
                else AddInfo(tool.Name +
                    ": Объём текущей позиции всё ещё за пределами допустимого отклонения, но торговля разрешена.");
            }
        }
        else if (tool.TriggerPosition != DateTime.MinValue) tool.TriggerPosition = DateTime.MinValue;
    }

    private static void SetOrdersVolumes(Tool tool, ToolState toolState)
    {
        var balance = Math.Abs(toolState.Balance);

        toolState.LongOrderVolume = balance;
        toolState.ShortOrderVolume = balance;
        if (tool.Scripts.Length == 1 || toolState.Balance.Eq(0))
        {
            toolState.LongOrderVolume += toolState.LongVolume;
            toolState.ShortOrderVolume += toolState.ShortVolume;
        }
    }


    private async Task NormalizePositionAsync(Tool tool, ToolState toolState)
    {
        if (!toolState.IsBidding || !toolState.IsNormalPrice) return;

        var activeOrders = TradingSystem.Orders.ToArray().Where(x => x.Sender == "System" &&
            x.Seccode == tool.Security.Seccode && Connector.OrderIsActive(x));
        if (!activeOrders.Any() && !tool.UseNormalization) return;
        if (activeOrders.Count() > 1)
        {
            AddInfo(tool.Name + ": cancellation of several active system orders: " + activeOrders.Count());
            foreach (var order in activeOrders) await Connector.CancelOrderAsync(order);
            return;
        }

        var activeOrder = activeOrders.SingleOrDefault();
        if (CheckScriptOrderCloseToExecution(tool))
        {
            if (activeOrder != null) await Connector.CancelOrderAsync(activeOrder);
            return;
        }

        ArgumentNullException.ThrowIfNull(tool.Security.Bars);
        var security = tool.Security;
        var balance = toolState.Balance;

        double gap = Math.Abs(balance) / 14D;
        if (gap > 0.5) gap = 0.5;
        bool normalizeUp =
            balance.More(0) && balance + Math.Ceiling(balance * 0.04) + gap < toolState.LongRealVolume ||
            balance.Less(0) && -balance + Math.Ceiling(-balance * 0.04) + gap < toolState.ShortRealVolume;

        var volume = balance > toolState.LongVolume ?
            balance - toolState.LongVolume : -balance - toolState.ShortVolume;

        if (activeOrder != null)
        {
            if ((activeOrder.Side == "B") == balance.Less(0) &&
                (balance > toolState.LongVolume || -balance > toolState.ShortVolume))
            {
                if (activeOrder.Price.NotEq(security.Bars.Close[^2]) &&
                    ServerTime.Minute != 0 && ServerTime.Minute != 30 ||
                    activeOrder.Balance.NotEq(volume))
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security,
                        Connector.OrderTypeNM, security.Bars.Close[^2], volume, "Normalization", null, "NM");
                }
            }
            else if ((activeOrder.Side == "B") == balance.More(0) && normalizeUp)
            {
                volume = balance.More(0) ? toolState.LongVolume - balance : toolState.ShortVolume + balance;

                if (activeOrder.Price.NotEq(security.Bars.Close[^2]) &&
                    ServerTime.Minute != 0 && ServerTime.Minute != 30 ||
                    activeOrder.Balance.NotEq(volume) && volume > security.MinQty)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security,
                        Connector.OrderTypeNM, security.Bars.Close[^2], volume, "NormalizationUp", null, "NM");
                }
            }
            else await Connector.CancelOrderAsync(activeOrder);
        }
        else if (balance > toolState.LongVolume || -balance > toolState.ShortVolume)
        {
            await Connector.SendOrderAsync(security, Connector.OrderTypeNM,
                balance < 0, security.Bars.Close[^2], volume, "Normalization", null, "NM");
            WriteStateLog(tool, toolState, "NM");
        }
        else if (normalizeUp)
        {
            foreach (var script in tool.Scripts)
            {
                var lastExecuted = script.Orders.LastOrDefault(Connector.OrderIsExecuted);
                if (lastExecuted != null && (lastExecuted.ChangeTime.AddDays(4) > ServerTime ||
                    balance.More(0) == security.Bars.Close[^2] < lastExecuted.Price))
                {
                    volume = balance.More(0) ? toolState.LongVolume - balance : toolState.ShortVolume + balance;
                    if (volume < security.MinQty)
                    {
                        AddInfo(tool.Name + ": unable to normalize up: volume < security.MinQty",
                            TradingSystem.Settings.DisplaySpecialInfo);
                        return;
                    }

                    await Connector.SendOrderAsync(security, Connector.OrderTypeNM,
                        balance.More(0), security.Bars.Close[^2], volume, "NormalizationUp", null, "NM");

                    WriteStateLog(tool, toolState, "NM");
                    return;
                }
            }
        }
    }

    private async Task<bool> CheckPositionMatchingAsync(Tool tool, ToolState toolState)
    {
        if (!toolState.IsBidding || !toolState.IsNormalPrice) return true;
        if (CheckScriptOrderCloseToExecution(tool)) return true;
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);

        var longPos = PositionType.Long;
        var shortPos = PositionType.Short;
        var neutralPos = PositionType.Neutral;

        var security = tool.Security;
        var scripts = tool.Scripts;
        var balance = toolState.Balance;

        if (scripts.Length == 1)
        {
            var position = scripts[0].CurrentPosition;
            if (position == neutralPos && balance.NotEq(0) ||
                position == longPos && balance.LessEq(0) || position == shortPos && balance.MoreEq(0))
            {
                AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                if (!await CancelActiveOrdersAsync(tool)) return false;

                double volume;
                bool isBuy = position == neutralPos ? balance.Less(0) : position == longPos;
                if (position == longPos) volume = Math.Abs(balance) + toolState.LongVolume;
                else if (position == shortPos) volume = Math.Abs(balance) + toolState.ShortVolume;
                else volume = Math.Abs(balance);

                await Connector.SendOrderAsync(security, OrderType.Market,
                    isBuy, security.Bars.Close[^2], volume, "BringingIntoLine", null, "NM");
                return false;
            }
        }
        else if (scripts.Length == 2)
        {
            var position1 = scripts[0].CurrentPosition;
            var position2 = scripts[1].CurrentPosition;
            if (position1 == position2)
            {
                if (position1 == neutralPos && balance.NotEq(0) ||
                    position1 == longPos && balance.LessEq(0) || position1 == shortPos && balance.MoreEq(0))
                {
                    AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                    if (!await CancelActiveOrdersAsync(tool)) return false;

                    double volume;
                    bool isBuy = position1 == neutralPos ? balance.Less(0) : position1 == longPos;
                    if (position1 == longPos) volume = Math.Abs(balance) + toolState.LongVolume;
                    else if (position1 == shortPos) volume = Math.Abs(balance) + toolState.ShortVolume;
                    else volume = Math.Abs(balance);

                    await Connector.SendOrderAsync(security, OrderType.Market,
                        isBuy, security.Bars.Close[^2], volume, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else if (position1 == longPos && position2 == shortPos || position1 == shortPos && position2 == longPos)
            {
                if (balance.NotEq(0))
                {
                    AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                    if (!await CancelActiveOrdersAsync(tool)) return false;

                    await Connector.SendOrderAsync(security, OrderType.Market,
                        balance.Less(0), security.Bars.Close[^2], Math.Abs(balance), "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else // Одна из позиций Neutral
            {
                var position = position1 == neutralPos ? position2 : position1;
                if (position == longPos && balance.LessEq(0) || position == shortPos && balance.MoreEq(0))
                {
                    AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                    if (!await CancelActiveOrdersAsync(tool)) return false;

                    double vol = position == longPos ?
                        Math.Abs(balance) + toolState.LongVolume : Math.Abs(balance) + toolState.ShortVolume;

                    await Connector.SendOrderAsync(security, OrderType.Market,
                        position == longPos, security.Bars.Close[^2], vol, "BringingIntoLine", null, "NM");
                    return false;
                }
            }
        }
        else throw new ArgumentException("Unexpected number of scripts");
        return true;
    }

    private bool CheckScriptOrderCloseToExecution(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool.Security.Bars);
        return TradingSystem.Orders.ToArray().Any(order => order.Seccode == tool.Security.Seccode &&
            (order.Status is "active" or "NEW" or "PARTIALLY_FILLED") && order.Sender != "System" &&
            (order.Price.Eq(tool.Security.Bars.Close[^2]) ||
            order.Quantity.More(order.Balance) || order.Note == "PartEx"));
    }


    private async Task<bool> CancelActiveOrdersAsync(Tool tool)
    {
        var active = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == tool.Security.Seccode && Connector.OrderIsActive(x));
        if (!active.Any()) return true;

        AddInfo(tool.Name + ": cancellation of all active orders: " + active.Count());
        foreach (var order in active) await Connector.CancelOrderAsync(order);

        for (int i = 0; i < 20; i++)
        {
            if (!active.Where(Connector.OrderIsActive).Any()) return true;
            await Task.Delay(250);
        }

        AddInfo(tool.Name + ": failed to cancel all active orders in time");
        return false;
    }

    private async Task<bool> CancelUnknownOrdersAsync(Tool tool)
    {
        var unknown = TradingSystem.Orders.ToArray().Where(x => x.Sender == null &&
            x.Seccode == tool.Security.Seccode && Connector.OrderIsActive(x));
        if (!unknown.Any()) return true;

        AddInfo(tool.Name + ": cancellation of unknown orders: " + unknown.Count());
        foreach (var order in unknown) await Connector.CancelOrderAsync(order);

        for (int i = 0; i < 20; i++)
        {
            if (!unknown.Where(Connector.OrderIsActive).Any()) return true;
            await Task.Delay(250);
        }

        AddInfo(tool.Name + ": failed to cancel unknown orders in time");
        return false;
    }


    private void UpdateControlPanel(Tool tool, ToolState toolState)
    {
        Window.Dispatcher.Invoke(() =>
        {
            tool.BorderState.Background = toolState.ReadyToTrade ? Theme.Green : Theme.Orange;
            tool.BrushState = toolState.ReadyToTrade ? Theme.Green : Theme.Orange;

            tool.MainBlockInfo.Text =
            "\n" + Math.Round(toolState.LongReqs, 2) + "/" + Math.Round(toolState.ShortReqs, 2) +
            "\nVols " + Math.Round(toolState.LongVolume, 4) + "/" + Math.Round(toolState.ShortVolume, 4) +
            "\nOrderVols " + Math.Round(toolState.LongOrderVolume, 4) + "/" + Math.Round(toolState.ShortOrderVolume, 4) +
            "\nLastTr " + tool.Security.LastTrade.Time.TimeOfDay.ToString();

            tool.BlockInfo.Text = "\nBal/Real " + toolState.Balance + "/" + toolState.RealBalance +
            "\n" + toolState.LongRealVolume + "/" + toolState.ShortRealVolume +
            "\nSMA " + toolState.AveragePrice + "\n15ATR " + Math.Round(toolState.ATR * 15, tool.Security.TickPrecision);
        });
    }

    private void UpdateBarsColor(Tool tool, ToolState toolState)
    {
        ArgumentNullException.ThrowIfNull(tool.MainModel);
        ((OxyPlot.Series.CandleStickSeries)tool.MainModel.Series[0]).DecreasingColor =
            toolState.IsBidding && (!tool.ShowBasicSecurity ||
            tool.ShowBasicSecurity && tool.BasicSecurity?.LastTrade.Time.AddHours(2) > ServerTime) ?
            Theme.RedBar : Theme.FadedBar;
        tool.MainModel.InvalidatePlot(false);
    }

    private void WriteStateLog(Tool tool, ToolState toolState, string type = "Risks")
    {
        var data = ServerTime + ": /////////////// " + type +
            "\nStopTrading " + tool.StopTrading + "\nIsBidding " + toolState.IsBidding +
            "\nReadyToTrade " + toolState.ReadyToTrade + "\nPortfolio.Saldo " + TradingSystem.Portfolio.Saldo +
            "\nBalance " + toolState.Balance + "\nRealBalance " + toolState.RealBalance +
            "\nUseShiftBalance " + tool.UseShiftBalance + "\nBaseBalance " + tool.BaseBalance +
            "\nRiskrate " + tool.Security.RiskrateLong + "/" + tool.Security.RiskrateShort +
            "\nInitReq " + tool.Security.InitReqLong + "/" + tool.Security.InitReqShort +
            "\nShareOfFunds " + tool.ShareOfFunds + "\nRubReqs " + toolState.LongReqs + "/" + toolState.ShortReqs +
            "\nVols " + toolState.LongVolume + "/" + toolState.ShortVolume +
            "\nRealVols " + toolState.LongRealVolume + "/" + toolState.ShortRealVolume +
            "\nOrderVol " + toolState.LongOrderVolume + "/" + toolState.ShortOrderVolume +
            "\nMin/Max lots " + tool.MinQty + "/" + tool.MaxQty + "\n";
        try
        {
            File.AppendAllText("Logs/Tools/" + tool.Name + ".txt", data);
        }
        catch (Exception e)
        {
            AddInfo(tool.Name + ": logging exception: " + type + ": " + e.Message);
        }
    }

    private void UpdateViewTool(object sender, SelectionChangedEventArgs e)
    {
        var tool = (Tool)((ComboBox)sender).DataContext;
        Task.Run(() => UpdateView(tool, true));
    }

    private async void ChangeActivityToolAsync(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        await ChangeActivityAsync((Tool)button.DataContext);
        button.IsEnabled = true;
    }
}
