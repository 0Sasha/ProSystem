using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace ProSystem;

[Serializable]
public abstract class Script : INotifyPropertyChanged
{
    protected Order lastExecuted;
    protected PositionType curPosition;

    [field: NonSerialized] protected TextBlock infoBlock;
    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

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

    public virtual ObservableCollection<Order> MyOrders { get; set; } = new();

    public virtual ObservableCollection<Trade> MyTrades { get; set; } = new();

    public Script(string name) => Name = name;

    public abstract void Initialize(Tool myTool, TabItem tabTool);

    public abstract void Calculate(Security symbol);

    public override string ToString() => GetType().Name;

    protected virtual void Notify(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
