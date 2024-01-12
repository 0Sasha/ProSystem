using Newtonsoft.Json;
using System.ComponentModel;

namespace ProSystem;

[Serializable]
public class Position : INotifyPropertyChanged
{
    private string seccode;
    private string? shortName;
    private double saldoIn;
    private double saldo;
    private double pl;
    private double unrealPL;
    private double initReqs;
    private double minReqs;

    [field: NonSerialized] public event PropertyChangedEventHandler? PropertyChanged;

    public string Seccode
    {
        get => seccode;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value, nameof(Seccode));
            seccode = value;
            NotifyChange(nameof(Seccode));
        }
    }

    public string? ShortName
    {
        get => shortName;
        set
        {
            shortName = value;
            NotifyChange(nameof(ShortName));
        }
    }

    public double SaldoIn
    {
        get => saldoIn;
        set
        {
            saldoIn = value;
            NotifyChange(nameof(SaldoIn));
        }
    }

    public double Saldo
    {
        get => saldo;
        set
        {
            saldo = value;
            NotifyChange(nameof(Saldo));
        }
    }

    public double PL
    {
        get => pl;
        set
        {
            pl = value;
            NotifyChange(nameof(PL));
        }
    }

    public double UnrealPL
    {
        get => unrealPL;
        set
        {
            unrealPL = value;
            NotifyChange(nameof(UnrealPL));
        }
    }

    public double InitReqs
    {
        get => initReqs;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(InitReqs));
            initReqs = value;
            NotifyChange(nameof(InitReqs));
        }
    }

    public double MinReqs
    {
        get => minReqs;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(MinReqs));
            minReqs = value;
            NotifyChange(nameof(MinReqs));
        }
    }

    [JsonConstructor]
    public Position(string seccode)
    {
        ArgumentException.ThrowIfNullOrEmpty(seccode, nameof(seccode));
        this.seccode = seccode;
    }

    private void NotifyChange(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
