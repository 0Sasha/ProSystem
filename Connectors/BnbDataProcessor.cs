using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProSystem;

internal class BnbDataProcessor
{
    private readonly BnbConnector Connector;
    private readonly AddInformation AddInfo;
    private readonly TradingSystem TradingSystem;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    public BnbDataProcessor(BnbConnector connector, TradingSystem tradingSystem, AddInformation addInfo)
    {
        Connector = connector ?? throw new ArgumentNullException(nameof(connector));
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
    }

    public bool CheckServerTime(string data)
    {
        var curTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var serverTime = JsonDocument.Parse(data).RootElement.GetProperty("serverTime").GetInt64();
        var diff = curTime - serverTime;
        if (diff > -10000 && diff < 10000) return true;
        AddInfo("Server time is far from local time: S/L: " +
            DateTimeOffset.FromUnixTimeMilliseconds(serverTime) + " /" + DateTimeOffset.UtcNow, notify: true);
        return false;
    }

    public bool CheckAPIPermissions(string data)
    {
        var root = JsonDocument.Parse(data).RootElement;
        if (!root.GetProperty("ipRestrict").GetBoolean()) AddInfo("IP is not restricted", notify: true);

        if (root.GetProperty("enableWithdrawals").GetBoolean() ||
            root.GetProperty("enableInternalTransfer").GetBoolean() ||
            root.GetProperty("permitsUniversalTransfer").GetBoolean() ||
            root.GetProperty("enableVanillaOptions").GetBoolean() ||
            root.GetProperty("enableMargin").GetBoolean() ||
            root.GetProperty("enableSpotAndMarginTrading").GetBoolean())
        {
            AddInfo("Permissions are redundant", notify: true);
        }

        if (!root.GetProperty("enableReading").GetBoolean() || !root.GetProperty("enableFutures").GetBoolean())
        {
            AddInfo("Permissions are insufficient", notify: true);
            return false;
        }
        return true;
    }

    public void ProcessExchangeInfo(string data)
    {
        var doc = JsonDocument.Parse(data);
        var symbols = doc.RootElement.GetProperty("symbols");
        foreach (var symbol in symbols.EnumerateArray())
        {
            var code = symbol.GetProperty("symbol").GetString();
            var security = Connector.Securities.SingleOrDefault(s => s.Seccode == code);
            if (security == null)
            {
                security = new(code);
                Connector.Securities.Add(security);
            }
            security.TradingStatus = symbol.GetProperty("status").GetString();
            security.Market = symbol.GetProperty("underlyingType").GetString();

            var filters = symbol.GetProperty("filters");
            foreach (var filter in filters.EnumerateArray())
            {
                var type = filter.GetProperty("filterType").GetString();
                if (type == "PRICE_FILTER")
                {
                    var tickSize = filter.GetProperty("tickSize").GetString();
                    security.MinStep = double.Parse(tickSize, IC);

                    if (tickSize.StartsWith("0.")) security.Decimals = tickSize.Length - 2;
                    else if (tickSize == "1.0") security.Decimals = 0;
                    else AddInfo("Unknown tickSize", notify: true);
                }
                else if (type == "LOT_SIZE")
                    security.LotSize = double.Parse(filter.GetProperty("stepSize").GetString(), IC);
            }
        }
    }

    public void ProcessPortfolio(string data)
    {
        var doc = JsonDocument.Parse(data);
        var portfolio = TradingSystem.Portfolio;
        portfolio.InitReqs = double.Parse(doc.RootElement.GetProperty("totalInitialMargin").GetString(), IC);
        portfolio.MinReqs = double.Parse(doc.RootElement.GetProperty("totalMaintMargin").GetString(), IC);
        portfolio.Saldo = double.Parse(doc.RootElement.GetProperty("totalWalletBalance").GetString(), IC);
        portfolio.UnrealPL = double.Parse(doc.RootElement.GetProperty("totalUnrealizedProfit").GetString(), IC);

        // totalPositionInitialMargin, totalOpenOrderInitialMargin, totalCrossWalletBalance
        // totalCrossUnPnl, availableBalance

        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var code = a.GetProperty("asset").GetString();
            var saldo = double.Parse(a.GetProperty("walletBalance").GetString(), IC);
            if (saldo > 0.00000001 || portfolio.MoneyPositions.Any(p => p.Seccode == code))
            {
                var pos = portfolio.MoneyPositions.SingleOrDefault(p => p.Seccode == code);
                if (pos == null)
                {
                    pos = new(code, saldo);
                    portfolio.MoneyPositions.Add(pos);
                }
                else pos.Saldo = saldo;
                pos.PL = double.Parse(a.GetProperty("unrealizedProfit").GetString(), IC);
            }
        }

        foreach (var p in doc.RootElement.GetProperty("positions").EnumerateArray())
        {
            var code = p.GetProperty("symbol").GetString();
            var saldo = double.Parse(p.GetProperty("positionAmt").GetString(), IC);
            if (saldo > 0.00000001 || portfolio.Positions.Any(p => p.Seccode == code))
            {
                var pos = portfolio.Positions.SingleOrDefault(p => p.Seccode == code);
                if (pos == null)
                {
                    pos = new(code, saldo);
                    portfolio.Positions.Add(pos);
                }
                else pos.Saldo = saldo;
                pos.PL = double.Parse(p.GetProperty("unrealizedProfit").GetString(), IC);
            }
        }
    }

    public void ProcessBars(string bars, TimeFrame barsTF, Security security, int baseTF)
    {
        List<DateTime> dateTime = new();
        List<double> open = new(), high = new(),
            low = new(), close = new(), volume = new();

        foreach (var bar in JsonDocument.Parse(bars).RootElement.EnumerateArray())
        {
            dateTime.Add(DateTimeOffset.FromUnixTimeMilliseconds(bar[0].GetInt64()).UtcDateTime);
            open.Add(double.Parse(bar[1].GetString(), IC));
            high.Add(double.Parse(bar[2].GetString(), IC));
            low.Add(double.Parse(bar[3].GetString(), IC));
            close.Add(double.Parse(bar[4].GetString(), IC));
            volume.Add(double.Parse(bar[5].GetString(), IC));
            _ = bar[10];
        }

        if (dateTime.Count > 2)
        {
            var newBars = new Bars(dateTime.ToArray(), open.ToArray(), high.ToArray(),
                low.ToArray(), close.ToArray(), volume.ToArray(), barsTF.Seconds / 60);
            security.UpdateBars(newBars, baseTF);
        }
    }

    public void ProcessData(string data)
    {
        if (data.EndsWith('\0'))
            data = data.Replace("\0", string.Empty);

        var root = JsonDocument.Parse(data).RootElement;
        if (root.TryGetProperty("e", out JsonElement et))
        {
            var eventType = et.GetString();
            if (eventType == "kline") ProcessBar(root);
            else
            {
                var info = root.GetRawText();
                AddInfo("Unknown eventType: " + eventType + "\n" + info);
            }
        }
        else
        {
            var res = root.GetProperty("result");
            var text = res.GetRawText();
            if (text != "null") AddInfo(text);
        }
    }

    private void ProcessBar(JsonElement root)
    {
        var code = root.GetProperty("s").GetString();
        var tool = TradingSystem.Tools.ToArray().SingleOrDefault(t =>
            t.Security.Seccode == code || t.BasicSecurity?.Seccode == code);

        if (tool == null)
        {
            if (code == "BTCUSDT") return;
            throw new ArgumentException("Symbol is not suitable");
        }
        var security = tool.Security.Seccode == code ? tool.Security : tool.BasicSecurity;

        var kline = root.GetProperty("k");
        var tfID = kline.GetProperty("i").GetString();
        var tf = Connector.TimeFrames.Single(t => t.ID == tfID).Seconds / 60;
        if (security.Bars.TF != tf) throw new ArgumentException("TF is not suitable");
        security.LastTrade = new(DateTime.Now);

        var startTime = DateTimeOffset.FromUnixTimeMilliseconds(kline.GetProperty("t").GetInt64()).UtcDateTime;
        var open = double.Parse(kline.GetProperty("o").GetString(), IC);
        var high = double.Parse(kline.GetProperty("h").GetString(), IC);
        var low = double.Parse(kline.GetProperty("l").GetString(), IC);
        var close = double.Parse(kline.GetProperty("c").GetString(), IC);
        var volume = double.Parse(kline.GetProperty("v").GetString(), IC);
        var barIsClosed = kline.GetProperty("x").GetBoolean();

        if (startTime == security.Bars.DateTime[^1])
        {
            security.Bars.Open[^1] = open;
            security.Bars.High[^1] = high;
            security.Bars.Low[^1] = low;
            security.Bars.Close[^1] = close;
            security.Bars.Volume[^1] = volume;
        }
        else if (startTime > security.Bars.DateTime[^1])
        {
            security.LastTrDT = DateTime.Now.AddSeconds(10);
            tool.TimeNextRecalc = DateTime.Now.AddSeconds(30);

            security.Bars.DateTime = security.Bars.DateTime.Concat(new[] { startTime }).ToArray();
            security.Bars.Open = security.Bars.Open.Concat(new[] { open }).ToArray();
            security.Bars.High = security.Bars.High.Concat(new[] { high }).ToArray();
            security.Bars.Low = security.Bars.Low.Concat(new[] { low }).ToArray();
            security.Bars.Close = security.Bars.Close.Concat(new[] { close }).ToArray();
            security.Bars.Volume = security.Bars.Volume.Concat(new[] { volume }).ToArray();

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

                await TradingSystem.ToolManager.CalculateAsync(tool);
                tool.MainModel.InvalidatePlot(true);
            });
        }
        else throw new ArgumentException("StartTime is not suitable");
    }
}
