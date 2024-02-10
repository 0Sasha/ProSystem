using System.ComponentModel;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using System.Windows.Threading;

namespace ProSystem;

internal class BnbConnector : Connector
{
    private string? apiKey;
    private byte[]? apiSecret;
    private readonly Timer ListenKeyHolder;
    private readonly BnbDataProcessor DataProcessor;
    private readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(100) };
    private readonly TimeFrame[] BnbTimeFrames =
    [
        new("1m", 1),
        new("5m", 5),
        new("15m", 15),
        new("30m", 30),
        new("1h", 60),
        new("2h", 120),
        new("4h", 240),
        new("6h", 360),
        new("8h", 480),
        new("12h", 720),
        new("1d", 1440)
    ];

    private const string BaseUrl = "https://api.binance.com";
    private const string BaseFuturesUrl = "https://fapi.binance.com";
    private const string BaseFuturesStreamUrl = "wss://fstream.binance.com";
    private const string RecvWindow = "8000";

    public readonly WebSocketManager SocketManager;

    public bool DeepLog { get => TradingSystem.Settings.DeepLog; }
    public string? ListenKey { get; private set; }

    public BnbConnector(TradingSystem tradingSystem, AddInformation addInfo) : base(tradingSystem, addInfo)
    {
        TimeFrames.AddRange(BnbTimeFrames);
        Markets.Add(new("COIN", "COIN"));
        DataProcessor = new(this, tradingSystem, addInfo);
        SocketManager = new(BaseFuturesStreamUrl, DataProcessor.ProcessData, addInfo);
        SocketManager.PropertyChanged += SocketManagerConnectionChanged;
        ListenKeyHolder = new(async (o) =>
        {
            if (Connection == ConnectionState.Connected) await UpdateListenKey();
        }, null, 0, 1800000);
    }

    public override async Task<bool> ConnectAsync(string login, SecureString password)
    {
        apiKey = login ?? throw new ArgumentNullException(nameof(login));
        if (password == null || password.Length == 0) throw new ArgumentNullException(nameof(password));

        var s = Marshal.SecureStringToGlobalAllocUnicode(password);
        apiSecret = Encoding.UTF8.GetBytes(Marshal.PtrToStringUni(s) ?? throw new Exception("s is null"));
        Marshal.ZeroFreeGlobalAllocUnicode(s);

        Connection = ConnectionState.Connecting;
        if (!await CheckServerTimeAsync() || !await CheckAPIPermissionsAsync())
        {
            ServerAvailable = false;
            Connection = ConnectionState.Disconnected;
            return false;
        }

        Securities.Clear();
        if (!await GetExchangeInfoAsync())
        {
            ServerAvailable = false;
            Connection = ConnectionState.Disconnected;
            return false;
        }

        if (!SocketManager.Connected)
        {
            if (!await UpdateListenKey() || !await SocketManager.ConnectAsync("/ws/" + ListenKey))
            {
                ServerAvailable = false;
                Connection = ConnectionState.Disconnected;
                return false;
            }
            await SocketManager.SendAsync("{ \"method\": \"LIST_SUBSCRIPTIONS\",\r\n\"id\": 3 }");
        }
        else
        {
            Connection = ConnectionState.Connected;
            AddInfo(Connection.ToString(), !ReconnectTime);
        }
        return true;
    }

    public override async Task<bool> DisconnectAsync()
    {
        if (SocketManager.Connected)
        {
            Connection = ConnectionState.Disconnecting;
            await SocketManager.DisconnectAsync();
        }
        else
        {
            Connection = ConnectionState.Disconnected;
            AddInfo(Connection.ToString(), !ReconnectTime);
        }
        return true;
    }

    public override async Task<bool> SendOrderAsync(Security security, OrderType type,
        bool isBuy, double price, double quantity, string signal, Script? sender = null, string? note = null)
    {
        var senderName = sender != null ? sender.Name : "System";
        if (price < 0.000001)
            throw new ArgumentOutOfRangeException(nameof(price), "Price <= 0. Sender: " + senderName);
        if (quantity < 0.000001)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity <= 0: " + senderName + "/" + quantity);
        price = Math.Round(price, security.TickPrecision);
        quantity = Math.Round(quantity, security.LotPrecision);

        var query = new Dictionary<string, string>()
        {
            { "symbol", security.Seccode },
            { "side", isBuy ? "BUY" : "SELL" },
            { "quantity", quantity.ToString(IC) },
            { "workingType", "CONTRACT_PRICE" },
            { "newOrderRespType", "RESULT" },
        };
        if (type == OrderType.Limit)
        {
            query["type"] = "LIMIT";
            query["price"] = price.ToString(IC);
            query["timeInForce"] = "GTC";
        }
        else if (type == OrderType.Market) query["type"] = "MARKET";
        else if (type == OrderType.Conditional)
        {
            query["type"] = "STOP_MARKET";
            query["stopPrice"] = price.ToString(IC);
            query["timeInForce"] = "GTC";
        }
        else throw new ArgumentException("Unexpected OrderType");

        if (signal == "Normalization") query["reduceOnly"] = "true";

        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/order", true, HttpMethod.Post, query);
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (!res.IsSuccessStatusCode)
        {
            AddInfo("SendOrder: order of " + senderName + " is not sent: " + root.GetString("msg"));
            return false;
        }

        var origQty = root.GetDouble("origQty");
        var order = new Order(root.GetLong("orderId"), root.GetString("symbol"),
            root.GetString("status"), ServerTime, root.GetString("side")[..1])
        {
            Quantity = origQty,
            Balance = origQty - root.GetDouble("executedQty"),
            Price = root.GetDouble("price"),
            Side = root.GetString("side")[..1],
            InitType = type,
            Type = root.GetString("type"),
            Time = root.GetDateTime("updateTime"),
            Signal = signal,
            Sender = senderName,
            Note = note
        };

        await TradingSystem.Window.Dispatcher.InvokeAsync(() =>
        {
            if (sender != null) sender.Orders.Add(order);
            else TradingSystem.SystemOrders.Add(order);
        }, DispatcherPriority.Send);

        AddInfo("SendOrder: order is sent: " + senderName + "/" + order.Seccode + "/" +
            order.Side + "/" + order.Price + "/" + order.Quantity + "/" + order.Id,
            TradingSystem.Settings.DisplaySentOrders);

        _ = Task.Run(() =>
        {
            Thread.Sleep(3000);
            TradingSystem.Window.Dispatcher.Invoke(() =>
            {
                if (!TradingSystem.Orders.ToArray().Any(o => o.Id == order.Id))
                    TradingSystem.Orders.Add(order);
            }, DispatcherPriority.Send);
        });
        return true;
    }

    public override async Task<bool> ReplaceOrderAsync(Order activeOrder, Security security,
        OrderType type, double price, double quantity, string signal, Script? sender = null, string? note = null)
    {
        var senderName = sender != null ? sender.Name : "System";
        AddInfo("ReplaceOrder: replacement by " + senderName, false);
        if (await CancelOrderAsync(activeOrder))
            return await SendOrderAsync(security, type, activeOrder.Side == "B", price, quantity, signal, sender, note);
        return false;
    }

    public override async Task<bool> CancelOrderAsync(Order activeOrder)
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/order", true, HttpMethod.Delete,
            new()
            {
                { "symbol", activeOrder.Seccode },
                { "orderId", activeOrder.Id.ToString(IC) }
            });
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (!res.IsSuccessStatusCode)
        {
            AddInfo("CancelOrder: " + activeOrder.Sender + ": error: " + root.GetString("msg"));
            return false;
        }

        if (root.GetString("symbol") == activeOrder.Seccode && root.GetLong("orderId") == activeOrder.Id)
        {
            var status = root.GetString("status");
            if (activeOrder.Status != status)
            {
                activeOrder.Status = status;
                activeOrder.ChangeTime = ServerTime;
            }
            return status == "CANCELED";
        }

        AddInfo("CancelOrder: " + activeOrder.Sender + ": unexpected response: " + content);
        return false;
    }

    public override async Task<bool> SubscribeToTradesAsync(Security security) =>
        await SubUnsubAsync(security, true);

    public override async Task<bool> UnsubscribeFromTradesAsync(Security security) =>
        await SubUnsubAsync(security, false);

    private async Task<bool> SubUnsubAsync(Security security, bool subscribe)
    {
        if (SocketManager.Connected)
        {
            var s = subscribe ? "SUBSCRIBE" : "UNSUBSCRIBE";
            await SocketManager.SendAsync("{\r\n\"method\": \"" + s + "\",\r\n\"params\":\r\n[\r\n\"" +
                security.Seccode.ToLower() + "@kline_30m\"],\r\n\"id\": 1\r\n}");
            return true;
        }

        AddInfo("SubUnsubToBars: WebSocketState is not open");
        return false;
    }

    protected override async Task<bool> OrderHistoricalDataAsync(Security security, TimeFrame tf, int count, int baseTF)
    {
        if (count > 1500)
        {
            var timeStep = (long)TimeSpan.FromMinutes(tf.Minutes * 980).TotalMilliseconds;
            var endTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
            var startTime = endTime - timeStep;
            while (count > 0)
            {
                using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/klines", false, HttpMethod.Get,
                    new()
                    {
                        { "symbol", security.Seccode },
                        { "interval", tf.ID },
                        { "startTime", startTime.ToString() },
                        { "endTime", endTime.ToString() },
                        { "limit", "1500" }
                    });
                if (!await ProcessBarsResponseAsync(res, tf, security, baseTF)) return false;
                count -= 980;
                endTime = startTime;
                startTime = endTime - timeStep;
            }
            return true;
        }
        else
        {
            using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/klines", false, HttpMethod.Get,
                new()
                {
                    { "symbol", security.Seccode },
                    { "interval", tf.ID },
                    { "limit", count.ToString() }
                });
            return await ProcessBarsResponseAsync(res, tf, security, baseTF);
        }
    }

    private async Task<bool> ProcessBarsResponseAsync(
        HttpResponseMessage? res, TimeFrame barsTF, Security security, int baseTF)
    {
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        //if (deepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (res.IsSuccessStatusCode)
        {
            _ = Task.Run(() => DataProcessor.ProcessBars(root, barsTF, security, baseTF));
            return true;
        }

        AddInfo("OrderHistoricalData: " + root.GetString("msg"));
        return false;
    }

    public override async Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio)
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v2/account", true, HttpMethod.Get);
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        //if (DeepLog) AddInfo(await res.Content.ReadAsStringAsync(), false);

        var root = JsonDocument.Parse(content).RootElement;
        if (res.IsSuccessStatusCode)
        {
            _ = Task.Run(() => DataProcessor.ProcessPortfolio(root));
            return true;
        }

        AddInfo("OrderPortfolioInfo: " + root.GetString("msg"));
        return false;
    }

    public override async Task<bool> PrepareForTradingAsync()
    {
        if (!await SetOneWayPositionMode())
        {
            AddInfo("PrepareForTrading: failed to set OneWay position mode");
            return false;
        }

        if (!await GetAllOpenOrdersAsync())
        {
            AddInfo("PrepareForTrading: failed to get open orders");
            return false;
        }

        foreach (var tool in TradingSystem.Tools)
        {
            if (!await ChangeInitialLeverage(tool.Security.Seccode, TradingSystem.Settings.InitialLeverage))
            {
                AddInfo("PrepareForTrading: failed to set initial leverage");
                return false;
            }

            if (!await SetCrossedMarginType(tool.Security.Seccode))
            {
                AddInfo("PrepareForTrading: failed to set crossed margin type");
                return false;
            }

            if (!await GetOrdersAsync(tool.Security.Seccode) || !await GetTradesAsync(tool.Security.Seccode))
            {
                AddInfo("PrepareForTrading: failed to get orders or trades");
                return false;
            }
        }

        return true;
    }



    protected override bool CheckRequirements(Security security)
    {
        if (security.TickSize < 0.000001 || security.TickCost < 0.000001 || security.TickPrecision < -0.000001 ||
            security.LotSize < 0.000001 || security.LotPrecision < -0.000001 || security.Notional < 0.000001)
        {
            AddInfo("CheckRequirements: " + security.Seccode + ": properties are incorrect", notify: true);
            return false;
        }

        ArgumentNullException.ThrowIfNull(security.Bars);
        security.MinQty = Math.Round(security.Notional /
            security.Bars.Close[^1], security.LotPrecision, MidpointRounding.ToZero) + security.LotSize;

        security.InitReqLong = security.Bars.Close[^1] * security.LotSize;
        security.InitReqShort = security.Bars.Close[^1] * security.LotSize;
        return true;
    }

    public override bool OrderIsActive(Order order) => order.Status is "NEW" or "PARTIALLY_FILLED";

    public override bool OrderIsExecuted(Order order) => order.Status is "FILLED";

    public override bool OrderIsTriggered(Order order) =>
        order.Type is "LIMIT" or "MARKET" && order.Status is "NEW" or "PARTIALLY_FILLED";


    private async Task<bool> CheckAPIPermissionsAsync()
    {
        using var res = await SendRequestAsync(BaseUrl + "/sapi/v1/account/apiRestrictions", true, HttpMethod.Get);
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (res.IsSuccessStatusCode) return DataProcessor.CheckAPIPermissions(root);

        AddInfo("CheckAPIPermissions: " + root.GetString("msg"));
        return false;
    }

    private async Task<bool> CheckServerTimeAsync()
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/time", false, HttpMethod.Get);
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (res.IsSuccessStatusCode) return DataProcessor.CheckServerTime(root);

        AddInfo("CheckServerTime: " + root.GetString("msg"));
        return false;
    }

    private async Task<bool> GetExchangeInfoAsync()
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/exchangeInfo", false, HttpMethod.Get);
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        //if (deepLog) AddInfo(await res.Content.ReadAsStringAsync(), false);

        var root = JsonDocument.Parse(content).RootElement;
        if (res.IsSuccessStatusCode)
        {
            DataProcessor.ProcessExchangeInfo(root);
            return true;
        }

        AddInfo("GetExchangeInfo: " + root.GetString("msg"));
        return false;
    }

    private async Task<bool> UpdateListenKey()
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/listenKey", true, HttpMethod.Post);
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (res.IsSuccessStatusCode)
        {
            ListenKey = root.GetString("listenKey");
            return true;
        }

        ListenKey = null;
        AddInfo("UpdateListenKey: " + root.GetString("msg"));
        return false;
    }


    public async Task<bool> GetTradesAsync(string symbol, bool recent = false)
    {
        var query = new Dictionary<string, string>() { { "symbol", symbol } };
        if (recent) query.Add("startTime", DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds().ToString(IC));
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/userTrades", true, HttpMethod.Get, query);
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (res.IsSuccessStatusCode)
        {
            DataProcessor.ProcessTrades(root);
            return true;
        }

        AddInfo("GetTrades: " + root.GetString("msg"));
        return false;
    }

    private async Task<bool> GetAllOpenOrdersAsync()
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/openOrders", true, HttpMethod.Get);
        if (res == null) return false;
        return await ProcessOrdersResponseAsync(res);
    }

    private async Task<bool> GetOrdersAsync(string symbol)
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/allOrders", true, HttpMethod.Get,
            new()
            {
                { "symbol", symbol },
                { "startTime", DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeMilliseconds().ToString(IC) }
            });
        if (res == null) return false;
        return await ProcessOrdersResponseAsync(res);
    }

    private async Task<bool> ProcessOrdersResponseAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        //if (DeepLog) AddInfo(content, false);

        var root = JsonDocument.Parse(content).RootElement;
        if (response.IsSuccessStatusCode)
        {
            DataProcessor.ProcessOrders(root);
            return true;
        }
        AddInfo("GetOrders: " + root.GetString("msg"));
        return false;
    }


    private async Task<bool> SetOneWayPositionMode()
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/positionSide/dual", true, HttpMethod.Post,
            new()
            {
                { "dualSidePosition", "false" }
            });
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);
        if (res.IsSuccessStatusCode) return true;

        var msg = JsonDocument.Parse(content).RootElement.GetString("msg");
        if (msg.StartsWith("No need to change position side")) return true;

        AddInfo("SetOneWayPositionMode: " + msg);
        return false;
    }

    private async Task<bool> ChangeInitialLeverage(string symbol, int leverage)
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/leverage", true, HttpMethod.Post,
            new()
            {
                { "symbol", symbol },
                { "leverage", leverage.ToString() }
            });
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(content).RootElement;

        if (DeepLog) AddInfo(content, false);
        if (res.IsSuccessStatusCode)
            return root.GetString("symbol") == symbol && root.GetInt("leverage") == leverage;

        AddInfo("ChangeInitialLeverage: " + root.GetString("msg"));
        return false;
    }

    private async Task<bool> SetCrossedMarginType(string symbol)
    {
        using var res = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/marginType", true, HttpMethod.Post,
            new()
            {
                { "symbol", symbol },
                { "marginType", "CROSSED" }
            });
        if (res == null) return false;

        var content = await res.Content.ReadAsStringAsync();
        if (DeepLog) AddInfo(content, false);
        if (res.IsSuccessStatusCode) return true;

        var msg = JsonDocument.Parse(content).RootElement.GetString("msg");
        if (msg.StartsWith("No need to change margin type")) return true;

        AddInfo("SetCrossedMarginType: " + msg);
        return false;
    }


    private async Task<HttpResponseMessage?> SendRequestAsync(string requestUri,
        bool sign, HttpMethod httpMethod, Dictionary<string, string>? query = null)
    {
        query ??= [];
        query["recvWindow"] = RecvWindow;
        query["timestamp"] = UnixTime.ToString(IC);
        var queryBuilder = BuildQueryString(query);

        if (sign)
        {
            var signature = GetSignature(queryBuilder.ToString());
            queryBuilder.Append("&signature=").Append(HttpUtility.UrlEncode(signature));
        }
        requestUri += "?" + queryBuilder.ToString();
        return await SendRequestAsync(requestUri, httpMethod);
    }

    private async Task<HttpResponseMessage?> SendRequestAsync(string requestUri, HttpMethod httpMethod)
    {
        using var request = new HttpRequestMessage(httpMethod, requestUri);
        request.Headers.Add("X-MBX-APIKEY", apiKey);

        HttpResponseMessage? response = null;
        var sentCommand = Task.Run(async () => response = await HttpClient.SendAsync(request));

        AddInfo("Request is sent: " + httpMethod.Method + " " + requestUri, false);
        await WaitSentCommandAsync(sentCommand, requestUri, 2000, 18000);
        return response;
    }

    private string GetSignature(string payload)
    {
        ArgumentNullException.ThrowIfNull(apiSecret, nameof(apiSecret));
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmacsha256 = new HMACSHA256(apiSecret);
        var hash = hmacsha256.ComputeHash(payloadBytes);

        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
    }

    private static StringBuilder BuildQueryString(Dictionary<string, string> queryParameters)
    {
        var builder = new StringBuilder();
        foreach (var parameter in queryParameters)
        {
            if (builder.Length > 0) builder.Append('&');
            builder.Append(parameter.Key).Append('=').Append(HttpUtility.UrlEncode(parameter.Value));
        }
        return builder;
    }


    private void SocketManagerConnectionChanged(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SocketManager.Connected))
        {
            Connection = SocketManager.Connected ? ConnectionState.Connected : ConnectionState.Disconnected;
            AddInfo(Connection.ToString(), !ReconnectTime);
        }
    }
}
