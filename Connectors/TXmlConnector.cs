using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace ProSystem;

internal class TXmlConnector : Connector
{
    private readonly AddInformation AddInfo;
    private readonly TradingSystem TradingSystem;
    private readonly TXmlDataProcessor DataProcessor;

    private int waitingTime = 18000;
    private ConnectionState connection;
    private Thread mainThread;
    private bool isWorking;

    private delegate bool CallBackDel(IntPtr Data);
    private readonly CallBackDel CallbackDel;

    private readonly ConcurrentQueue<string> DataQueue = new();
    private readonly XmlReaderSettings XS = new()
    {
        IgnoreWhitespace = true,
        ConformanceLevel = ConformanceLevel.Fragment,
        DtdProcessing = DtdProcessing.Parse
    };
    private readonly StringComparison SC = StringComparison.Ordinal;
    private readonly CultureInfo IC = CultureInfo.InvariantCulture;

    public bool Scheduled { get; set; }

    public List<ClientAccount> Clients { get; } = new();

    public override ConnectionState Connection
    {
        get => connection;
        set
        {
            if (connection != value)
            {
                connection = value;
                Notify(nameof(Connection));
            }
        }
    }

    public TXmlConnector(TradingSystem tradingSystem, AddInformation addInfo)
    {
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
        DataProcessor = new(this, tradingSystem, addInfo);
        CallbackDel = CallBack;
    }


    public override event PropertyChangedEventHandler PropertyChanged;

    private void Notify(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool CallBack(IntPtr data)
    {
        DataQueue.Enqueue(Marshal.PtrToStringUTF8(data));
        FreeMemory(data);
        return true;
    }

    private void ProcessDataQueue()
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
    public override async Task<bool> ConnectAsync(string login, SecureString password, bool scheduled)
    {
        if (!Initialized) throw new Exception("Connector is not initialized.");

        Securities.Clear(); Markets.Clear();
        TimeFrames.Clear(); Clients.Clear();
        TradingSystem.Window.Dispatcher.Invoke(() => TradingSystem.Orders.Clear());

        var settings = TradingSystem.Settings;
        waitingTime = settings.RequestTM * 1000 + 3000;
        Connection = ConnectionState.Connecting;
        TriggerReconnection = DateTime.Now.AddSeconds(settings.SessionTM);
        Scheduled = scheduled;

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

        var res = await SendCommand(firstPart, lastPart, password);
        if (res.StartsWith("<result success=\"true\"", SC) || res.Contains("уже устанавливается")) return true;
        Connection = ConnectionState.Disconnected;
        AddInfo("Connect: " + res);
        return false;
    }

    public override async Task<bool> DisconnectAsync(bool scheduled = false)
    {
        var result = true;
        TradingSystem.ReadyToTrade = false;
        Connection = ConnectionState.Disconnecting;
        Scheduled = scheduled;

        var res = await SendCommand("<command id=\"disconnect\"/>");
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
        if (price < double.Epsilon)
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

        using XmlReader xr = XmlReader.Create(new StringReader(await SendCommand(command)), XS);
        xr.Read();
        if (xr.GetAttribute("success") == "true")
        {
            int trId = int.Parse(xr.GetAttribute("transactionid"), IC);
            TradingSystem.Window.Dispatcher.Invoke(() =>
            {
                if (sender != null) sender.Orders.Add(new Order(trId, sender.Name, signal, note));
                else TradingSystem.SystemOrders.Add(new Order(trId, senderName, signal, note));
            });

            AddInfo("SendOrder: order is sent: " + senderName + "/" + symbol.Seccode + "/" +
                buySell + "/" + price + "/" + quantity, TradingSystem.Settings.DisplaySentOrders);

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
        var result = await SendCommand("<command id=\"cancelorder\"><transactionid>" +
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

    public override async Task<bool> SubscribeToTradesAsync(Security security) => await SubUnsub(true, security);

    public override async Task<bool> UnsubscribeFromTradesAsync(Security security) => await SubUnsub(false, security);

    private async Task<bool> SubUnsub(bool subscribe, Security symbol, bool quotations = false, bool quotes = false)
    {
        var command = (subscribe ? "<command id=\"subscribe\">" : "<command id=\"unsubscribe\">") +
            "<alltrades><security><board>" + symbol.Board + "</board><seccode>" + symbol.Seccode +
            "</seccode></security></alltrades>";
        if (quotations) command += "<quotations><security><board>" +
                symbol.Board + "</board><seccode>" + symbol.Seccode + "</seccode></security></quotations>";
        if (quotes) command += "<quotes><security><board>" +
                symbol.Board + "</board><seccode>" + symbol.Seccode + "</seccode></security></quotes>";
        command += "</command>";

        var result = await SendCommand(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("SubUnsub: " + result);
        return false;
    }
    
    public override async Task<bool> OrderHistoricalDataAsync(Security symbol, TimeFrame tf, int count)
    {
        var command = "<command id=\"gethistorydata\"><security><board>" + symbol.Board +
            "</board><seccode>" + symbol.Seccode +"</seccode></security><period>" +
            tf.ID + "</period><count>" + count + "</count><reset>true</reset></command>";
        var result = await SendCommand(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("OrderHistoricalData: " + result);
        return false;
    }

    public override async Task<bool> OrderSecurityInfoAsync(Security symbol) =>
        await GetClnSecPermissions(symbol) && await GetSecurityInfo(symbol);

    private async Task<bool> GetSecurityInfo(Security symbol)
    {
        var command = "<command id=\"get_securities_info\"><security><market>" + symbol.Market +
            "</market><seccode>" + symbol.Seccode + "</seccode></security></command>";
        var result = await SendCommand(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("GetSecurityInfo: " + result);
        return false;
    }

    private async Task<bool> GetClnSecPermissions(Security symbol)
    {
        var client = Clients.SingleOrDefault(x => x.Market == symbol.Market);
        if (client == null)
        {
            AddInfo("GetClnSecPermissions: client is not found");
            return false;
        }

        var command = "<command id=\"get_cln_sec_permissions\"><security><board>" + symbol.Board + "</board>" +
            "<seccode>" + symbol.Seccode + "</seccode></security><client>" + client.ID + "</client></command>";
        var result = await SendCommand(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("GetClnSecPermissions: " + result);
        return false;
    }

    public override async Task<bool> OrderPortfolioInfoAsync(UnitedPortfolio portfolio)
    {
        var command = "<command id=\"get_mc_portfolio\" union=\"" + portfolio.Union +
            "\" currency=\"false\" asset=\"false\" money=\"false\" depo=\"true\" registers=\"false\"/>";
        var result = await SendCommand(command);
        if (result == "<result success=\"true\"/>") return true;
        AddInfo("OrderPortfolioInfo: " + result);
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
            if (SetCallback(CallbackDel))
            {
                isWorking = true;
                mainThread = new(ProcessDataQueue) { IsBackground = true, Name = "DataProcessor" };
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

    private async Task<string> SendCommand(string command)
    {
        if (Connection == ConnectionState.Disconnected) return "There is no connection";
        IntPtr nintCommand = Marshal.StringToHGlobalAnsi(command);
        var result = await SendCommand(nintCommand, command);
        Marshal.FreeHGlobal(nintCommand);
        return result;
    }

    private async Task<string> SendCommand(string firstPart, string lastPart, SecureString middlePart)
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

        var result = await SendCommand(command, firstPart);
        Marshal.FreeHGlobal(command);
        Marshal.ZeroFreeGlobalAllocAnsi(middle);
        Marshal.FreeHGlobal(first);
        Marshal.FreeHGlobal(last);
        return result;
    }
    
    private async Task<string> SendCommand(IntPtr command, string strCommand)
    {
        var result = "Empty";
        var sending = Task.Run(() =>
        {
            IntPtr res = SendCommand(command);
            result = Marshal.PtrToStringUTF8(res);
            FreeMemory(res);
        });

        try
        {
            await Task.Run(() =>
            {
                if (!sending.Wait(2000))
                {
                    TradingSystem.ReadyToTrade = false;
                    AddInfo("Server response timed out. Trading is suspended.", false);
                    if (!sending.Wait(waitingTime))
                    {
                        ServerAvailable = false;
                        if (Connection == ConnectionState.Connected)
                        {
                            Connection = ConnectionState.Connecting;
                            TriggerReconnection = DateTime.Now.AddSeconds(TradingSystem.Settings.SessionTM);
                        }
                        AddInfo("Server is not responding. Command: " + strCommand, false);

                        if (!sending.Wait(waitingTime * 15))
                        {
                            AddInfo("Infinitely waiting for a server response", notify: true);
                            sending.Wait();
                        }
                        AddInfo("Server is responding", false);
                        ServerAvailable = true;
                    }
                    else if (Connection == ConnectionState.Connected) TradingSystem.ReadyToTrade = true;
                }
            });
        }
        catch (Exception e) { AddInfo("Exception during sending command: " + e.Message); }
        finally { sending.Dispose(); }
        return result;
    }
    #endregion

    #region Import external methods
    [DllImport("txmlconnector64.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern bool SetCallback(CallBackDel callBack);

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
