using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Controls;

namespace ProSystem;

[Serializable]
public abstract class Script : INotifyPropertyChanged
{
    protected Order lastExecuted;
    protected PositionType curPosition;
    protected ScriptProperties properties;

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
        set { curPosition = value; NotifyChange(); }
    }

    public virtual ScriptResult Result { get; set; }

    public virtual TextBlock BlockInfo
    {
        get => infoBlock;
        set { infoBlock = value; NotifyChange(); }
    }

    public virtual ObservableCollection<Order> Orders { get; set; } = new();

    public virtual ObservableCollection<Trade> Trades { get; set; } = new();

    public ScriptProperties Properties { get => properties; }

    public Script(string name) => Name = name;

    public abstract void Calculate(Security symbol);

    public override string ToString() => GetType().Name;

    protected virtual void NotifyChange(string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
