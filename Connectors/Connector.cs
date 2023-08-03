using System;
using System.Collections.Generic;

namespace ProSystem;

public abstract class Connector
{
    public virtual bool Initialized { get; set; }
    public virtual bool BackupServer { get; set; }
    public virtual bool ServerAvailable { get; set; }
    public virtual ConnectionState Connection { get; set; }
    public virtual DateTime TriggerReconnection { get; set; }

    public virtual List<Security> Securities { get; set; }
    public virtual List<Market> Markets { get; set; }
    public virtual List<TimeFrame> TimeFrames { get; set; }

    public virtual double USDRUB { get; set; }
    public virtual double EURRUB { get; set; }

    public abstract bool Initialize(int logLevel);

    public abstract bool Uninitialize();

    public abstract void Connect(bool scheduled); // async

    public abstract void Disconnect(bool scheduled); // async

    public abstract bool SendOrder(Security security, OrderType type, bool isBuy,
        double price, int quantity, string signal, Script sender = null, string note = null);

    public abstract bool ReplaceOrder(Order activeOrder, Security security, OrderType type,
        double price, int quantity, string signal, Script sender = null, string note = null);

    public abstract bool CancelOrder(Order activeOrder);

    public abstract void SubscribeToTrades(Security security);

    public abstract void UnsubscribeFromTrades(Security security);

    public abstract void OrderHistoricalData(Security security, TimeFrame tf, int count);

    public abstract void OrderSecurityInfo(Security security);

    public abstract void OrderPortfolioInfo(UnitedPortfolio portfolio);
}
