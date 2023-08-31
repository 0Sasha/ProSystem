using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public ToolManager(Window window, TradingSystem tradingSystem,
        IScriptManager scriptManager, AddInformation addInfo)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
        ScriptManager = scriptManager ?? throw new ArgumentNullException(nameof(scriptManager));
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        Connector = TradingSystem.Connector ?? throw new ArgumentException("Connector is null");
    }

    public void Initialize(Tool tool)
    {
        if (tool.BaseTF < 1) tool.BaseTF = 30;
        tool.Controller ??= Plot.GetController();
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
                    await RequestBarsAsync(tool);
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
                    await RequestBarsAsync(tool);
                }
                catch (Exception ex)
                {
                    AddInfo("ChangeActivity: " + ex.Message);
                    AddInfo(ex.StackTrace, false);
                    return;
                }
            }
        }

        tool.Active = !tool.Active;
        tool.BrushState = tool.Active ? (tool.StopTrading ? Theme.Orange : Theme.Green) : Theme.Red;
        var btnContent = tool.Active ? "Deactivate tool" : "Activate tool";

        Window.Dispatcher.Invoke(() => (tool.Tab.Content as Grid).Children.OfType<Grid>()
            .Last().Children.OfType<Grid>().First().Children.OfType<Button>().First().Content = btnContent);
        tool.NotifyChange();
    }

    public async Task CalculateAsync(Tool tool)
    {
        while (Interlocked.Exchange(ref tool.IsOccupied, 1) != 0)
        {
            AddInfo(tool.Name + ": Calculate: method is occupied by another thread", false);
            await Task.Delay(500);
        }

        while (DateTime.Now.AddSeconds(-3) < tool.TimeLastRecalc)
        {
            AddInfo(tool.Name + ": Calculate: waiting for data from server", false);
            await Task.Delay(1000);
        }

        try
        {
            if (await CheckToolAsync(tool)) await CalculateToolAsync(tool);
            AddInfo("Scripts executed: " + tool.Name, false);
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
            tool.TimeLastRecalc = DateTime.Now;
            tool.TimeNextRecalc = DateTime.Now.AddSeconds(TradingSystem.Settings.RecalcInterval / 2);
            Interlocked.Exchange(ref tool.IsOccupied, 0);
        }
    }

    public async Task RequestBarsAsync(Tool tool)
    {
        if (Connector.TimeFrames == null || Connector.TimeFrames.Count == 0)
        {
            AddInfo("RequestBars: Connector.TimeFrames is empty", notify: true);
            return;
        }

        var tf = Connector.TimeFrames.Last(x => x.Seconds / 60 <= tool.BaseTF);
        int count = 25;

        var security = tool.Security;
        var basicSec = tool.BasicSecurity;
        if (basicSec != null)
        {
            if (basicSec.SourceBars == null || basicSec.SourceBars.Close.Length < 500 ||
                basicSec.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
                tool.BaseTF != basicSec.SourceBars.TF && tool.BaseTF != basicSec.Bars.TF) count = 5000;
            await Connector.OrderHistoricalDataAsync(basicSec, tf, count);
        }

        count = 25;
        if (security.SourceBars == null || security.SourceBars.Close.Length < 500 ||
            security.SourceBars.DateTime[^1].AddHours(6) < DateTime.Now ||
            tool.BaseTF != security.SourceBars.TF && tool.BaseTF != security.Bars.TF) count = 5000;
        await Connector.OrderHistoricalDataAsync(security, tf, count);
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
            await RequestBarsAsync(tool);
            await Task.Delay(500);
            if (security.Bars != null) UpdateView(tool, true);
        }
    }

    public void UpdateBars(Tool tool, bool updateBasicSecurity)
    {
        if (updateBasicSecurity)
        {
            var basic = tool.BasicSecurity;
            if (basic.SourceBars.TF == tool.BaseTF) basic.Bars = basic.SourceBars;
            else basic.Bars = basic.SourceBars.Compress(tool.BaseTF);
        }
        else
        {
            var sec = tool.Security;
            if (sec.SourceBars.TF == tool.BaseTF) sec.Bars = sec.SourceBars;
            else sec.Bars = sec.SourceBars.Compress(tool.BaseTF);
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

    public void UpdateLastTrade(Tool tool, Trade lastTrade)
    {
        var security = tool.Security.Seccode == lastTrade.Seccode ? tool.Security : tool.BasicSecurity;
        security.LastTrade = lastTrade;
        var bars = security.Bars;

        if (lastTrade.DateTime < bars.DateTime[^1].AddMinutes(bars.TF))
        {
            bars.Close[^1] = lastTrade.Price;
            if (lastTrade.Price > bars.High[^1]) bars.High[^1] = lastTrade.Price;
            else if (lastTrade.Price < bars.Low[^1]) bars.Low[^1] = lastTrade.Price;
            bars.Volume[^1] += lastTrade.Quantity;
        }
        else if (DateTime.Now > security.LastTrDT)
        {
            security.LastTrDT = DateTime.Now.AddSeconds(10);
            tool.TimeNextRecalc = DateTime.Now.AddSeconds(30);

            if (DateTime.Now.Date == bars.DateTime[^1].Date) bars.DateTime =
                    bars.DateTime.Concat(new DateTime[] { bars.DateTime[^1].AddMinutes(bars.TF) }).ToArray();
            else bars.DateTime =
                    bars.DateTime.Concat(new DateTime[] { DateTime.Now.Date.AddHours(DateTime.Now.Hour) }).ToArray();
            bars.Open = bars.Open.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.High = bars.High.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.Low = bars.Low.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.Close = bars.Close.Concat(new double[] { lastTrade.Price }).ToArray();
            bars.Volume = bars.Volume.Concat(new double[] { lastTrade.Quantity }).ToArray();

            Task.Run(async () =>
            {
                await Task.Delay(250);
                var lastExecuted = TradingSystem.Orders.ToArray()
                    .LastOrDefault(x => x.Seccode == tool.Security.Seccode && x.Status == "matched");

                if (lastExecuted != null && lastExecuted.DateTime.AddSeconds(3) > DateTime.Now)
                {
                    AddInfo(tool.Name + ": an order is executed during the bar opening. Waiting.", false);
                    await Task.Delay(2000);
                }
                else if (tool.Security.Seccode == security.Seccode)
                {
                    var active = TradingSystem.Orders.ToArray()
                        .Where(x => x.Seccode == security.Seccode && (x.Status is "active" or "watching")).ToArray();
                    if (active.Any(x => Math.Abs(x.Price - security.LastTrade.Price) < 0.00001))
                    {
                        AddInfo(tool.Name + ": active order price equals bar opening. Waiting.", false);
                        await Task.Delay(2000);
                    }
                }

                await CalculateAsync(tool);
                tool.MainModel.InvalidatePlot(true);
                await RequestBarsAsync(tool);
            });
        }
    }


    private async Task<bool> CheckToolAsync(Tool tool)
    {
        var security = tool.Security;
        var basicSecurity = tool.BasicSecurity;
        if (security.LastTrade == null || security.LastTrade.DateTime < DateTime.Now.AddDays(-5))
        {
            AddInfo(tool.Name + ": last trade is not actual. Subscribing.", notify: true);
            await Connector.SubscribeToTradesAsync(security);
            return false;
        }
        else if (security.Bars == null || basicSecurity != null && basicSecurity.Bars == null)
        {
            AddInfo(tool.Name + ": there is no bars. Request.", notify: true);
            await RequestBarsAsync(tool);
            return false;
        }
        else if (basicSecurity == null && security.Bars.Close.Length < 200 ||
            basicSecurity != null && (security.Bars.Close.Length < 200 || basicSecurity.Bars.Close.Length < 200))
        {
            var counts = security.Bars.Close.Length.ToString();
            if (basicSecurity != null) counts += "/" + basicSecurity.Bars.Close.Length;
            AddInfo(tool.Name + ": not enough bars: " + counts + " Request.", notify: true);
            await RequestBarsAsync(tool);
            return false;
        }
        else if (tool.Scripts.Length > 2)
        {
            AddInfo(tool.Name + ": unexpected number of scripts: " + tool.Scripts.Length, notify: true);
            return false;
        }
        return true;
    }

    private async Task CalculateToolAsync(Tool tool)
    {
        await WaitCertaintyAsync(tool);
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

    private async Task WaitCertaintyAsync(Tool tool)
    {
        var undefined = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == tool.Security.Seccode && (x.Status is "forwarding" or "inactive"));
        if (undefined.Any())
        {
            AddInfo(tool.Name + ": uncertain order status: " + undefined.First().Status);
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(300);
                if (!undefined.Where(x => x.Status is "forwarding" or "inactive").Any()) return;
            }
            AddInfo(tool.Name + ": failed to get certain order status");
        }

        var lastTrade = TradingSystem.Trades.ToArray().LastOrDefault(x => x.Seccode == tool.Security.Seccode);
        if (lastTrade != null && lastTrade.DateTime.AddSeconds(2) > DateTime.Now) await Task.Delay(1500);
    }

    private void IdentifyOrdersAndTrades(Tool tool)
    {
        ScriptManager.IdentifyOrdersAndTrades(tool);

        var systemOrders = TradingSystem.SystemOrders;
        var unknownOrders = TradingSystem.Orders.ToArray()
            .Where(x => x.Sender == null && x.Seccode == tool.Security.Seccode);
        foreach (var unknownOrder in unknownOrders)
        {
            int i = Array.FindIndex(systemOrders.ToArray(), x => x.TrID == unknownOrder.TrID);
            if (i > -1)
            {
                unknownOrder.Sender = systemOrders[i].Sender;
                unknownOrder.Signal = systemOrders[i].Signal;
                unknownOrder.Note = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => systemOrders[i] = unknownOrder);
            }
        }

        var unknownTrades = TradingSystem.Trades.ToArray()
            .Where(x => x.SenderOrder == null && x.Seccode == tool.Security.Seccode);
        foreach (var unknownTrade in unknownTrades)
        {
            int i = Array.FindIndex(systemOrders.ToArray(), x => x.OrderNo == unknownTrade.OrderNo);
            if (i > -1)
            {
                unknownTrade.SenderOrder = systemOrders[i].Sender;
                unknownTrade.SignalOrder = systemOrders[i].Signal;
                unknownTrade.NoteOrder = systemOrders[i].Note;
                Window.Dispatcher.Invoke(() => TradingSystem.SystemTrades.Add(unknownTrade));
            }
        }
    }


    private ToolState CalculateToolState(Tool tool)
    {
        var average = Math.Round(tool.Security.Bars.Close.TakeLast(30).Average(), tool.Security.Decimals);
        var atr = Indicators.ATR(tool.Security.Bars.High, tool.Security.Bars.Low, tool.Security.Bars.Close, 50);

        var readyToTrade = !tool.StopTrading &&
            TradingSystem.PortfolioManager.CheckEquity() && Connector.CheckRequirements(tool.Security);
        var toolState = new ToolState(readyToTrade, IsLogging(tool), Connector.SecurityIsBidding(tool.Security))
        {
            IsNormalPrice = Math.Abs(average - tool.Security.Bars.Close[^1]) < atr[^2] * 15,
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
        if (DateTime.Now.Second >= 30 && (DateTime.Now.Minute is 0 or 29 or 30 or 59))
        {
            try
            {
                if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
                if (!Directory.Exists("Logs/LogsTools")) Directory.CreateDirectory("Logs/LogsTools");

                var path = "Logs/LogsTools/" + tool.Name + ".txt";
                if (!File.Exists(path)) File.Create(path).Close();

                var data = DateTime.Now.ToString(IC) + ": /////////////////// RECOUNT SCRIPTS" +
                    "\nLastTrade " + sec.LastTrade.Price.ToString(IC) +
                    "\nDateLastTrade " + sec.LastTrade.DateTime.ToString(IC) + "\n";

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
        if (tool.TradeShare)
        {
            var settings = TradingSystem.Settings;
            var optShare = saldo / 100 * tool.ShareOfFunds;
            if (optShare > maxShare)
            {
                AddInfo(tool.Name + ": ShareOfFunds exceeds risk level", settings.DisplaySpecialInfo, true);
                optShare = maxShare;
            }

            int longVol = (int)Math.Floor(optShare / toolState.LongReqs);
            if (longVol < tool.MinNumberOfLots)
            {
                AddInfo(tool.Name + ": LongVolume < MinNumberOfLots", settings.DisplaySpecialInfo, true);
                longVol = tool.MinNumberOfLots;
            }
            if (longVol > tool.MaxNumberOfLots)
            {
                AddInfo(tool.Name + ": LongVolume > MaxNumberOfLots", settings.DisplaySpecialInfo, true);
                longVol = tool.MaxNumberOfLots;
            }
            if (longVol * toolState.LongReqs > maxShare)
            {
                AddInfo(tool.Name + ": LongVolume exceeds risk level", settings.DisplaySpecialInfo, true);
                longVol = (int)Math.Floor(optShare / toolState.LongReqs);
            }

            int shortVol = (int)Math.Floor(optShare / toolState.ShortReqs);
            if (shortVol < tool.MinNumberOfLots)
            {
                AddInfo(tool.Name + ": shortVol < MinNumberOfLots", settings.DisplaySpecialInfo, true);
                shortVol = tool.MinNumberOfLots;
            }
            if (shortVol > tool.MaxNumberOfLots)
            {
                AddInfo(tool.Name + ": shortVol > MaxNumberOfLots", settings.DisplaySpecialInfo, true);
                shortVol = tool.MaxNumberOfLots;
            }
            if (shortVol * toolState.ShortReqs > maxShare)
            {
                AddInfo(tool.Name + ": ShortVolume exceeds risk level", settings.DisplaySpecialInfo, true);
                shortVol = (int)Math.Floor(optShare / toolState.ShortReqs);
            }

            toolState.LongVolume = longVol;
            toolState.ShortVolume = shortVol;

            toolState.LongRealVolume = Math.Round(saldo * 0.01 * tool.ShareOfFunds / toolState.LongReqs, 2);
            toolState.ShortRealVolume = Math.Round(saldo * 0.01 * tool.ShareOfFunds / toolState.ShortReqs, 2);
        }
        else
        {
            if (tool.NumberOfLots * Math.Max(toolState.LongReqs, toolState.ShortReqs) > maxShare)
            {
                AddInfo(tool.Name + ": NumberOfLots превышает допустимый объём риска.", notify: true);
                toolState.LongVolume = (int)Math.Floor(maxShare / toolState.LongReqs);
                toolState.ShortVolume = (int)Math.Floor(maxShare / toolState.ShortReqs);
            }
            else
            {
                toolState.LongVolume = tool.NumberOfLots;
                toolState.ShortVolume = tool.NumberOfLots;
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
        if (position != null) toolState.Balance = (int)position.Saldo;

        toolState.RealBalance = toolState.Balance;
        if (tool.UseShiftBalance) toolState.Balance -= tool.BaseBalance;

        if (Math.Abs(toolState.Balance) >
            Math.Max(Math.Max(toolState.LongVolume, toolState.ShortVolume), 1) *
            TradingSystem.Settings.TolerancePosition)
        {
            if (DateTime.Today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                toolState.ReadyToTrade = false;
                if (tool.TriggerPosition == DateTime.MinValue)
                {
                    AddInfo(tool.Name +
                        ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                    tool.TriggerPosition = DateTime.Now.AddHours(12);
                }
            }
            else
            {
                AddInfo(tool.Name +
                    ": Объём текущей позиции за пределами допустимого отклонения. Ожидание.", notify: true);
                if (tool.TriggerPosition == DateTime.MinValue)
                {
                    tool.TriggerPosition = DateTime.Now.AddHours(4);
                    toolState.ReadyToTrade = false;
                }
                else if (DateTime.Now < tool.TriggerPosition) toolState.ReadyToTrade = false;
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
        if (tool.Scripts.Length == 1 || toolState.Balance == 0)
        {
            toolState.LongOrderVolume += toolState.LongVolume;
            toolState.ShortOrderVolume += toolState.ShortVolume;
        }
    }


    private async Task NormalizePositionAsync(Tool tool, ToolState toolState)
    {
        if (!toolState.IsBidding || !toolState.IsNormalPrice) return;

        var activeOrders = TradingSystem.Orders.ToArray().Where(x => x.Sender == "System" &&
            x.Seccode == tool.Security.Seccode && (x.Status is "active" or "watching"));
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

        var security = tool.Security;
        var balance = toolState.Balance;

        double gap = Math.Abs(balance) / 14D;
        if (gap > 0.5) gap = 0.5;
        bool normalizeUp =
            balance > 0 && balance + Math.Ceiling(balance * 0.04) + gap < toolState.LongRealVolume ||
            balance < 0 && -balance + Math.Ceiling(-balance * 0.04) + gap < toolState.ShortRealVolume;

        int volume = balance > toolState.LongVolume ?
            balance - toolState.LongVolume : -balance - toolState.ShortVolume;

        if (activeOrder != null)
        {
            if ((activeOrder.BuySell == "B") == balance < 0 &&
                (balance > toolState.LongVolume || -balance > toolState.ShortVolume))
            {
                if (Math.Abs(activeOrder.Price - security.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || activeOrder.Balance != volume)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security,
                        OrderType.Limit, security.Bars.Close[^2], volume, "Normalization", null, "NM");
                }
            }
            else if ((activeOrder.BuySell == "B") == balance > 0 && normalizeUp)
            {
                volume = balance > 0 ? toolState.LongVolume - balance : toolState.ShortVolume + balance;

                if (Math.Abs(activeOrder.Price - security.Bars.Close[^2]) > 0.00001 &&
                    DateTime.Now.Minute != 0 && DateTime.Now.Minute != 30 || activeOrder.Balance != volume)
                {
                    await Connector.ReplaceOrderAsync(activeOrder, security,
                        OrderType.Limit, security.Bars.Close[^2], volume, "NormalizationUp", null, "NM");
                }
            }
            else await Connector.CancelOrderAsync(activeOrder);
        }
        else if (balance > toolState.LongVolume || -balance > toolState.ShortVolume)
        {
            await Connector.SendOrderAsync(security, OrderType.Limit,
                balance < 0, security.Bars.Close[^2], volume, "Normalization", null, "NM");
            WriteStateLog(tool, toolState, "NM");
        }
        else if (normalizeUp)
        {
            foreach (var script in tool.Scripts)
            {
                var lastExecuted = script.Orders.LastOrDefault(x => x.Status == "matched");
                if (lastExecuted != null && (lastExecuted.DateTime.AddDays(4) > DateTime.Now ||
                    balance > 0 == security.Bars.Close[^2] < lastExecuted.Price))
                {
                    volume = balance > 0 ? toolState.LongVolume - balance : toolState.ShortVolume + balance;

                    await Connector.SendOrderAsync(security, OrderType.Limit,
                        balance > 0, security.Bars.Close[^2], volume, "NormalizationUp", null, "NM");

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

        var longPos = PositionType.Long;
        var shortPos = PositionType.Short;
        var neutralPos = PositionType.Neutral;

        var security = tool.Security;
        var scripts = tool.Scripts;
        var balance = toolState.Balance;

        if (scripts.Length == 1)
        {
            var position = scripts[0].CurrentPosition;
            if (position == neutralPos && balance != 0 ||
                position == longPos && balance <= 0 || position == shortPos && balance >= 0)
            {
                AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                if (!await CancelActiveOrdersAsync(tool)) return false;

                int volume;
                bool isBuy = position == neutralPos ? balance < 0 : position == longPos;
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
                if (position1 == neutralPos && balance != 0 ||
                    position1 == longPos && balance <= 0 || position1 == shortPos && balance >= 0)
                {
                    AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                    if (!await CancelActiveOrdersAsync(tool)) return false;

                    int volume;
                    bool isBuy = position1 == neutralPos ? balance < 0 : position1 == longPos;
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
                if (balance != 0)
                {
                    AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                    if (!await CancelActiveOrdersAsync(tool)) return false;

                    await Connector.SendOrderAsync(security, OrderType.Market,
                        balance < 0, security.Bars.Close[^2], Math.Abs(balance), "BringingIntoLine", null, "NM");
                    return false;
                }
            }
            else // Одна из позиций Neutral
            {
                var position = position1 == neutralPos ? position2 : position1;
                if (position == longPos && balance <= 0 || position == shortPos && balance >= 0)
                {
                    AddInfo(tool.Name + ": position mismatch. Normalization by market.", notify: true);
                    if (!await CancelActiveOrdersAsync(tool)) return false;

                    int vol = position == longPos ?
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
        return TradingSystem.Orders.ToArray().Any(order =>
            order.Seccode == tool.Security.Seccode && order.Status == "active" && order.Sender != "System" &&
            (Math.Abs(order.Price - tool.Security.Bars.Close[^2]) < 0.00001 ||
            order.Quantity - order.Balance > 0.00001 || order.Note == "PartEx"));
    }


    private async Task<bool> CancelActiveOrdersAsync(Tool tool)
    {
        var active = TradingSystem.Orders.ToArray()
            .Where(x => x.Seccode == tool.Security.Seccode && (x.Status is "active" or "watching"));
        if (!active.Any()) return true;

        AddInfo(tool.Name + ": cancellation of all active orders: " + active.Count());
        foreach (var order in active) await Connector.CancelOrderAsync(order);

        for (int i = 0; i < 20; i++)
        {
            if (!active.Where(x => x.Status is "active" or "watching").Any()) return true;
            await Task.Delay(250);
        }

        AddInfo(tool.Name + ": failed to cancel all active orders in time");
        return false;
    }

    private async Task<bool> CancelUnknownOrdersAsync(Tool tool)
    {
        var unknown = TradingSystem.Orders.ToArray().Where(x => x.Sender == null &&
            x.Seccode == tool.Security.Seccode && (x.Status is "active" or "watching"));
        if (!unknown.Any()) return true;

        AddInfo(tool.Name + ": cancellation of unknown orders: " + unknown.Count());
        foreach (var order in unknown) await Connector.CancelOrderAsync(order);

        for (int i = 0; i < 20; i++)
        {
            if (!unknown.Where(x => x.Status is "active" or "watching").Any()) return true;
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
            "\nReq " + Math.Round(toolState.LongReqs) + "/" + Math.Round(toolState.ShortReqs) +
            "\nVols " + toolState.LongVolume + "/" + toolState.ShortVolume +
            "\nOrderVols " + toolState.LongOrderVolume + "/" + toolState.ShortOrderVolume +
            "\nLastTr " + tool.Security.LastTrade.DateTime.TimeOfDay.ToString();

            tool.BlockInfo.Text = "\nBal/Real " + toolState.Balance + "/" + toolState.RealBalance +
            "\nRealV " + toolState.LongRealVolume + "/" + toolState.ShortRealVolume +
            "\nSMA " + toolState.AveragePrice + "\n15ATR " + Math.Round(toolState.ATR * 15, tool.Security.Decimals);
        });
    }

    private static void UpdateBarsColor(Tool tool, ToolState toolState)
    {
        (tool.MainModel.Series[0] as OxyPlot.Series.CandleStickSeries).DecreasingColor = toolState.IsBidding &&
            (!tool.ShowBasicSecurity ||
            tool.ShowBasicSecurity && tool.BasicSecurity.LastTrade.DateTime.AddHours(2) > DateTime.Now) ?
            Theme.RedBar : Theme.FadedBar;
        tool.MainModel.InvalidatePlot(false);
    }

    private void WriteStateLog(Tool tool, ToolState toolState, string type = "Risks")
    {
        var data = DateTime.Now + ": /////////////// " + type +
            "\nStopTrading " + tool.StopTrading + "\nIsBidding " + toolState.IsBidding +
            "\nReadyToTrade " + toolState.ReadyToTrade + "\nPortfolio.Saldo " + TradingSystem.Portfolio.Saldo +
            "\nBalance " + toolState.Balance + "\nRealBalance " + toolState.RealBalance +
            "\nUseShiftBalance " + tool.UseShiftBalance + "\nBaseBalance " + tool.BaseBalance +
            "\nReserate " + tool.Security.ReserateLong + "/" + tool.Security.ReserateShort +
            "\nInitReq " + tool.Security.InitReqLong + "/" + tool.Security.InitReqShort +
            "\nShareOfFunds " + tool.ShareOfFunds + "\nRubReqs " + toolState.LongReqs + "/" + toolState.ShortReqs +
            "\nVols " + toolState.LongVolume + "/" + toolState.ShortVolume +
            "\nRealVols " + toolState.LongRealVolume + "/" + toolState.ShortRealVolume +
            "\nOrderVol " + toolState.LongOrderVolume + "/" + toolState.ShortOrderVolume +
            "\nMin/Max lots " + tool.MinNumberOfLots + "/" + tool.MaxNumberOfLots + "\n";
        try
        {
            File.AppendAllText("Logs/LogsTools/" + tool.Name + ".txt", data);
        }
        catch (Exception e)
        {
            AddInfo(tool.Name + ": logging exception: " + type + ": " + e.Message);
        }
    }

    private void UpdateViewTool(object sender, SelectionChangedEventArgs e)
    {
        var tool = (sender as ComboBox).DataContext as Tool;
        Task.Run(() => UpdateView(tool, true));
    }

    private async void ChangeActivityToolAsync(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        button.IsEnabled = false;
        await ChangeActivityAsync(button.DataContext as Tool);
        button.IsEnabled = true;
    }
}
