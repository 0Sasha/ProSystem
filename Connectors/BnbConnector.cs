using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace ProSystem;

internal class BnbConnector : Connector
{
    private string apiKey;
    private byte[] apiSecret;
    private readonly AddInformation AddInfo;
    private readonly BnbDataProcessor DataProcessor;
    private readonly TradingSystem TradingSystem;
    private readonly HttpClient HttpClient = new();
    private readonly WebSocketManager SocketManager;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    private const string BaseUrl = "https://api.binance.com";
    private const string BaseFuturesUrl = "https://fapi.binance.com";
    private const string BaseFuturesStreamUrl = "wss://fstream.binance.com";

    public BnbConnector(TradingSystem tradingSystem, AddInformation addInfo)
    {
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
        TimeFrames.Add(new("1m", 60, "ONE_MINUTE"));
        TimeFrames.Add(new("5m", 300, "FIVE_MINUTE"));
        TimeFrames.Add(new("30m", 1800, "THIRTY_MINUTE"));
        TimeFrames.Add(new("1h", 3600, "ONE_HOUR"));
        TimeFrames.Add(new("1d", 86400, "ONE_DAY"));
        DataProcessor = new(this, tradingSystem, addInfo);
        SocketManager = new(BaseFuturesStreamUrl +
            "/ws/btcusdt@kline_30m", DataProcessor.ProcessData, addInfo);
    }

    public override bool Initialize(int logLevel)
    {
        Initialized = true;
        return Initialized;
    }

    public override bool Uninitialize()
    {
        Initialized = false;
        return Initialized;
    }

    public override async Task<bool> ConnectAsync(string login, SecureString password)
    {
        apiKey = login ?? throw new ArgumentNullException(nameof(login));
        if (password == null) throw new ArgumentNullException(nameof(password));

        // TODO Not to put in plain string
        var valuePtr = Marshal.SecureStringToGlobalAllocUnicode(password);
        apiSecret = Encoding.UTF8.GetBytes(Marshal.PtrToStringUni(valuePtr));
        Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);

        if (!await CheckServerTimeAsync() || !await CheckAPIPermissionsAsync()) return false;
        
        Connection = ConnectionState.Connecting;
        await GetExchangeInfoAsync();

        if (!SocketManager.Connected)
        {
            if (!await SocketManager.ConnectAsync())
            {
                Connection = ConnectionState.Disconnected;
                return false;
            }
            await SocketManager.SendAsync("{ \"method\": \"LIST_SUBSCRIPTIONS\",\r\n\"id\": 3 }");
            //await SubscribeToTradesAsync(new("ETHUSDT"));
        }
        Connection = ConnectionState.Connected;
        return true;
    }

    public override async Task<bool> DisconnectAsync()
    {
        if (SocketManager.Connected)
        {
            Connection = ConnectionState.Disconnecting;
            await SocketManager.DisconnectAsync();
        }
        Connection = ConnectionState.Disconnected;
        return true;
    }

    public override Task<bool> SendOrderAsync(Security security, OrderType type, bool isBuy, double price, int quantity, string signal, Script sender = null, string note = null)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> ReplaceOrderAsync(Order activeOrder, Security security, OrderType type, double price, int quantity, string signal, Script sender = null, string note = null)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> CancelOrderAsync(Order activeOrder)
    {
        throw new NotImplementedException();
    }

    public override async Task<bool> SubscribeToTradesAsync(Security security) =>
        await SubUnsub(security, true);

    public override async Task<bool> UnsubscribeFromTradesAsync(Security security) =>
        await SubUnsub(security, false);

    private async Task<bool> SubUnsub(Security security, bool subscribe)
    {
        var method = subscribe ? "SUBSCRIBE" : "UNSUBSCRIBE";
        if (SocketManager.Connected)
        {
            await SocketManager.SendAsync("{\r\n\"method\": \"" + method + "\",\r\n\"params\":\r\n[\r\n\"" +
                security.Seccode.ToLower() + "@kline_30m\"],\r\n\"id\": 1\r\n}");
            return true;
        }

        AddInfo("SubUnsubToBars: WebSocketState is not open");
        return false;
    }

    public override async Task<bool> OrderHistoricalDataAsync(Security security, TimeFrame tf, int count)
    {
        var tool = TradingSystem.Tools.Single(t => t.Security.Seccode == security.Seccode ||
            t.BasicSecurity?.Seccode == security.Seccode);
        if (count > 1500)
        {
            var timeStep = (long)TimeSpan.FromSeconds(tf.Seconds * 980).TotalMilliseconds;
            var endTime = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
            var startTime = endTime - timeStep;
            while (count > 0)
            {
                var bars = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/klines", false, HttpMethod.Get,
                    new()
                    {
                        { "symbol", security.Seccode },
                        { "interval", tf.ID },
                        { "startTime", startTime.ToString() },
                        { "endTime", endTime.ToString() },
                        { "limit", "1500" }
                    });
                _ = Task.Run(() => DataProcessor.ProcessBars(bars, tf, security, tool.BaseTF));
                count -= 980;
                endTime = startTime;
                startTime = endTime - timeStep;
            }
        }
        else
        {
            var bars = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/klines", false, HttpMethod.Get,
                new()
                {
                    { "symbol", security.Seccode },
                    { "interval", tf.ID },
                    { "limit", count.ToString() }
                });
            _ = Task.Run(() => DataProcessor.ProcessBars(bars, tf, security, tool.BaseTF));
        }
        return true;
    }

    public override async Task<bool> OrderSecurityInfoAsync(Security security) => true;

    public override async Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio)
    {
        var info = await SendRequestAsync(BaseFuturesUrl + "/fapi/v2/account", true, HttpMethod.Get,
            new()
            {
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
        AddInfo(info, false);
        return true;
    }


    private async Task<string> SendRequestAsync(string requestUri, bool sign,
        HttpMethod httpMethod, Dictionary<string, string> query = null)
    {
        var queryBuilder = new StringBuilder();
        if (query != null) queryBuilder = BuildQueryString(query, queryBuilder);

        if (sign)
        {
            var signature = GetSignature(queryBuilder.ToString());
            if (queryBuilder.Length > 0) queryBuilder.Append('&');
            queryBuilder.Append("signature=").Append(HttpUtility.UrlEncode(signature));
        }
        if (queryBuilder.Length > 0) requestUri += "?" + queryBuilder.ToString();
        return await SendRequestAsync(requestUri, httpMethod);
    }

    private async Task<string> SendRequestAsync(string requestUri, HttpMethod httpMethod)
    {
        using var request = new HttpRequestMessage(httpMethod, requestUri);
        request.Headers.Add("X-MBX-APIKEY", apiKey);

        var response = await HttpClient.SendAsync(request);
        using HttpContent responseContent = response.Content;
        var result = await responseContent.ReadAsStringAsync();

        if (response.IsSuccessStatusCode) return result;
        // TODO Read all headers
        throw new Exception("StatusCode: " + (int)response.StatusCode + "\nResult: " + result);
    }


    private string GetSignature(string payload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmacsha256 = new HMACSHA256(apiSecret);
        var hash = hmacsha256.ComputeHash(payloadBytes);

        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
    }

    private StringBuilder BuildQueryString(Dictionary<string, string> queryParameters, StringBuilder builder)
    {
        foreach (var cur in queryParameters)
        {
            if (!string.IsNullOrWhiteSpace(cur.Value))
            {
                if (builder.Length > 0) builder.Append('&');
                builder.Append(cur.Key).Append('=').Append(HttpUtility.UrlEncode(cur.Value));
            }
        }

        return builder;
    }


    private async Task<bool> CheckServerTimeAsync()
    {
        var info = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/time", false, HttpMethod.Get);
        return DataProcessor.CheckServerTime(info);
    }

    private async Task GetExchangeInfoAsync()
    {
        var info = await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/exchangeInfo", false, HttpMethod.Get);
        DataProcessor.ProcessExchangeInfo(info);
    }

    private async Task<string> GetOrdersAsync(string symbol)
    {
        return await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/allOrders", true, HttpMethod.Get,
            new()
            {
                { "symbol", symbol },
                { "startTime", DateTimeOffset.Now.AddDays(-5).ToUnixTimeMilliseconds().ToString(IC) },
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
    }

    private async Task<string> GetTradesAsync(string symbol)
    {
        return await SendRequestAsync(BaseFuturesUrl + "/fapi/v1/userTrades", true, HttpMethod.Get,
            new()
            {
                { "symbol", symbol },
                { "startTime", DateTimeOffset.Now.AddDays(-5).ToUnixTimeMilliseconds().ToString(IC) },
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
    }

    private async Task<string> GetPositionAsync(string symbol)
    {
        return await SendRequestAsync(BaseFuturesUrl + "/fapi/v2/positionRisk", true, HttpMethod.Get,
            new()
            {
                { "symbol", symbol },
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
    }



    private async Task<bool> CheckAPIPermissionsAsync()
    {
        var info = await SendRequestAsync(BaseUrl + "/sapi/v1/account/apiRestrictions", true, HttpMethod.Get,
            new()
            {
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
        return DataProcessor.CheckAPIPermissions(info);
    }

    private async Task<string> GetAccountSnapshotAsync()
    {
        return await SendRequestAsync(BaseUrl + "/sapi/v1/accountSnapshot", true, HttpMethod.Get,
            new()
            {
                { "type", "FUTURES" }, // FUTURES
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
    }

    private async Task<string> GetFundingWalletAsync()
    {
        return await SendRequestAsync(BaseUrl + "/sapi/v1/asset/get-funding-asset", true, HttpMethod.Post,
            new()
            {
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
    }

    private async Task<string> GetUserAssetsAsync()
    {
        return await SendRequestAsync(BaseUrl + "/sapi/v3/asset/getUserAsset", true, HttpMethod.Post,
            new()
            {
                { "timestamp", DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString(IC) }
            });
    }
}
