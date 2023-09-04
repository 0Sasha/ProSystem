using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ProSystem;

abstract class DataProcessor
{
    protected readonly AddInformation AddInfo;
    protected readonly TradingSystem TradingSystem;
    protected readonly CultureInfo IC = CultureInfo.InvariantCulture;
    protected readonly StringComparison OC = StringComparison.Ordinal;

    public DataProcessor(TradingSystem tradingSystem, AddInformation addInfo)
    {
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
    }

    protected static void UpdateBars(Security security, Bars newBars, int baseTF)
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
                security.SourceBars.DateTime = security.SourceBars.DateTime[..y].Concat(dateTime).ToArray();
                security.SourceBars.Open = security.SourceBars.Open[..y].Concat(open).ToArray();
                security.SourceBars.High = security.SourceBars.High[..y].Concat(high).ToArray();
                security.SourceBars.Low = security.SourceBars.Low[..y].Concat(low).ToArray();
                security.SourceBars.Close = security.SourceBars.Close[..y].Concat(close).ToArray();
                security.SourceBars.Volume = security.SourceBars.Volume[..y].Concat(volume).ToArray();
            }
            else security.SourceBars = newBars; // Отсутствует общий бар
        }
        else if (dateTime[^1] < security.SourceBars.DateTime[0]) // Полученные данные глубже исходных
        {
            if (dateTime[^1].AddDays(5) < security.SourceBars.DateTime[0])
                throw new ArgumentException("Received bars are too deep: " + security.Seccode);

            security.SourceBars.DateTime = dateTime.Concat(security.SourceBars.DateTime).ToArray();
            security.SourceBars.Open = open.Concat(security.SourceBars.Open).ToArray();
            security.SourceBars.High = high.Concat(security.SourceBars.High).ToArray();
            security.SourceBars.Low = low.Concat(security.SourceBars.Low).ToArray();
            security.SourceBars.Close = close.Concat(security.SourceBars.Close).ToArray();
            security.SourceBars.Volume = volume.Concat(security.SourceBars.Volume).ToArray();
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
            var startTime = DateTime.Now.Date == bars.DateTime[^1].Date ?
                bars.DateTime[^1].AddMinutes(bars.TF) : DateTime.Now.Date.AddHours(DateTime.Now.Hour);
            var price = lastTrade.Price;
            AddNewBar(tool, security, true, true, startTime, price, price, price, price, lastTrade.Quantity);
        }
    }

    protected void UpdateLastBar(Trade lastTrade, DateTime startTime,
        double open, double high, double low, double close, double volume)
    {
        var tool = TradingSystem.Tools
            .Single(x => x.Security.Seccode == lastTrade.Seccode || x.BasicSecurity?.Seccode == lastTrade.Seccode);
        var security = tool.Security.Seccode == lastTrade.Seccode ? tool.Security : tool.BasicSecurity;
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
        security.LastTrDT = DateTime.Now.AddSeconds(10);
        tool.TimeNextRecalc = DateTime.Now.AddSeconds(30);

        security.Bars.DateTime = security.Bars.DateTime.Concat(new[] { startTime }).ToArray();
        security.Bars.Open = security.Bars.Open.Concat(new[] { open }).ToArray();
        security.Bars.High = security.Bars.High.Concat(new[] { high }).ToArray();
        security.Bars.Low = security.Bars.Low.Concat(new[] { low }).ToArray();
        security.Bars.Close = security.Bars.Close.Concat(new[] { close }).ToArray();
        security.Bars.Volume = security.Bars.Volume.Concat(new[] { volume }).ToArray();

        Task.Run(() => RecalculateAsync(tool, security, delay, requestBars));
    }

    private async Task RecalculateAsync(Tool tool, Security security, bool delay, bool requestBars)
    {
        if (delay) await Task.Delay(250);

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
            if (active.Any(x => Math.Abs(x.Price - security.LastTrade.Price) < 0.00000001))
            {
                AddInfo(tool.Name + ": active order price equals bar opening. Waiting.", false);
                await Task.Delay(2000);
            }
        }

        await TradingSystem.ToolManager.CalculateAsync(tool);
        tool.MainModel.InvalidatePlot(true);
        if (requestBars) await TradingSystem.ToolManager.RequestBarsAsync(tool);
    }
}
