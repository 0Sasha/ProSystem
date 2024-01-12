using System.Globalization;

namespace ProSystem;

public abstract class DataProcessor(TradingSystem tradingSystem, AddInformation addInfo)
{
    protected readonly AddInformation AddInfo = addInfo;
    protected readonly TradingSystem TradingSystem = tradingSystem;
    protected readonly CultureInfo IC = CultureInfo.InvariantCulture;
    protected readonly StringComparison OC = StringComparison.Ordinal;

    private DateTime ServerTime { get => TradingSystem.Connector.ServerTime; }

    protected void UpdateBars(Security security, Bars newBars, int baseTF)
    {
        if (newBars.TF <= 0) throw new ArgumentException("TF <= 0", nameof(newBars));
        if (baseTF <= 0) throw new ArgumentException("baseTF <= 0", nameof(baseTF));
        if (newBars.DateTime.Length < 2) throw new ArgumentException("DateTime.Length < 2", nameof(newBars));

        var dateTime = newBars.DateTime;
        var open = newBars.Open;
        var high = newBars.High;
        var low = newBars.Low;
        var close = newBars.Close;
        var volume = newBars.Volume;

        if (security.SourceBars == null || security.SourceBars.DateTime == null ||
            security.SourceBars.TF != newBars.TF) security.SourceBars = newBars;
        else if (dateTime[^1] >= security.SourceBars.DateTime[^1]) // Полученные данные свежее исходных
        {
            // Поиск первого общего бара
            int y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[0]);
            if (y == -1) y = Array.FindIndex(security.SourceBars.DateTime, x => x == dateTime[1]);

            if (y > -1) // Есть общие бары
            {
                security.SourceBars.DateTime = [.. security.SourceBars.DateTime[..y], .. dateTime];
                security.SourceBars.Open = [.. security.SourceBars.Open[..y], .. open];
                security.SourceBars.High = [.. security.SourceBars.High[..y], .. high];
                security.SourceBars.Low = [.. security.SourceBars.Low[..y], .. low];
                security.SourceBars.Close = [.. security.SourceBars.Close[..y], .. close];
                security.SourceBars.Volume = [.. security.SourceBars.Volume[..y], .. volume];
            }
            else security.SourceBars = newBars; // Отсутствует общий бар
        }
        else if (dateTime[^1] < security.SourceBars.DateTime[0]) // Полученные данные глубже исходных
        {
            if (dateTime[^1].AddDays(7) < security.SourceBars.DateTime[0])
                AddInfo("Received bars are too deep: " + security.Seccode, true, true);

            security.SourceBars.DateTime = [.. dateTime, .. security.SourceBars.DateTime];
            security.SourceBars.Open = [.. open, .. security.SourceBars.Open];
            security.SourceBars.High = [.. high, .. security.SourceBars.High];
            security.SourceBars.Low = [.. low, .. security.SourceBars.Low];
            security.SourceBars.Close = [.. close, .. security.SourceBars.Close];
            security.SourceBars.Volume = [.. volume, .. security.SourceBars.Volume];
        }
        else if (dateTime[0] < security.SourceBars.DateTime[0])
        {
            int count = Array.FindIndex(dateTime, d => d == security.SourceBars.DateTime[0]);
            security.SourceBars.DateTime = dateTime.Take(count).Concat(security.SourceBars.DateTime).ToArray();
            security.SourceBars.Open = open.Take(count).Concat(security.SourceBars.Open).ToArray();
            security.SourceBars.High = high.Take(count).Concat(security.SourceBars.High).ToArray();
            security.SourceBars.Low = low.Take(count).Concat(security.SourceBars.Low).ToArray();
            security.SourceBars.Close = close.Take(count).Concat(security.SourceBars.Close).ToArray();
            security.SourceBars.Volume = volume.Take(count).Concat(security.SourceBars.Volume).ToArray();
        }
        else return;

        if (security.SourceBars.TF == baseTF) security.Bars = security.SourceBars;
        else Task.Run(() => security.Bars = security.SourceBars.Compress(baseTF));
    }

    protected void UpdateLastBar(Trade lastTrade)
    {
        var tool = TradingSystem.Tools
            .Single(x => x.Security.Seccode == lastTrade.Seccode || x.BasicSecurity?.Seccode == lastTrade.Seccode);
        
        var security = tool.BasicSecurity;
        if (security == null || tool.Security.Seccode == lastTrade.Seccode) security = tool.Security;
        ArgumentNullException.ThrowIfNull(security.Bars);
        
        var prevTradeTime = security.LastTrade.Time;
        security.LastTrade = lastTrade;

        var bars = security.Bars;
        if (lastTrade.Time < bars.DateTime[^1].AddMinutes(bars.TF))
        {
            bars.Close[^1] = lastTrade.Price;
            if (lastTrade.Price > bars.High[^1]) bars.High[^1] = lastTrade.Price;
            else if (lastTrade.Price < bars.Low[^1]) bars.Low[^1] = lastTrade.Price;
            bars.Volume[^1] += lastTrade.Quantity;
        }
        else if (ServerTime > prevTradeTime)
        {
            var startTime = ServerTime.Date == bars.DateTime[^1].Date ?
                bars.DateTime[^1].AddMinutes(bars.TF) : ServerTime.Date.AddHours(ServerTime.Hour);
            var price = lastTrade.Price;
            AddNewBar(tool, security, true, true, startTime, price, price, price, price, lastTrade.Quantity);
        }
    }

    protected void UpdateLastBar(Trade lastTrade, DateTime startTime,
        double open, double high, double low, double close, double volume)
    {
        var tool = TradingSystem.Tools
            .Single(x => x.Security.Seccode == lastTrade.Seccode || x.BasicSecurity?.Seccode == lastTrade.Seccode);

        var security = tool.BasicSecurity;
        if (security == null || tool.Security.Seccode == lastTrade.Seccode) security = tool.Security;
        ArgumentNullException.ThrowIfNull(security.Bars);
        security.LastTrade = lastTrade;

        var bars = security.Bars;
        if (startTime == bars.DateTime[^1])
        {
            bars.Open[^1] = open;
            bars.High[^1] = high;
            bars.Low[^1] = low;
            bars.Close[^1] = close;
            bars.Volume[^1] = volume;
        }
        else if (startTime > bars.DateTime[^1])
            AddNewBar(tool, security, false, false, startTime, open, high, low, close, volume);
        else throw new ArgumentException("StartTime < Bars.DateTime[^1]");
    }

    private void AddNewBar(Tool tool, Security security, bool delay, bool requestBars,
        DateTime startTime, double open, double high, double low, double close, double volume)
    {
        ArgumentNullException.ThrowIfNull(security.Bars);

        security.LastTrade.Time = ServerTime.AddSeconds(10);
        tool.NextRecalc = ServerTime.AddSeconds(30);

        security.Bars.DateTime = [.. security.Bars.DateTime, .. new[] { startTime }];
        security.Bars.Open = [.. security.Bars.Open, .. new[] { open }];
        security.Bars.High = [.. security.Bars.High, .. new[] { high }];
        security.Bars.Low = [.. security.Bars.Low, .. new[] { low }];
        security.Bars.Close = [.. security.Bars.Close, .. new[] { close }];
        security.Bars.Volume = [.. security.Bars.Volume, .. new[] { volume }];

        Task.Run(() => RecalculateAsync(tool, security, delay, requestBars));
    }

    private async Task RecalculateAsync(Tool tool, Security security, bool delay, bool requestBars)
    {
        if (delay) await Task.Delay(250);

        var lastExecuted = TradingSystem.Orders.ToArray()
            .LastOrDefault(x => x.Seccode == tool.Security.Seccode && TradingSystem.Connector.OrderIsExecuted(x));
        if (lastExecuted != null && lastExecuted.ChangeTime.AddSeconds(3) > ServerTime)
        {
            AddInfo(tool.Name + ": an order is executed during the bar opening. Waiting.", false);
            await Task.Delay(2000);
        }
        else if (tool.Security.Seccode == security.Seccode)
        {
            var active = TradingSystem.Orders.ToArray()
                .Where(x => x.Seccode == security.Seccode && TradingSystem.Connector.OrderIsActive(x)).ToArray();
            if (active.Any(x => Math.Abs(x.Price - security.LastTrade.Price) < 0.000001))
            {
                AddInfo(tool.Name + ": active order price equals bar opening. Waiting.", false);
                await Task.Delay(2000);
            }
        }

        await TradingSystem.ToolManager.CalculateAsync(tool);
        tool.MainModel?.InvalidatePlot(true);
        if (requestBars) await TradingSystem.Connector.RequestBarsAsync(tool);
    }
}
