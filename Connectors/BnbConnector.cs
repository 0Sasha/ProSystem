using System;
using System.Collections.Generic;
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
    private readonly TradingSystem TradingSystem;
    private readonly HttpClient HttpClient = new();
    private const string BaseUrl = "https://api.binance.com";

    public BnbConnector(TradingSystem tradingSystem, AddInformation addInfo)
    {
        TradingSystem = tradingSystem;
        AddInfo = addInfo;
        TimeFrames.Add(new("1m", 60, "ONE_MINUTE"));
        TimeFrames.Add(new("5m", 300, "FIVE_MINUTE"));
        TimeFrames.Add(new("30m", 1800, "THIRTY_MINUTE"));
        TimeFrames.Add(new("1h", 3600, "ONE_HOUR"));
        TimeFrames.Add(new("1d", 86400, "ONE_DAY"));
    }

    public override bool Initialize(int logLevel) => true;

    public override bool Uninitialize() => true;

    public override async Task<bool> ConnectAsync(string login, SecureString password)
    {
        apiKey = login ?? throw new ArgumentNullException(nameof(login));

        // TODO Not to put in plain string
        var valuePtr = Marshal.SecureStringToGlobalAllocUnicode(password);
        var pas = Marshal.PtrToStringUni(valuePtr);
        Marshal.ZeroFreeGlobalAllocUnicode(valuePtr);

        apiSecret = Encoding.UTF8.GetBytes(pas);

        var res = await SendRequestAsync("/api/v3/ping", false, HttpMethod.Get);
        if (res == "{}")
        {
            Connection = ConnectionState.Connected;
            return true;
        }
        else
        {
            Connection = ConnectionState.Disconnected;
            return false;
        }
    }

    public override Task<bool> DisconnectAsync()
    {
        throw new NotImplementedException();
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

    public override Task<bool> SubscribeToTradesAsync(Security security)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> UnsubscribeFromTradesAsync(Security security)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> OrderHistoricalDataAsync(Security security, TimeFrame tf, int count)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> OrderSecurityInfoAsync(Security security)
    {
        throw new NotImplementedException();
    }

    public override Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio)
    {
        throw new NotImplementedException();
    }


    private async Task<string> SendRequestAsync(string requestUri, bool signed,
        HttpMethod httpMethod, Dictionary<string, string> query = null)
    {
        var queryBuilder = new StringBuilder();
        if (query != null) queryBuilder = BuildQueryString(query, queryBuilder);

        if (signed)
        {
            if (queryBuilder.Length > 0) queryBuilder.Append('&');
            var signature = Sign(queryBuilder.ToString());
            queryBuilder.Append("signature=").Append(HttpUtility.UrlEncode(signature));
        }
        if (queryBuilder.Length > 0) requestUri += "?" + queryBuilder.ToString();
        return await SendRequestAsync(requestUri, httpMethod);
    }

    private async Task<string> SendRequestAsync(string requestUri, HttpMethod httpMethod)
    {
        using var request = new HttpRequestMessage(httpMethod, BaseUrl + requestUri);
        request.Headers.Add("X-MBX-APIKEY", apiKey);

        var response = await HttpClient.SendAsync(request);
        using HttpContent responseContent = response.Content;
        var result = await responseContent.ReadAsStringAsync();

        if (response.IsSuccessStatusCode) return result;
        throw new Exception("StatusCode: " + (int)response.StatusCode + "\nResult: " + result);
    }


    private string Sign(string payload)
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
}
