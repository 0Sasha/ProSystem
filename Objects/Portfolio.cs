using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ProSystem;

[Serializable]
public class UnitedPortfolio : INotifyPropertyChanged
{
    private double inReqs;
    private double minReqs;

    public string Union { get; set; } // Код юниона
    public double SaldoIn { get; set; } // Входящая оценка стоимости единого портфеля
    public double Saldo { get; set; } // Текущая оценка стоимости единого портфеля
    public double PL { get; set; } // Прибыль/убыток общий
    public double InitReqs
    {
        get => inReqs;
        set
        {
            if (value < 0) throw new ArgumentException("InitReqs is < 0");
            inReqs = value;
            ShareInitReqs = Math.Round(inReqs / Saldo * 100, 2);
            Notify(nameof(InitReqs));
        }
    } // Начальные требования
    public double MinReqs
    {
        get => minReqs;
        set
        {
            if (value < 0) throw new ArgumentException("MinReqs is < 0");
            minReqs = value;
            ShareMinReqs = Math.Round(minReqs / Saldo * 100, 2);
            Notify(nameof(MinReqs));
        }
    } // Минимальные требования
    public double Free { get; set; } // Свободные средства
    public double UnrealPL { get; set; } // Нереализованная прибыль/убыток
    public double GO { get; set; } // Размер требуемого ГО FORTS
    public double VarMargin { get; set; } // Вариационная маржа FORTS
    public double FinRes { get; set; } // Финансовый результат последнего клиринга FORTS

    public double ShareBaseAssets { get; set; } // Доля базовых активов
    public double ShareInitReqsBaseAssets { get; set; } // Доля начальных требований базовых активов
    public double ShareInitReqs { get; set; } // Доля начальных требований
    public double ShareMinReqs { get; set; } // Доля минимальных требования
    public double PotentialShareInitReqs { get; set; } // Потенциальная доля начальных требований

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

    public UnitedPortfolio() { }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

    public void Notify(string propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
