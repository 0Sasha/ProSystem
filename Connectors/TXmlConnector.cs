using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
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

    private int waitingTimeMs = 18000;
    private Thread mainThread;
    private bool isWorking;

    public virtual double USDRUB { get; set; }
    public virtual double EURRUB { get; set; }
    public List<ClientAccount> Clients { get; } = new();

    public TXmlConnector(TradingSystem tradingSystem, AddInformation addInfo) : base(tradingSystem, addInfo)
    {
        DataProcessor = new(this, tradingSystem, addInfo);
        CallBackTXml = CallBack;
    }

    private bool CallBack(IntPtr data)
    {
        DataQueue.Enqueue(Marshal.PtrToStringUTF8(data));
        FreeMemory(data);
        return true;
    }

    private void ProcessData()
    {
        while (isWorking)
        {
            string data = null;
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
        if (string.IsNullOrEmpty(login)) throw new ArgumentNullException(nameof(login));
        if (password == null) throw new ArgumentNullException(nameof(password));
        if (!Initialized) throw new Exception("Connector is not initialized.");

        Securities.Clear(); Markets.Clear();
        TimeFrames.Clear(); Clients.Clear();
        TradingSystem.Portfolio.Positions.Clear();
        TradingSystem.Portfolio.MoneyPositions.Clear();
        TradingSystem.Window.Dispatcher.Invoke(() => TradingSystem.Orders.Clear());

        var settings = TradingSystem.Settings;
        waitingTimeMs = settings.RequestTM * 1000 + 3000;
        ReconnectionTrigger = DateTime.Now.AddSeconds(settings.SessionTM);
        Connection = ConnectionState.Connecting;

        // Частота обращений коннектора к серверу Transaq в миллисекундах. Минимум 10.
        var delay = "20";
        // Таймаут информирования о текущих показателях единого портфеля минимум один раз в N секунд.
        var limits = settings.SessionTM.ToString();
        // Таймаут информирования о текущей стоимости позиций один раз в N секунд за исключением позиций FORTS.
        var equity = "0";
        var host = !BackupServer ? "tr1.finam.ru" : "tr2.finam.ru";
        var port = "3900";

        var firstPart = "<command id=\"connect\"><login>" + login + "</login><password>";
        var lastPart = "</password><host>" + host + "</host><port>" + port + "</port><rqdelay>" + delay +
            "</rqdelay><session_timeout>" + settings.SessionTM + "</session_timeout><request_timeout>" +
            settings.RequestTM + "</request_timeout><push_u_limits>" + limits +
            "</push_u_limits><push_pos_equity>" + equity + "</push_pos_equity></command>";

        var res = await SendCommandAsync(firstPart, lastPart, password);
        if (res.StartsWith("<result success=\"true\"", SC) || res.Contains("уже устанавливается")) return true;
        Connection = ConnectionState.Disconnected;
        AddInfo("Connect: " + res);
        return false;
    }

    public override async Task<bool> DisconnectAsync()
    {
        var result = true;
        TradingSystem.ReadyToTrade = false;
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

    public override async Task<bool> SendOrderAsync(Security symbol, OrderType type, bool isBuy,
        double price, int quantity, string signal, Script sender = null, string note = null)
    {
        var senderName = sender != null ? sender.Name : "System";
        if (symbol == null) throw new ArgumentNullException(nameof(symbol));
        if (price < 0.00000001)
            throw new ArgumentOutOfRangeException(nameof(price), "Price <= 0. Sender: " + senderName);
        if (quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity < 1. Sender: " + senderName);

        price = Math.Round(price, symbol.Decimals);
        var buySell = isBuy ? "B" : "S";
        var credit = ""; // UseCredit ? "<usecredit/>" : 
        string id, market, condition;

        if (type == OrderType.Limit)
        {
            id = "neworder";
            market = "";
            condition = "None";
        }
        else if (type == OrderType.Conditional)
        {
            id = "newcondorder";
            market = "";
            condition = isBuy ? "BidOrLast" : "AskOrLast";
        }
        else if (type == OrderType.Market)
        {
            id = "neworder";
            market = "<bymarket/>";
            condition = "None";
        }
        else throw new ArgumentException("Unknown type of the order", nameof(type));

        var command = "<command id=\"" + id + "\">" +
            "<security><board>" + symbol.Board + "</board><seccode>" + symbol.Seccode + "</seccode></security>" +
            "<union>" + TradingSystem.Portfolio.Union + "</union><price>" + price.ToString(IC) + "</price>" +
            "<quantity>" + quantity + "</quantity><buysell>" + buySell + "</buysell>" + market +
            (type != OrderType.Conditional ? credit + "</command>" :
            "<cond_type>" + condition + "</cond_type><cond_value>" + price.ToString(IC) + "</cond_value>" +
            "<validafter>0</validafter><validbefore>till_canceled</validbefore>" + credit + "</command>");

        using XmlReader xr = XmlReader.Create(new StringReader(await SendCommandAsync(command)), XS);
        xr.Read();
        if (xr.GetAttribute("success") == "true")
        {
            int trId = int.Parse(xr.GetAttribute("transactionid"), IC);
            var order = new Order(trId, senderName, signal, note);
            await TradingSystem.Window.Dispatcher.InvokeAsync(() =>
            {
                if (sender != null) sender.Orders.Add(order);
                else TradingSystem.SystemOrders.Add(order);
            }, DispatcherPriority.Send);

            AddInfo("SendOrder: order is sent: " + senderName + "/" + symbol.Seccode + "/" +
                buySell + "/" + price + "/" + quantity + "/" + trId, TradingSystem.Settings.DisplaySentOrders);

            xr.Close();
            return true;
        }
        else if (xr.GetAttribute("success") == "false")
        {
            xr.ReadToFollowing("message");
            xr.Read();
            AddInfo("SendOrder: order of " + senderName + " is not accepted: " + xr.Value, true,
                xr.Value.Contains("Недостаток обеспечения"));
        }
        else
        {
            xr.Read();
            AddInfo("SendOrder: order of " + senderName + " is not accepted: " + xr.Value);
        }
        xr.Close();
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
                activeOrder.DateTime = DateTime.Now.AddDays(-2);
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
        double price, int quantity, string signal, Script sender = null, string note = null)
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
                activeOrder.BuySell == "B", price, quantity, signal, sender, note);
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
        AddInfo("SubUnsub: " + result);
        return false;
    }
    
    public override async Task<bool> OrderHistoricalDataAsync(Security symbol, TimeFrame tf, int count)
    {
        var command = "<command id=\"gethistorydata\"><security><board>" + symbol.Board +
            "</board><seccode>" + symbol.Seccode +"</seccode></security><period>" +
            tf.ID + "</period><count>" + count + "</count><reset>true</reset></command>";
        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("OrderHistoricalData: " + result);
        return false;
    }

    public override async Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio)
    {
        var union = portfolio.Union == null || portfolio.Union == "" ? Clients[0].Union : portfolio.Union;
        var command = "<command id=\"get_mc_portfolio\" union=\"" + union +
            "\" currency=\"false\" asset=\"false\" money=\"false\" depo=\"true\" registers=\"false\"/>";
        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("OrderPortfolioInfo: " + result);
        return false;
    }


    public override bool SecurityIsBidding(Security security)
    {
        if (DateTime.Now < DateTime.Today.AddHours(1) ||
            DateTime.Now > DateTime.Today.AddMinutes(839).AddSeconds(55) &&
            DateTime.Now < DateTime.Today.AddMinutes(845)) return false;

        if (DateTime.Now > DateTime.Today.AddMinutes(1129).AddSeconds(55) &&
            DateTime.Now < DateTime.Today.AddMinutes(1145))
            return security.LastTrade.DateTime > DateTime.Today.AddMinutes(1130);

        return security.LastTrade.DateTime.AddHours(1) > DateTime.Now;
    }

    public override bool CheckRequirements(Security security)
    {
        if (security.Market != "4")
        {
            AddInfo(security.Seccode + ": unexpected market: " + security.Market, notify: true);
            return false;
        }

        if (security.InitReqLong < 100 || security.InitReqShort < 100 ||
            security.SellDeposit < 100 || security.InitReqLong < security.SellDeposit / 2)
        {
            AddInfo(security.Seccode + ": reqs are out of norm: " +
                security.InitReqLong + "/" + security.InitReqShort + " SellDep: " + security.SellDeposit, true, true);
            Task.Run(async () => await OrderSecurityInfoAsync(security));
            return false;
        }
        return true;
    }


    public override async Task<bool> OrderPreTradingData()
    {
        await OrderHistoricalDataAsync(new("CETS", "USD000UTSTOM"), new("1", 60), 1);
        await OrderHistoricalDataAsync(new("CETS", "EUR_RUB__TOM"), new("1", 60), 1);
        return true;
    }

    public override async Task<bool> OrderSecurityInfoAsync(Security symbol) =>
        await GetClnSecPermissionsAsync(symbol) && await GetSecurityInfoAsync(symbol);

    private async Task<bool> GetSecurityInfoAsync(Security symbol)
    {
        var command = "<command id=\"get_securities_info\"><security><market>" + symbol.Market +
            "</market><seccode>" + symbol.Seccode + "</seccode></security></command>";
        var result = await SendCommandAsync(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("GetSecurityInfo: " + result);
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
        AddInfo("GetClnSecPermissions: " + result);
        return false;
    }
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
                mainThread = new(ProcessData) { IsBackground = true, Name = "DataProcessor" };
                mainThread.Start();
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
