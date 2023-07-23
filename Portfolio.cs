using System;
using System.Linq;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using static ProSystem.MainWindow;
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
            inReqs = value;
            ShareInitReqs = Math.Round(inReqs / Saldo * 100, 2);
            Task.Run(() =>
            {
                UpdatePositions();
                Notify(nameof(InitReqs));
            });
        }
    } // Начальные требования
    public double MinReqs
    {
        get => minReqs;
        set
        {
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

    public double ShareBaseAssets { get; private set; } // Доля базовых активов
    public double ShareInitReqsBaseAssets { get; private set; } // Доля начальных требований базовых активов
    public double ShareInitReqs { get; private set; } // Доля начальных требований
    public double ShareMinReqs { get; private set; } // Доля минимальных требования
    public double PotentialShareInitReqs { get; private set; } // Потенциальная доля начальных требований

    public Dictionary<DateTime, int> Equity { get; set; } = new();
    public int AverageEquity
    {
        get
        {
            if (Equity == null || Equity.Count == 0) return 500000;
            if (Equity.Count > 4) return (int)Equity.TakeLast(5).Select(x => x.Value).Average();
            return Equity.Last().Value;
        }
    }

    public List<Position> Positions { get; set; } = new();
    public List<Position> MoneyPositions { get; set; } = new();
    public ObservableCollection<object> AllPositions { get; set; } = new();

    public UnitedPortfolio() { }

    [field: NonSerialized] public event PropertyChangedEventHandler PropertyChanged;

    private void Notify(string propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void UpdateSharesAndCheck(Tool[] tools, Settings settings)
    {
        double sumPotInitReqs = 0;
        double sumInitReqsBaseAssets = 0;
        double sumReqsBaseAssets = 0;
        foreach (var tool in tools)
        {
            if (tool.Active)
            {
                if (tool.TradeShare)
                {
                    var inReqs = Saldo / 100 * tool.ShareOfFunds;
                    sumPotInitReqs += inReqs;
                }
                else sumPotInitReqs += tool.NumberOfLots * Math.Max(tool.MySecurity.InitReqLong, tool.MySecurity.InitReqShort);

                if (tool.UseShiftBalance)
                {
                    sumReqsBaseAssets += tool.BaseBalance *
                        (tool.MySecurity.LastTrade.Price / tool.MySecurity.MinStep * tool.MySecurity.MinStepCost);

                    var inReqsBaseAssets = tool.BaseBalance > 0 ?
                        tool.BaseBalance * tool.MySecurity.InitReqLong : -tool.BaseBalance * tool.MySecurity.InitReqShort;

                    sumPotInitReqs += inReqsBaseAssets;
                    sumInitReqsBaseAssets += inReqsBaseAssets;
                }
            }
        }

        PotentialShareInitReqs = Math.Round(sumPotInitReqs / Saldo * 100, 2);
        ShareBaseAssets = Math.Round(sumReqsBaseAssets / Saldo * 100, 2);
        ShareInitReqsBaseAssets = Math.Round(sumInitReqsBaseAssets / Saldo * 100, 2);

        if (ShareBaseAssets > settings.OptShareBaseAssets + settings.ToleranceBaseAssets ||
            ShareBaseAssets < settings.OptShareBaseAssets - settings.ToleranceBaseAssets)
            AddInfo("Portfolio: Доля базовых активов за пределами допустимого отклонения: " +
                ShareBaseAssets + "%", notify: true);

        if (PotentialShareInitReqs > settings.MaxShareInitReqsPortfolio)
            AddInfo("Portfolio: Потенциальные начальные требования портфеля превышают норму: " +
                settings.MaxShareInitReqsPortfolio + "%. PotentialInitReqs: " + PotentialShareInitReqs + "%", notify: true);
    }
    public void UpdatePositions()
    {
        try
        {
            AllPositions = new(MoneyPositions.Concat(Positions.OrderBy(x => x.ShortName))) { this };
            Window.Dispatcher.Invoke(() =>
            {
                Window.PortfolioView.ItemsSource = AllPositions;
                Window.PortfolioView.ScrollIntoView(this);
            });
        }
        catch (Exception ex) { AddInfo("UpdatePositions исключение: " + ex.Message); }
    }
    public void ClearOldPositions() => Positions.RemoveAll(x => x.Saldo == 0);
    public void UpdateEquity(DateTime dateTimeOpenPeriod)
    {
        Equity[dateTimeOpenPeriod] = (int)Portfolio.Saldo;
        Notify();
    }
    public bool CheckEquity(int toleranceEquity)
    {
        int range = AverageEquity / 100 * toleranceEquity;
        if (Saldo < AverageEquity - range || Saldo > AverageEquity + range)
        {
            AddInfo("Стоимость портфеля за пределами допустимого отклонения.", notify: true);
            return false;
        }
        return true;
    }

    public PlotModel GetDistributionPlot(IEnumerable<Tool> tools, string filter, bool onlyWithPositions, bool excludeBaseBals)
    {
        var Assets = new CategoryAxis { Position = AxisPosition.Left };
        var FactVol = new BarSeries { BarWidth = 4, StrokeThickness = 0 };
        var MaxVol = new BarSeries { FillColor = Theme.MaxVolume, StrokeThickness = 0 };
        var Axis = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = new double[]
            {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15,
                16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30
            },
        };

        Tool[] MyTools = tools.Where(x => x.Active).ToArray();
        for (int i = MyTools.Length - 1; i >= 0; i--)
        {
            if (filter == "All tools" ||
                filter == "First part" && i < MyTools.Length / 2 || filter == "Second part" && i >= MyTools.Length / 2)
            {
                Position Pos = Positions.SingleOrDefault(x => x.Seccode == MyTools[i].MySecurity.Seccode);
                if (Pos != null && Math.Abs(Pos.Saldo) > 0.0001)
                {
                    int shift = excludeBaseBals ? MyTools[i].BaseBalance : 0;
                    double FactReq = Math.Abs((Pos.Saldo > 0 ? (Pos.Saldo - shift) * MyTools[i].MySecurity.InitReqLong :
                        (-Pos.Saldo - shift) * MyTools[i].MySecurity.InitReqShort) / Saldo * 100);
                    FactVol.Items.Add(new BarItem
                    {
                        Value = FactReq,
                        Color = Pos.Saldo - shift > 0 ? Theme.LongPosition : Theme.ShortPosition
                    });
                }
                else if (!onlyWithPositions) FactVol.Items.Add(new BarItem { Value = 0 });
                else continue;

                Assets.Labels.Add(MyTools[i].Name);
                MaxVol.Items.Add(new BarItem
                {
                    Value = excludeBaseBals || !MyTools[i].UseShiftBalance ?
                    MyTools[i].ShareOfFunds : (MyTools[i].BaseBalance > 0 ?
                    MyTools[i].ShareOfFunds + (MyTools[i].BaseBalance * MyTools[i].MySecurity.InitReqLong / Saldo * 100) :
                    MyTools[i].ShareOfFunds + (-MyTools[i].BaseBalance * MyTools[i].MySecurity.InitReqShort / Saldo * 100))
                });
            }
        }
        Theme.Color(Assets);
        Theme.Color(Axis);

        var Model = new PlotModel();
        Model.Series.Add(MaxVol);
        Model.Series.Add(FactVol);
        Model.Axes.Add(Assets);
        Model.Axes.Add(Axis);
        Model.PlotMargins = new OxyThickness(55, 0, 0, 20);

        Theme.Color(Model);
        return Model;
    }
    public PlotModel GetPortfolioPlot()
    {
        var AssetsPorfolio = new CategoryAxis { Position = AxisPosition.Left };
        var FactVolPorfolio = new BarSeries { BarWidth = 4, FillColor = Theme.ShortPosition, StrokeThickness = 0 };
        var MaxVolPorfolio = new BarSeries { FillColor = Theme.MaxVolume, StrokeThickness = 0 };
        var AxisPorfolio = new LinearAxis
        {
            Position = AxisPosition.Bottom,
            MinimumPadding = 0,
            MaximumPadding = 0.1,
            AbsoluteMinimum = 0,
            AbsoluteMaximum = 250,
            ExtraGridlines = new double[]
            {
                5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95
            },
        };

        AssetsPorfolio.Labels.Add("Portfolio");
        FactVolPorfolio.Items.Add(new BarItem { Value = ShareInitReqs });
        MaxVolPorfolio.Items.Add(new BarItem { Value = PotentialShareInitReqs });
        Theme.Color(AssetsPorfolio);
        Theme.Color(AxisPorfolio);

        var Model = new PlotModel();
        Model.Series.Add(MaxVolPorfolio);
        Model.Series.Add(FactVolPorfolio);
        Model.Axes.Add(AssetsPorfolio);
        Model.Axes.Add(AxisPorfolio);
        Model.PlotMargins = new OxyThickness(55, 0, 0, 20);
        Theme.Color(Model);
        return Model;
    }
}
