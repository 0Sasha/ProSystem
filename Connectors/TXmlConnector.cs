using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Threading;
using System.Xml;

namespace ProSystem;

internal class TXmlConnector : Connector
{
    private delegate bool CallBackTXmlConnector(IntPtr Data);

    private readonly TXmlDataProcessor DataProcessor;
    private readonly CallBackTXmlConnector CallBackTXml;
    private readonly ConcurrentQueue<string> DataQueue = new();
    private readonly XmlReaderSettings XS = new()
    {
        IgnoreWhitespace = true,
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Parse
    };
    private readonly StringComparison SC = StringComparison.Ordinal;
    private readonly Thread MainThread;
    private readonly int FirstTradingHour = 7;

    private int waitingTimeMs = 18000;
    private bool isWorking;

    public virtual double USDRUB { get; set; }
    public virtual double EURRUB { get; set; }
    public override bool ReconnectTime =>
        base.ReconnectTime || ServerTime.Hour == FirstTradingHour && ServerTime.Minute is 0 or 1;
    public override DateTime ServerTime { get => DateTime.UtcNow.AddHours(3); }
    public List<ClientAccount> Clients { get; } = [];

    public TXmlConnector(TradingSystem tradingSystem, AddInformation addInfo) : base(tradingSystem, addInfo)
    {
        DataProcessor = new(this, tradingSystem, addInfo);
        CallBackTXml = CallBack;
        MainThread = new(ProcessData) { IsBackground = true, Name = "DataProcessor" };
    }

    private bool CallBack(IntPtr data)
    {
        var strData = Marshal.PtrToStringUTF8(data);
        if (strData != null) DataQueue.Enqueue(strData);
        FreeMemory(data);
        return true;
    }

    private void ProcessData()
    {
        while (isWorking)
        {
            var data = string.Empty;
            try
            {
                while (!DataQueue.IsEmpty)
                {
                    if (DataQueue.TryDequeue(out data)) DataProcessor.ProcessData(data);
                    else AddInfo("ProcessDataQueue: не удалось взять объект из очереди.");
                }
            }
            catch (Exception e)
            {
                AddInfo("ProcessDataQueue: Исключение: " + e.Message, notify: true);
                AddInfo("Трассировка стека: " + e.StackTrace);
                AddInfo("Данные: " + data, false);
                if (e.InnerException != null)
                {
                    AddInfo("Внутреннее исключение: " + e.InnerException.Message);
                    AddInfo("Трассировка стека внутреннего исключения: " + e.InnerException.StackTrace);
                }
            }
            Thread.Sleep(5);
        }
    }

    #region High level methods
    public override async Task<bool> ConnectAsync(string login, SecureString password)
    {
        ArgumentException.ThrowIfNullOrEmpty(login, nameof(login));
        if (!Initialized) throw new Exception("Connector is not initialized.");

        Securities.Clear(); Markets.Clear();
        TimeFrames.Clear(); Clients.Clear();
        TradingSystem.Portfolio.Positions.Clear();
        TradingSystem.Portfolio.MoneyPositions.Clear();
        TradingSystem.Window.Dispatcher.Invoke(TradingSystem.Orders.Clear);

        waitingTimeMs = 18000;
        Connection = ConnectionState.Connecting;

        var host = !BackupServer ? "tr1.finam.ru" : "tr2.finam.ru";

        var firstPart = "<command id=\"connect\"><login>" + login + "</login><password>";
        var lastPart = "</password><host>" + host + "</host><port>3900</port><rqdelay>20</rqdelay>" +
            "<session_timeout>180</session_timeout><request_timeout>15</request_timeout>" +
            "<push_u_limits>180</push_u_limits><push_pos_equity>0</push_pos_equity></command>";

        var res = await SendCommandAsync(firstPart, lastPart, password);
        if (res.StartsWith("<result success=\"true\"", SC) || res.Contains("уже устанавливается")) return true;
        Connection = ConnectionState.Disconnected;
        AddInfo("Connect: " + res);
        return false;
    }

    public override async Task<bool> DisconnectAsync()
    {
        var result = true;
        Connection = ConnectionState.Disconnecting;

        var res = await SendCommandAsync("<command id=\"disconnect\"/>");
        if (res.StartsWith("<result success=\"true\"", SC)) Connection = ConnectionState.Disconnected;
        else
        {
            if (res.StartsWith("<result success=\"false\"><message>Соединение не установлено", SC))
                Connection = ConnectionState.Disconnected;
            else result = false;
            AddInfo("Disconnect: " + res);
        }
        Thread.Sleep(1000);
        return result;
    }

    public override async Task ResetAsync()
    {
        await base.ResetAsync();
        await FileManager.ArchiveFiles("Logs/Transaq", ServerTime.AddDays(-1).ToString("yyyyMMdd"),
            ServerTime.AddDays(-1).ToString("yyyyMMdd") + " archive", true);
        var time = ServerTime.Date.AddHours(FirstTradingHour) - ServerTime;
        if (time.TotalMinutes > 0)
        {
            AddInfo("Reset: waiting " + time, false);
            await Task.Delay(time);
        }
    }

    public override async Task<bool> SendOrderAsync(Security symbol, OrderType type, bool isBuy,
        double price, double quantity, string signal, Script? sender = null, string? note = null)
    {
        var senderName = sender != null ? sender.Name : "System";
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(price, nameof(price));
        ArgumentOutOfRangeException.ThrowIfLessThan(quantity, 1, nameof(quantity));

        price = Math.Round(price, symbol.TickPrecision);
        var side = isBuy ? "B" : "S";
        string id, market = "", condition = "None";

        if (type == OrderType.Limit) id = "neworder";
        else if (type == OrderType.Conditional)
        {
            id = "newcondorder";
            condition = isBuy ? "BidOrLast" : "AskOrLast";
        }
        else if (type == OrderType.Market)
        {
            id = "neworder";
            market = "<bymarket/>";
        }
        else throw new ArgumentException("Unknown type of the order", nameof(type));

        var command = "<command id=\"" + id + "\">" +
            "<security><board>" + symbol.Board + "</board><seccode>" + symbol.Seccode + "</seccode></security>" +
            "<union>" + TradingSystem.Portfolio.Union + "</union><price>" + price.ToString(IC) + "</price>" +
            "<quantity>" + quantity + "</quantity><buysell>" + side + "</buysell>" + market +
            (type != OrderType.Conditional ? "</command>" :
            "<cond_type>" + condition + "</cond_type><cond_value>" + price.ToString(IC) + "</cond_value>" +
            "<validafter>0</validafter><validbefore>till_canceled</validbefore></command>");

        using XmlReader xr = XmlReader.Create(new StringReader(await SendCommandAsync(command)), XS);
        xr.Read();
        var success = xr.GetAttribute("success");
        if (success == "true")
        {
            var order = new Order(0, symbol.Seccode, "Sent", ServerTime, side)
            {
                TrID = xr.GetIntAttribute("transactionid"),
                InitType = type,
                Sender = senderName,
                Signal = signal,
                Note = note
            };

            await TradingSystem.Window.Dispatcher.InvokeAsync(() =>
            {
                if (sender != null) sender.Orders.Add(order);
                else TradingSystem.SystemOrders.Add(order);
            }, DispatcherPriority.Send);

            AddInfo("SendOrder: order is sent: " + order.Sender + "/" + order.Seccode + "/" + order.Side + "/" +
                order.Price + "/" + order.Quantity + "/" + order.TrID, TradingSystem.Settings.DisplaySentOrders);

            _ = Task.Run(() =>
            {
                Thread.Sleep(3000);
                TradingSystem.Window.Dispatcher.Invoke(() =>
                {
                    if (!TradingSystem.Orders.ToArray().Any(o => o.TrID == order.TrID))
                        TradingSystem.Orders.Add(order);
                }, DispatcherPriority.Send);
            });
            return true;
        }
        else if (success == "false")
        {
            AddInfo("SendOrder: order of " + senderName +
                " is not accepted: " + xr.GetNextString("message"), true);
        }
        else
        {
            xr.Read();
            AddInfo("SendOrder: order of " + senderName + " is not accepted: " + xr.Value);
        }
        return false;
    }

    public override async Task<bool> CancelOrderAsync(Order activeOrder)
    {
        var result = await SendCommandAsync("<command id=\"cancelorder\"><transactionid>" +
            activeOrder.TrID + "</transactionid></command>");
        using XmlReader xr = XmlReader.Create(new StringReader(result), XS);
        xr.Read();

        if (xr.Name == "error" || xr.GetAttribute("success") == "false")
        {
            xr.Read(); xr.Read();
            if (xr.Value.Contains("Неверное значение параметра"))
            {
                activeOrder.Status = activeOrder.Status == "active" ? "cancelled" : "disabled";
                activeOrder.ChangeTime = ServerTime.AddDays(-2);
                AddInfo("CancelOrder: " + activeOrder.Sender + ": active order is not actual. Status is updated.");
            }
            else AddInfo("CancelOrder: " + activeOrder.Sender + ": error: " + xr.Value);
            xr.Close();
            return false;
        }
        AddInfo("CancelOrder: request is sent " + activeOrder.Sender + "/" + activeOrder.Seccode, false);
        xr.Close();
        return true;
    }

    public override async Task<bool> ReplaceOrderAsync(Order activeOrder, Security symbol, OrderType type,
        double price, double quantity, string signal, Script? sender = null, string? note = null)
    {
        var senderName = sender != null ? sender.Name : "System";
        AddInfo("ReplaceOrder: replacement by " + senderName, false);
        if (await CancelOrderAsync(activeOrder))
        {
            for (int i = 0; i < 15; i++)
            {
                Thread.Sleep(200);
                if (activeOrder.Status is "cancelled" or "disabled") break;
                if (activeOrder.Status == "matched")
                {
                    AddInfo("Order of " + senderName + " is already executed");
                    return false;
                }
                else if (i == 14)
                {
                    AddInfo("Didn't wait for the order cancellation: " + senderName);
                    return false;
                }
            }
            return await SendOrderAsync(symbol, type,
                activeOrder.Side == "B", price, quantity, signal, sender, note);
        }
        return false;
    }

    public override async Task<bool> SubscribeToTradesAsync(Security security) =>
        await SubUnsubAsync(true, security);

    public override async Task<bool> UnsubscribeFromTradesAsync(Security security) =>
        await SubUnsubAsync(false, security);

    private async Task<bool> SubUnsubAsync(bool subscribe, Security symbol, bool quotations = false, bool quotes = false)
    {
        var command = (subscribe ? "<command id=\"subscribe\">" : "<command id=\"unsubscribe\">") +
            "<alltrades><security><board>" + symbol.Board + "</board><seccode>" + symbol.Seccode +
            "</seccode></security></alltrades>";
        if (quotations) command += "<quotations><security><board>" +
                symbol.Board + "</board><seccode>" + symbol.Seccode + "</seccode></security></quotations>";
        if (quotes) command += "<quotes><security><board>" +
                symbol.Board + "</board><seccode>" + symbol.Seccode + "</seccode></security></quotes>";
        command += "</command>";

        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        ShowBadResult("SubUnsub", result, command);
        return false;
    }

    protected override async Task<bool> OrderHistoricalDataAsync(Security security, TimeFrame tf, int count, int _)
    {
        var command = "<command id=\"gethistorydata\"><security><board>" + security.Board +
            "</board><seccode>" + security.Seccode + "</seccode></security><period>" +
            tf.ID + "</period><count>" + count + "</count><reset>true</reset></command>";
        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        ShowBadResult("OrderHistoricalData", result, command);
        return false;
    }

    public override async Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio)
    {
        var union = portfolio.Union == null || portfolio.Union == "" ? Clients[0].Union : portfolio.Union;
        var command = "<command id=\"get_mc_portfolio\" union=\"" + union +
            "\" currency=\"false\" asset=\"false\" money=\"false\" depo=\"true\" registers=\"false\"/>";
        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        ShowBadResult("OrderPortfolioInfo", result, command);
        return false;
    }


    public override async Task<bool> PrepareForTradingAsync()
    {
        return await OrderHistoricalDataAsync(new("CETS", "USD000UTSTOM"), 1, count: 1) &&
            await OrderHistoricalDataAsync(new("CETS", "EUR_RUB__TOM"), 1, count: 1);
    }

    public override async Task<bool> OrderSecurityInfoAsync(Security symbol) =>
        await GetClnSecPermissionsAsync(symbol) && await GetSecurityInfoAsync(symbol);

    private async Task<bool> GetSecurityInfoAsync(Security symbol)
    {
        var command = "<command id=\"get_securities_info\"><security><market>" + symbol.Market +
            "</market><seccode>" + symbol.Seccode + "</seccode></security></command>";
        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        ShowBadResult("GetSecurityInfo", result, command);
        return false;
    }

    private async Task<bool> GetClnSecPermissionsAsync(Security symbol)
    {
        var client = Clients.SingleOrDefault(x => x.Market == symbol.Market);
        if (client == null)
        {
            AddInfo("GetClnSecPermissions: client is not found");
            return false;
        }

        var command = "<command id=\"get_cln_sec_permissions\"><security><board>" + symbol.Board + "</board>" +
            "<seccode>" + symbol.Seccode + "</seccode></security><client>" + client.ID + "</client></command>";
        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        ShowBadResult("GetClnSecPermissions", result, command);
        return false;
    }

    private void ShowBadResult(string method, string result, string command) =>
        AddInfo(method + ": " + result + " || " + command);

    public override async Task WaitForCertaintyAsync(Tool tool)
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
        if (lastTrade != null && lastTrade.Time.AddSeconds(2) > ServerTime) await Task.Delay(1500);
    }


    public override bool SecurityIsBidding(Security security)
    {
        if (ServerTime < DateTime.Today.AddHours(1) ||
            ServerTime > DateTime.Today.AddMinutes(839).AddSeconds(55) &&
            ServerTime < DateTime.Today.AddMinutes(845)) return false;

        if (ServerTime > DateTime.Today.AddMinutes(1129).AddSeconds(55) &&
            ServerTime < DateTime.Today.AddMinutes(1145)) return security.LastTrade.Time.AddSeconds(5) > ServerTime;

        return security.LastTrade.Time.AddHours(1) > ServerTime;
    }

    protected override bool CheckRequirements(Security security)
    {
        if (security.Market != "4")
        {
            AddInfo(security.Seccode + ": unexpected market: " + security.Market, notify: true);
            return false;
        }

        if (security.TickSize < 0.000001 || security.TickCost < 0.000001 || security.TickPrecision < -0.000001 ||
            security.LotSize < 0.000001 || security.LotPrecision < -0.000001)
        {
            AddInfo("CheckRequirements: " + security.Seccode + ": properties are incorrect", notify: true);
            return false;
        }

        if (security.InitReqLong < 100 || security.InitReqShort < 100 ||
            security.Deposit < 100 || security.InitReqLong < security.Deposit / 2)
        {
            AddInfo(security.Seccode + ": reqs are out of norm: " +
                security.InitReqLong + "/" + security.InitReqShort + " SellDep: " + security.Deposit, true, true);
            Task.Run(async () => await OrderSecurityInfoAsync(security));
            return false;
        }
        return true;
    }

    public override bool OrderIsActive(Order order) => order.Status is "active" or "watching";

    public override bool OrderIsExecuted(Order order) => order.Status == "matched";

    public override bool OrderIsTriggered(Order order) => order.Status == "active";
    #endregion

    #region Underlying methods
    public override bool Initialize(int logLevel)
    {
        if (!Directory.Exists("Logs/Transaq")) Directory.CreateDirectory("Logs/Transaq");

        IntPtr path = Marshal.StringToHGlobalAnsi("Logs/Transaq");
        IntPtr result = Initialize(path, logLevel);
        Marshal.FreeHGlobal(path);

        if (result.Equals(IntPtr.Zero))
        {
            FreeMemory(result);
            if (SetCallback(CallBackTXml))
            {
                isWorking = true;
                MainThread.Start();
                Initialized = true;
                return true;
            }

            AddInfo("Callback failed.");
            return false;
        }

        AddInfo("Initialization failed: " + Marshal.PtrToStringUTF8(result));
        FreeMemory(result);
        return false;
    }

    public override bool Uninitialize()
    {
        if (Connection == ConnectionState.Disconnected)
        {
            IntPtr result = UnInitialize();
            if (!result.Equals(IntPtr.Zero))
            {
                AddInfo("Uninitialization failed: " + Marshal.PtrToStringUTF8(result));
                FreeMemory(result);
                return false;
            }

            FreeMemory(result);
            isWorking = false;
            Initialized = false;
            return true;
        }

        AddInfo("Uninitialization failed: server is connected.");
        return false;
    }

    private async Task<string> SendCommandAsync(string command)
    {
        if (Connection == ConnectionState.Disconnected) return "There is no connection";
        IntPtr nintCommand = Marshal.StringToHGlobalAnsi(command);
        var result = await SendCommandAsync(nintCommand, command);
        Marshal.FreeHGlobal(nintCommand);
        return result;
    }

    private async Task<string> SendCommandAsync(string firstPart, string lastPart, SecureString middlePart)
    {
        lastPart += "\0";
        IntPtr first = Marshal.StringToHGlobalAnsi(firstPart);
        IntPtr last = Marshal.StringToHGlobalAnsi(lastPart);
        IntPtr command = Marshal.AllocHGlobal(firstPart.Length + middlePart.Length + lastPart.Length);
        IntPtr middle;
        unsafe
        {
            byte* data = (byte*)command.ToPointer();
            byte* firstPartBt = (byte*)first.ToPointer();
            for (int i = 0; i < firstPart.Length; i++) *data++ = *firstPartBt++;

            middle = Marshal.SecureStringToGlobalAllocAnsi(middlePart);
            byte* middlePartBt = (byte*)middle.ToPointer();
            for (int i = 0; i < middlePart.Length; i++) *data++ = *middlePartBt++;

            byte* lastPartBt = (byte*)last.ToPointer();
            for (int i = 0; i < lastPart.Length; i++) *data++ = *lastPartBt++;
        }

        var result = await SendCommandAsync(command, firstPart);
        Marshal.FreeHGlobal(command);
        Marshal.ZeroFreeGlobalAllocAnsi(middle);
        Marshal.FreeHGlobal(first);
        Marshal.FreeHGlobal(last);
        return result;
    }

    private async Task<string> SendCommandAsync(IntPtr command, string strCommand)
    {
        var result = "Empty";
        var sentCommand = Task.Run(() =>
        {
            IntPtr res = SendCommand(command);
            result = Marshal.PtrToStringUTF8(res);
            FreeMemory(res);
        });
        await WaitSentCommandAsync(sentCommand, strCommand, 2000, waitingTimeMs);
        return result;
    }
    #endregion

    #region Import external methods
    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool SetCallback(CallBackTXmlConnector callBack);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern IntPtr SendCommand(IntPtr Data);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool FreeMemory(IntPtr Data);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr Initialize(IntPtr Path, int LogLevel);

    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.Winapi)]
    private static extern IntPtr UnInitialize();
    #endregion
}
