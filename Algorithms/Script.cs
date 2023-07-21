using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace ProSystem.Algorithms;

public abstract class Script : INotifyPropertyChanged
{
    protected Order lastExecuted;
    protected PositionType curPosition;

    [field: NonSerialized] protected TextBlock infoBlock;
    [field: NonSerialized] public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

    public virtual string Name { get; set; }

    public virtual Order ActiveOrder { get; set; }

    public virtual Order LastExecuted
    {
        get => lastExecuted;
        set
        {
            lastExecuted = value;
            if (lastExecuted != null)
                CurrentPosition = lastExecuted.BuySell == "B" ? PositionType.Long : PositionType.Short;
        }
    }

    public virtual PositionType CurrentPosition
    {
        get => curPosition;
        set { curPosition = value; Notify(); }
    }

    public virtual ScriptResult Result { get; set; }

    public virtual TextBlock BlockInfo
    {
        get => infoBlock;
        set { infoBlock = value; Notify(); }
    }

    public virtual ObservableCollection<Order> MyOrders { get; set; }

    public virtual ObservableCollection<Trade> MyTrades { get; set; }

    public Script(string name) => Name = name;

    public abstract void Initialize(Tool MyTool, TabItem TabTool);

    public abstract void Calculate(Security Symbol);

    public override string ToString() => this.GetType().Name;

    protected void Notify(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
}
