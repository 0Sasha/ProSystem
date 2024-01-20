using System.ComponentModel;
using System.Globalization;
using System.Security;

namespace ProSystem;

public abstract class Connector : INotifyPropertyChanged
{
    protected readonly AddInformation AddInfo;
    protected readonly TradingSystem TradingSystem;
    protected readonly CultureInfo IC = CultureInfo.InvariantCulture;

    protected bool backupServer;
    protected ConnectionState connection = ConnectionState.Disconnected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual bool Initialized { get; protected set; }
    public virtual bool BackupServer
    {
        get => backupServer;
        set
        {
            backupServer = value;
            NotifyChange(nameof(BackupServer));
        }
    }
    public virtual bool ServerAvailable { get; set; }

    public virtual bool ReconnectTime { get => ServerTime.Hour == 0 && ServerTime.Minute is 15 or 16; }
    public virtual long UnixTime { get => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); }
    public virtual DateTime ServerTime { get => DateTime.UtcNow; }

    public ConnectionState Connection
    {
        get => connection;
        set
        {
            if (connection != value)
            {
                connection = value;
                if (connection == ConnectionState.Connecting)
                    ReconnectTrigger = ServerTime.AddSeconds(180);
                NotifyChange(nameof(Connection));
            }
        }
    }
    public virtual DateTime ReconnectTrigger { get; set; } = DateTime.UtcNow.AddDays(1);

    public virtual List<Security> Securities { get; protected set; } = [];
    public virtual List<Market> Markets { get; protected set; } = [];
    public virtual List<TimeFrame> TimeFrames { get; protected set; } = [];

    public virtual OrderType OrderTypeNM { get; set; } = OrderType.Limit;

    protected Connector(TradingSystem tradingSystem, AddInformation addInfo)
    {
        TradingSystem = tradingSystem ?? throw new ArgumentNullException(nameof(tradingSystem));
        AddInfo = addInfo ?? throw new ArgumentNullException(nameof(addInfo));
    }


    public virtual bool Initialize(int logLevel)
    {
        Initialized = true;
        return true;
    }

    public virtual bool Uninitialize()
    {
        Initialized = false;
        return true;
    }


    public abstract Task<bool> ConnectAsync(string login, SecureString password);

    public abstract Task<bool> DisconnectAsync();

    public virtual async Task ResetAsync()
    {
        BackupServer = false;
        await Task.CompletedTask;
    }


    public abstract Task<bool> SendOrderAsync(Security security, OrderType type, bool isBuy,
        double price, double quantity, string signal, Script? sender = null, string? note = null);

    public abstract Task<bool> ReplaceOrderAsync(Order activeOrder, Security security, OrderType type,
        double price, double quantity, string signal, Script? sender = null, string? note = null);

    public abstract Task<bool> CancelOrderAsync(Order activeOrder);


    public abstract Task<bool> SubscribeToTradesAsync(Security security);

    public abstract Task<bool> UnsubscribeFromTradesAsync(Security security);


    public async Task<bool> RequestBarsAsync(Tool tool)
    {
        if (tool.BasicSecurity != null)
        {
            if (!await OrderHistoricalDataAsync(tool.BasicSecurity, tool.BaseTF, true)) return false;
        }
        return await OrderHistoricalDataAsync(tool.Security, tool.BaseTF);
    }

    public async Task<bool> OrderHistoricalDataAsync(Security security, int minuteTF, bool basic = false, int count = 0)
    {
        if (TimeFrames == null || TimeFrames.Count == 0)
        {
            AddInfo("OrderHistoricalDataAsync: TimeFrames is empty", notify: true);
            return false;
        }

        if (count == 0)
        {
            var maxCount = basic ? 4000 : 3000;
            count = security.Bars == null || security.Bars.Close.Length < 300 ||
                security.Bars.DateTime[^1].AddHours(12) < ServerTime || minuteTF != security.Bars.TF ? maxCount : 25;
        }

        var tf = TimeFrames.SingleOrDefault(x => x.Minutes == minuteTF);
        if (tf == null)
        {
            tf = TimeFrames.Last(x => x.Minutes < minuteTF);
            count *= minuteTF / tf.Minutes;
        }

        return await OrderHistoricalDataAsync(security, tf, count, minuteTF);
    }

    protected abstract Task<bool> OrderHistoricalDataAsync(Security security, TimeFrame tf, int count, int baseTF);


    public abstract Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio);

    public virtual async Task<bool> OrderSecurityInfoAsync(Security security) => await Task.FromResult(true);


    public abstract Task<bool> PrepareForTradingAsync();

    public virtual async Task WaitForCertaintyAsync(Tool tool)
    {
        var lastTrade = TradingSystem.Trades.ToArray().LastOrDefault(x => x.Seccode == tool.Security.Seccode);
        if (lastTrade != null && lastTrade.Time.AddSeconds(2) > ServerTime) await Task.Delay(1500);
    }

    public virtual async Task<bool> CheckToolAsync(Tool tool)
    {
        if (!await CheckSecurityAsync(tool, tool.Security)) return false;
        if (tool.BasicSecurity != null && !await CheckSecurityAsync(tool, tool.BasicSecurity)) return false;

        if (tool.Scripts.Length > 2)
        {
            AddInfo(tool.Name + ": unexpected number of scripts: " + tool.Scripts.Length, notify: true);
            return false;
        }
        if (tool.StopTrading || !tool.UseNormalization)
            AddInfo(tool.Name + ": stopTrading or NM are not usual", notify: true);

        return CheckRequirements(tool.Security);
    }

    private async Task<bool> CheckSecurityAsync(Tool tool, Security security)
    {
        if (security.LastTrade.Time < ServerTime.AddDays(-5))
        {
            AddInfo(tool.Name + ": last trade is not actual. Subscribing.", notify: true);
            await SubscribeToTradesAsync(security);
            return false;
        }
        if (security.Bars == null || security.Bars.Close.Length < 200)
        {
            AddInfo(tool.Name + ": there is no enough bars. Request.", notify: true);
            await RequestBarsAsync(tool);
            return false;
        }
        return true;
    }

    protected abstract bool CheckRequirements(Security security);


    public virtual bool SecurityIsBidding(Security security) => security.LastTrade.Time.AddMinutes(1) > ServerTime;

    public abstract bool OrderIsActive(Order order);

    public abstract bool OrderIsExecuted(Order order);

    public abstract bool OrderIsTriggered(Order order);


    protected async Task WaitSentCommandAsync(Task sentCommand, string command, int shortMsTimeout, int longMsTimeout)
    {
        try
        {
            await Task.Run(() =>
            {
                if (!sentCommand.Wait(shortMsTimeout))
                {
                    TradingSystem.ReadyToTrade = false;
                    AddInfo("Server response timed out. Trading is suspended", false);
                    if (!sentCommand.Wait(longMsTimeout))
                    {
                        ServerAvailable = false;
                        if (Connection == ConnectionState.Connected)
                            Connection = ConnectionState.Connecting;
                        AddInfo("Server is not responding. Command: " + command, false);

                        if (!sentCommand.Wait(longMsTimeout * 15))
                        {
                            AddInfo("Infinitely waiting for a server response", notify: true);
                            sentCommand.Wait();
                        }
                        AddInfo("Server is responding", false);
                        ServerAvailable = true;
                    }
                    else if (Connection == ConnectionState.Connected) TradingSystem.ReadyToTrade = true;
                }
            });
        }
        catch (Exception e) { AddInfo("Exception during sending command: " + e.Message); }
        finally { sentCommand.Dispose(); }
    }

    protected void NotifyChange(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
