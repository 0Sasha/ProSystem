using System.Text.Json;

namespace ProSystem;

internal class BnbDataProcessor : DataProcessor
{
    private readonly BnbConnector Connector;

    public BnbDataProcessor(BnbConnector connector, TradingSystem tradingSystem, AddInformation addInfo) :
        base(tradingSystem, addInfo) => Connector = connector;

    public bool CheckServerTime(JsonElement root)
    {
        var curTime = Connector.UnixTime;
        var serverTime = root.GetLong("serverTime");

        var diff = curTime - serverTime;
        if (diff > -10000 && diff < 10000) return true;

        AddInfo("Server time is far from local time: S/L: " +
            DateTimeOffset.FromUnixTimeMilliseconds(serverTime) + " / " + Connector.ServerTime, notify: true);
        return false;
    }

    public bool CheckAPIPermissions(JsonElement root)
    {
        if (!root.GetBool("ipRestrict")) AddInfo("IP is not restricted", notify: true);

        if (root.GetBool("enableWithdrawals") || root.GetBool("enableInternalTransfer") ||
            root.GetBool("permitsUniversalTransfer") || root.GetBool("enableVanillaOptions") ||
            root.GetBool("enableMargin") || root.GetBool("enableSpotAndMarginTrading"))
        {
            AddInfo("Permissions are redundant", notify: true);
        }

        if (!root.GetBool("enableReading") || !root.GetBool("enableFutures"))
        {
            AddInfo("Permissions are insufficient", notify: true);
            return false;
        }
        return true;
    }

    public void ProcessExchangeInfo(JsonElement root)
    {
        foreach (var symbol in root.GetProperty("symbols").EnumerateArray())
        {
            var code = symbol.GetString("symbol");
            var security = Connector.Securities.SingleOrDefault(s => s.Seccode == code);
            security ??= TradingSystem.Tools.SingleOrDefault(t => t.Security.Seccode == code)?.Security;
            security ??= TradingSystem.Tools.SingleOrDefault(t => t.BasicSecurity?.Seccode == code)?.BasicSecurity;
            if (security == null)
            {
                security = new(code);
                Connector.Securities.Add(security);
            }
            security.TradingStatus = symbol.GetString("status");
            security.Market = symbol.GetString("underlyingType");
            security.Currency = symbol.GetString("marginAsset");

            var filters = symbol.GetProperty("filters");
            foreach (var filter in filters.EnumerateArray())
            {
                var type = filter.GetString("filterType");
                if (type == "PRICE_FILTER")
                {
                    security.MinPrice = filter.GetDouble("minPrice");
                    security.MaxPrice = filter.GetDouble("maxPrice");
                    security.TickSize = filter.GetDouble("tickSize");
                    security.TickCost = security.TickSize;

                    var tickSize = filter.GetString("tickSize");
                    if (tickSize.StartsWith("0.")) security.TickPrecision = tickSize.Length - 2;
                    else if (tickSize is "1.0" or "1") security.TickPrecision = 0;
                    else AddInfo("Unknown tickSize: " + tickSize, notify: true);
                }
                else if (type == "LOT_SIZE")
                {
                    security.LotSize = filter.GetDouble("stepSize");

                    var stepSize = filter.GetString("stepSize");
                    if (stepSize.StartsWith("0.")) security.LotPrecision = stepSize.Length - 2;
                    else if (stepSize is "1.0" or "1") security.LotPrecision = 0;
                    else AddInfo("Unknown stepSize: " + stepSize, notify: true);
                }
                else if (type == "MIN_NOTIONAL") security.Notional = filter.GetDouble("notional");
                else if (type == "MARKET_LOT_SIZE")
                {
                    var l = filter.GetDouble("stepSize");
                    if (Math.Abs(security.LotSize - l) > 0.000001)
                        AddInfo(code + ": MARKET_LOT_SIZE != LOT_SIZE", notify: true);
                }
            }
        }
    }

    public void ProcessPortfolio(JsonElement root)
    {
        var portfolio = TradingSystem.Portfolio;
        portfolio.InitReqs = root.GetDouble("totalInitialMargin");
        portfolio.MinReqs = root.GetDouble("totalMaintMargin");
        portfolio.Saldo = root.GetDouble("totalMarginBalance");
        portfolio.UnrealPL = root.GetDouble("totalUnrealizedProfit");
        portfolio.Free = root.GetDouble("availableBalance");

        if (Connector.DeepLog)
            AddInfo("Portfolio InitReqs/MinReqs: " + portfolio.InitReqs + "/" + portfolio.MinReqs, false);
        // totalMarginBalance = totalWalletBalance + totalUnrealizedProfit
        // totalPositionInitialMargin, totalOpenOrderInitialMargin, totalCrossWalletBalance
        // totalCrossUnPnl

        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            var code = a.GetString("asset");
            var saldo = a.GetDouble("marginBalance");
            if (saldo > 0.000001 || portfolio.MoneyPositions.Any(p => p.Seccode == code))
            {
                if (Connector.DeepLog) AddInfo("Asset: " + a.GetRawText(), false);

                var pos = portfolio.MoneyPositions.SingleOrDefault(p => p.Seccode == code);
                if (pos == null)
                {
                    pos = new(code) { Saldo = saldo };
                    portfolio.MoneyPositions.Add(pos);
                }
                else pos.Saldo = saldo;
                pos.UnrealPL = a.GetDouble("unrealizedProfit");
                pos.InitReqs = a.GetDouble("initialMargin");
                pos.MinReqs = a.GetDouble("maintMargin");
            }
        }

        foreach (var p in root.GetProperty("positions").EnumerateArray())
        {
            var code = p.GetString("symbol");
            var saldo = p.GetDouble("positionAmt");
            if (Math.Abs(saldo) > 0.000001 || portfolio.Positions.Any(p => p.Seccode == code))
            {
                if (Connector.DeepLog) AddInfo("Position: " + p.GetRawText(), false);

                var pos = portfolio.Positions.SingleOrDefault(p => p.Seccode == code);
                if (pos == null)
                {
                    pos = new(code) { Saldo = saldo };
                    portfolio.Positions.Add(pos);
                }
                else pos.Saldo = saldo;
                pos.UnrealPL = p.GetDouble("unrealizedProfit");
                pos.InitReqs = p.GetDouble("initialMargin");
                pos.MinReqs = p.GetDouble("maintMargin");
            }
        }
    }

    public void ProcessOrders(JsonElement root)
    {
        foreach (var jsonOrder in root.EnumerateArray())
        {
            var newOrder = ConstructOrder(jsonOrder, false);
            UpdateOrder(newOrder);
        }
    }

    public void ProcessTrades(JsonElement root)
    {
        foreach (var json in root.EnumerateArray())
        {
            var id = json.GetLong("id");
            var orderId = json.GetLong("orderId");
            var symbol = json.GetString("symbol");
            var side = json.GetString("side")[..1];
            var time = json.GetDateTime("time");
            var price = json.GetDouble("price");
            var quantity = json.GetDouble("qty");
            var commission = json.GetDouble("commission");

            var trade = TradingSystem.Trades.SingleOrDefault(o => o.Id == id);
            if (trade == null)
            {
                trade = new(id, orderId, symbol, side, time, price, quantity, commission);
                TradingSystem.Window.Dispatcher.Invoke(() => TradingSystem.Trades.Add(trade));
                AddInfo("New trade: " + trade.Seccode + "/" + trade.Side + "/" +
                    trade.Price + "/" + trade.Quantity, TradingSystem.Settings.DisplayNewTrades);
            }
            else
            {
                trade.OrderId = orderId;
                trade.Seccode = symbol;
                trade.Side = side;
                trade.Time = time;
                trade.Price = price;
                trade.Quantity = quantity;
                trade.Commission = commission;
            }
        }
    }

    public void ProcessBars(JsonElement root, TimeFrame barsTF, Security security, int baseTF)
    {
        List<DateTime> dateTime = [];
        List<double> open = [], high = [], low = [], close = [], volume = [];

        foreach (var bar in root.EnumerateArray())
        {
            dateTime.Add(bar[0].GetDateTimeFromLong());
            open.Add(bar[1].GetDoubleFromString());
            high.Add(bar[2].GetDoubleFromString());
            low.Add(bar[3].GetDoubleFromString());
            close.Add(bar[4].GetDoubleFromString());
            volume.Add(bar[5].GetDoubleFromString());
            _ = bar[10];
        }

        if (dateTime.Count > 1)
        {
            var newBars = new Bars([.. dateTime], [.. open], [.. high],
                [.. low], [.. close], [.. volume], barsTF.Minutes);
            UpdateBars(security, newBars, baseTF);
        }
    }


    public void ProcessData(string data)
    {
        if (data.EndsWith('\0'))
            data = data.Replace("\0", string.Empty);

        var root = JsonDocument.Parse(data).RootElement;
        if (root.TryGetProperty("e", out JsonElement e))
        {
            var eventType = e.GetString();
            if (eventType == "kline") ProcessBar(root);
            else if (eventType == "ORDER_TRADE_UPDATE") ProcessOrder(root);
            else if (eventType == "ACCOUNT_UPDATE") ProcessPositions(root);
            else if (eventType == "ACCOUNT_CONFIG_UPDATE") ProcessAccountConfig(root);
            //else if (eventType == "MARGIN_CALL") { }
            else AddInfo("Unknown eventType: " + eventType + "\n" + root.GetRawText());
        }
        else if (root.TryGetProperty("stream", out JsonElement _) &&
            root.GetProperty("data").GetString("e") == "listenKeyExpired")
        {
            _ = Connector.SocketManager.DisconnectAsync();
        }
        else if (root.TryGetProperty("msg", out _) ||
            root.TryGetProperty("result", out JsonElement result) && result.GetRawText() != "null" &&
            result.EnumerateArray().First().GetString() != Connector.ListenKey)
        {
            var res = root.GetRawText();
            if (res != "null") AddInfo("ProcessData: " + res);
        }
    }

    private void ProcessBar(JsonElement root)
    {
        //if (Connector.DeepLog) AddInfo("ProcessBar: " + root.GetRawText(), false);

        var code = root.GetString("s");
        var security = TradingSystem.Tools.SingleOrDefault(t => t.Security.Seccode == code)?.Security;
        security ??= TradingSystem.Tools.SingleOrDefault(t => t.BasicSecurity?.Seccode == code)?.BasicSecurity;
        if (security == null)
        {
            AddInfo("ProcessBar: unexpected security", true, true);
            return;
        }

        var kline = root.GetProperty("k");
        var tf = Connector.TimeFrames.Single(t => t.ID == kline.GetString("i")).Minutes;
        if (security.Bars?.TF != tf) throw new ArgumentException("ProcessBar: TF is not suitable");
        //var eventTime = DateTimeOffset.FromUnixTimeMilliseconds(root.GetLong("E")).UtcDateTime;

        var startTime = kline.GetDateTime("t");
        var open = kline.GetDouble("o");
        var high = kline.GetDouble("h");
        var low = kline.GetDouble("l");
        var close = kline.GetDouble("c");
        var volume = kline.GetDouble("v");
        //var barIsClosed = kline.GetBool("x");

        var lastTrade = new Trade(code, Connector.ServerTime, close, 0);
        UpdateLastBar(lastTrade, startTime, open, high, low, close, volume);
    }

    private void ProcessOrder(JsonElement root)
    {
        root = root.GetProperty("o");
        var newOrder = ConstructOrder(root, true);
        UpdateOrder(newOrder);

        var tradeId = root.GetLong("t");
        if (tradeId != 0)
        {
            Task.Run(async () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Thread.Sleep(1000);
                    if (await Connector.GetTradesAsync(newOrder.Seccode, true)) return;
                }
                AddInfo("ProcessOrder: failed to order trades of " + newOrder.Seccode, true, true);
            });
        }
    }

    private void ProcessPositions(JsonElement root)
    {
        if (Connector.DeepLog) AddInfo("ProcessPositions: " + root.GetRawText(), false);
        Task.Run(() => Connector.OrderPortfolioInfoAsync(TradingSystem.Portfolio));

        foreach (var p in root.GetProperty("a").GetProperty("P").EnumerateArray())
        {
            var seccode = p.GetString("s");
            var pos = TradingSystem.Portfolio.Positions.SingleOrDefault(x => x.Seccode == seccode);
            if (pos == null)
            {
                pos = new(seccode);
                TradingSystem.Portfolio.Positions.Add(pos);
            }
            pos.Saldo = p.GetDouble("pa");
            pos.UnrealPL = p.GetDouble("up");
            pos.PL = p.GetDouble("cr");
        }
    }

    private void ProcessAccountConfig(JsonElement root)
    {
        if (Connector.DeepLog) AddInfo("ProcessAccountConfig: " + root.GetRawText(), false);

        if (root.TryGetProperty("ac", out JsonElement lev))
            AddInfo(lev.GetString("s") + " leverage: " + lev.GetInt("l"));
        else if (root.TryGetProperty("ai", out JsonElement mul))
            AddInfo("Multi-Assets Mode: " + mul.GetBool("j"));
        else AddInfo("ProcessAccountConfig: unknown data", true, true);
    }


    private Order ConstructOrder(JsonElement root, bool isEvent)
    {
        if (Connector.DeepLog) AddInfo("Order: " + root.GetRawText(), false);

        var id = root.GetLong(isEvent ? "i" : "orderId");
        var symbol = root.GetString(isEvent ? "s" : "symbol");
        var status = root.GetString(isEvent ? "X" : "status");
        var side = root.GetString(isEvent ? "S" : "side")[..1];
        var newOrder = new Order(id, symbol, status, Connector.ServerTime, side)
        {
            Quantity = root.GetDouble(isEvent ? "q" : "origQty"),
            Type = root.GetString(isEvent ? "o" : "type"),
            Time = root.GetDateTime(isEvent ? "T" : "time")
        };

        newOrder.Price = root.GetDouble(isEvent ? (newOrder.Type is "LIMIT" or "MARKET" ? "p" : "sp") : "price");
        newOrder.Balance = newOrder.Quantity - root.GetDouble(isEvent ? "z" : "executedQty");

        if (newOrder.Type == "MARKET") newOrder.InitType = OrderType.Market;
        else newOrder.InitType = newOrder.Type == "LIMIT" ? OrderType.Limit : OrderType.Conditional;

        return newOrder;
    }

    private void UpdateOrder(Order newOrder)
    {
        var oldOrder = TradingSystem.Orders.ToArray().SingleOrDefault(o => o.Id == newOrder.Id);
        if (oldOrder == null) TradingSystem.Window.Dispatcher.Invoke(() => TradingSystem.Orders.Add(newOrder));
        else
        {
            oldOrder.Seccode = newOrder.Seccode;
            oldOrder.Quantity = newOrder.Quantity;
            oldOrder.Balance = newOrder.Balance;
            oldOrder.Price = newOrder.Price;
            oldOrder.Side = newOrder.Side;
            oldOrder.Time = newOrder.Time;
            oldOrder.Type = newOrder.Type;
            if (oldOrder.Status != newOrder.Status)
            {
                oldOrder.Status = newOrder.Status;
                oldOrder.ChangeTime = newOrder.ChangeTime;
            }
        }
    }
}
