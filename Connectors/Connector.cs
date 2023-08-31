using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace ProSystem;

public abstract class Connector : INotifyPropertyChanged
{
    protected readonly AddInformation AddInfo;
    protected readonly TradingSystem TradingSystem;
    protected readonly CultureInfo IC = CultureInfo.InvariantCulture;

    protected bool backupServer;
    protected ConnectionState connection = ConnectionState.Disconnected;

    public event PropertyChangedEventHandler PropertyChanged;

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
    public virtual ConnectionState Connection
    {
        get => connection;
        set
        {
            if (connection != value)
            {
                connection = value;
                NotifyChange(nameof(Connection));
            }
        }
    }
    public virtual DateTime ReconnectionTrigger { get; set; } = DateTime.Now.AddMinutes(5);

    public virtual List<Security> Securities { get; protected set; } = new();
    public virtual List<Market> Markets { get; protected set; } = new();
    public virtual List<TimeFrame> TimeFrames { get; protected set; } = new();

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


    public abstract Task<bool> SendOrderAsync(Security security, OrderType type, bool isBuy,
        double price, int quantity, string signal, Script sender = null, string note = null);

    public abstract Task<bool> ReplaceOrderAsync(Order activeOrder, Security security, OrderType type,
        double price, int quantity, string signal, Script sender = null, string note = null);

    public abstract Task<bool> CancelOrderAsync(Order activeOrder);


    public abstract Task<bool> SubscribeToTradesAsync(Security security);

    public abstract Task<bool> UnsubscribeFromTradesAsync(Security security);

    public abstract Task<bool> OrderHistoricalDataAsync(Security security, TimeFrame tf, int count);

    public abstract Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio);


    public virtual async Task<bool> OrderSecurityInfoAsync(Security security) => await Task.FromResult(true);

    public virtual async Task<bool> OrderSpecificPreTradingData() => await Task.FromResult(true);


    public abstract bool SecurityIsBidding(Security security);

    public abstract bool CheckRequirements(Security security);


    protected async Task WaitSentCommandAsync(Task sentCommand, string command, int shortMsTimeout, int longMsTimeout)
    {
        try
        {
            await sentCommand.WaitAsync(TimeSpan.FromMilliseconds(shortMsTimeout));
            if (!sentCommand.IsCompleted)
            {
                var initReadyToTrade = TradingSystem.ReadyToTrade;
                TradingSystem.ReadyToTrade = false;
                AddInfo("Server response timed out. Trading is suspended.", false);
                await sentCommand.WaitAsync(TimeSpan.FromMilliseconds(longMsTimeout));
                if (!sentCommand.IsCompleted)
                {
                    ServerAvailable = false;
                    if (Connection == ConnectionState.Connected)
                    {
                        Connection = ConnectionState.Connecting;
                        ReconnectionTrigger = DateTime.Now.AddSeconds(TradingSystem.Settings.SessionTM);
                    }
                    AddInfo("Server is not responding. Command: " + command, false);

                    await sentCommand.WaitAsync(TimeSpan.FromMilliseconds(longMsTimeout * 15));
                    if (!sentCommand.IsCompleted)
                    {
                        AddInfo("Infinitely waiting for a server response", notify: true);
                        await sentCommand.WaitAsync(CancellationToken.None);
                    }
                    AddInfo("Server is responding", false);
                    ServerAvailable = true;
                }
                else if (Connection == ConnectionState.Connected) TradingSystem.ReadyToTrade = initReadyToTrade;
            }
        }
        catch (Exception e) { AddInfo("Exception during sending command: " + e.Message); }
        finally { sentCommand.Dispose(); }
    }

    protected void NotifyChange(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
