using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Security;
using System.Threading.Tasks;

namespace ProSystem;

public abstract class Connector : INotifyPropertyChanged
{
    public virtual bool Initialized { get; protected set; }
    public virtual bool BackupServer { get; set; }
    public virtual bool ServerAvailable { get; set; }
    public virtual ConnectionState Connection { get; set; }
    public virtual DateTime ReconnectionTrigger { get; set; } = DateTime.Now.AddMinutes(5);

    public virtual List<Security> Securities { get; protected set; } = new();
    public virtual List<Market> Markets { get; protected set; } = new();
    public virtual List<TimeFrame> TimeFrames { get; protected set; } = new();

    public virtual double USDRUB { get; set; }
    public virtual double EURRUB { get; set; }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void NotifyChange(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public abstract bool Initialize(int logLevel);

    public abstract bool Uninitialize();

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

    public abstract Task<bool> OrderSecurityInfoAsync(Security security);

    public abstract Task<bool> OrderPortfolioInfoAsync(Portfolio portfolio);
}
