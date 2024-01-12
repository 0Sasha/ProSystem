using System.Collections.ObjectModel;
using System.ComponentModel;

namespace ProSystem;

[Serializable]
public class Portfolio : INotifyPropertyChanged
{
    private string? union;
    private double saldoIn;
    private double saldo;
    private double pl;
    private double unrealPL;
    private double free;
    private double initReqs;
    private double minReqs;
    private double varMargin;
    private double finRes;

    private double shareBaseAssets;
    private double shareInitReqsBaseAssets;
    private double shareInitReqs;
    private double shareMinReqs;
    private double potentialShareInitReqs;

    [field: NonSerialized] public event PropertyChangedEventHandler? PropertyChanged;

    public string? Union
    {
        get => union;
        set
        {
            union = value;
            NotifyChange(nameof(Union));
        }
    }
    public double SaldoIn
    {
        get => saldoIn;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(SaldoIn));
            saldoIn = value;
            NotifyChange(nameof(SaldoIn));
        }
    }
    public double Saldo
    {
        get => saldo;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(Saldo));
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
            ShareInitReqs = Math.Round(initReqs / Saldo * 100, 2);
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
            ShareMinReqs = Math.Round(minReqs / Saldo * 100, 2);
            NotifyChange(nameof(MinReqs));
        }
    }
    public double Free
    {
        get => free;
        set
        {
            free = value;
            NotifyChange(nameof(Free));
        }
    }
    public double VarMargin
    {
        get => varMargin;
        set
        {
            varMargin = value;
            NotifyChange(nameof(VarMargin));
        }
    }
    public double FinRes
    {
        get => finRes;
        set
        {
            finRes = value;
            NotifyChange(nameof(FinRes));
        }
    }

    public double ShareBaseAssets
    {
        get => shareBaseAssets;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(ShareBaseAssets));
            shareBaseAssets = value;
            NotifyChange(nameof(ShareBaseAssets));
        }
    }
    public double ShareInitReqsBaseAssets
    {
        get => shareInitReqsBaseAssets;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(ShareInitReqsBaseAssets));
            shareInitReqsBaseAssets = value;
            NotifyChange(nameof(ShareInitReqsBaseAssets));
        }
    }
    public double ShareInitReqs
    {
        get => shareInitReqs;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(ShareInitReqs));
            shareInitReqs = value;
            NotifyChange(nameof(ShareInitReqs));
        }
    }
    public double ShareMinReqs
    {
        get => shareMinReqs;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(ShareMinReqs));
            shareMinReqs = value;
            NotifyChange(nameof(ShareMinReqs));
        }
    }
    public double PotentialShareInitReqs
    {
        get => potentialShareInitReqs;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value, nameof(PotentialShareInitReqs));
            potentialShareInitReqs = value;
            NotifyChange(nameof(PotentialShareInitReqs));
        }
    }

    public Dictionary<DateTime, double> Equity { get; set; } = [];
    public double AverageEquity
    {
        get
        {
            if (Equity == null || Equity.Count == 0) return Saldo;
            if (Equity.Count > 4) return Equity.TakeLast(5).Select(x => x.Value).Average();
            return Equity.Last().Value;
        }
    }

    public List<Position> Positions { get; set; } = [];
    public List<Position> MoneyPositions { get; set; } = [];
    public ObservableCollection<object> AllPositions { get; set; } = [];

    public Portfolio() { }

    public void NotifyChange(string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
