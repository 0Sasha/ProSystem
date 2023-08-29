using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ProSystem;

[Serializable]
public class Portfolio : INotifyPropertyChanged
{
    private string union;
    private double saldoIn;
    private double saldo;
    private double pl;
    private double initReqs;
    private double minReqs;
    private double free;
    private double unrealPL;
    private double go;
    private double varMargin;
    private double finRes;

    private double shareBaseAssets;
    private double shareInitReqsBaseAssets;
    private double shareInitReqs;
    private double shareMinReqs;
    private double potentialShareInitReqs;

    public string Union
    {
        get => union;
        set
        {
            union = value;
            NotifyChange(nameof(Union));
        }
    } // Код юниона
    public double SaldoIn
    {
        get => saldoIn;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(SaldoIn));
            saldoIn = value;
            NotifyChange(nameof(SaldoIn));
        }
    } // Входящая оценка стоимости единого портфеля
    public double Saldo
    {
        get => saldo;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(Saldo));
            saldo = value;
            NotifyChange(nameof(Saldo));
        }
    } // Текущая оценка стоимости единого портфеля
    public double PL
    {
        get => pl;
        set
        {
            pl = value;
            NotifyChange(nameof(PL));
        }
    } // Прибыль/убыток общий
    public double InitReqs
    {
        get => initReqs;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(InitReqs));
            initReqs = value;
            ShareInitReqs = Math.Round(initReqs / Saldo * 100, 2);
            NotifyChange(nameof(InitReqs));
        }
    } // Начальные требования
    public double MinReqs
    {
        get => minReqs;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(MinReqs));
            minReqs = value;
            ShareMinReqs = Math.Round(minReqs / Saldo * 100, 2);
            NotifyChange(nameof(MinReqs));
        }
    } // Минимальные требования
    public double Free
    {
        get => free;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(Free));
            free = value;
            NotifyChange(nameof(Free));
        }
    } // Свободные средства
    public double UnrealPL
    {
        get => unrealPL;
        set
        {
            unrealPL = value;
            NotifyChange(nameof(UnrealPL));
        }
    } // Нереализованная прибыль/убыток
    public double GO
    {
        get => go;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(GO));
            go = value;
            NotifyChange(nameof(GO));
        }
    } // Размер требуемого ГО FORTS
    public double VarMargin
    {
        get => varMargin;
        set
        {
            varMargin = value;
            NotifyChange(nameof(VarMargin));
        }
    } // Вариационная маржа FORTS
    public double FinRes
    {
        get => finRes;
        set
        {
            finRes = value;
            NotifyChange(nameof(FinRes));
        }
    } // Финансовый результат последнего клиринга FORTS

    public double ShareBaseAssets
    {
        get => shareBaseAssets;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(ShareBaseAssets));
            shareBaseAssets = value;
            NotifyChange(nameof(ShareBaseAssets));
        }
    } // Доля базовых активов
    public double ShareInitReqsBaseAssets
    {
        get => shareInitReqsBaseAssets;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(ShareInitReqsBaseAssets));
            shareInitReqsBaseAssets = value;
            NotifyChange(nameof(ShareInitReqsBaseAssets));
        }
    } // Доля начальных требований базовых активов
    public double ShareInitReqs
    {
        get => shareInitReqs;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(ShareInitReqs));
            shareInitReqs = value;
            NotifyChange(nameof(ShareInitReqs));
        }
    } // Доля начальных требований
    public double ShareMinReqs
    {
        get => shareMinReqs;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(ShareMinReqs));
            shareMinReqs = value;
            NotifyChange(nameof(ShareMinReqs));
        }
    } // Доля минимальных требования
    public double PotentialShareInitReqs
    {
        get => potentialShareInitReqs;
        set
        {
            if (value < -0.00000001) throw new ArgumentOutOfRangeException(nameof(PotentialShareInitReqs));
            potentialShareInitReqs = value;
            NotifyChange(nameof(PotentialShareInitReqs));
        }
    } // Потенциальная доля начальных требований

    public Dictionary<DateTime, int> Equity { get; set; } = new();
    public int AverageEquity
    {
        get
        {
            if (Equity == null || Equity.Count == 0) return 0;
            if (Equity.Count > 4) return (int)Equity.TakeLast(5).Select(x => x.Value).Average();
            return Equity.Last().Value;
        }
    }

    public List<Position> Positions { get; set; } = new();
    public List<Position> MoneyPositions { get; set; } = new();
    public ObservableCollection<object> AllPositions { get; set; } = new();

    public Portfolio() { }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

    public void NotifyChange(string propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
