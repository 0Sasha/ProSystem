using System;
using System.Collections.Generic;
using System.Linq;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace ProSystem;

internal static class PlotExtensions
{
    public static PlotController GetController()
    {
        var controller = new PlotController();
        controller.BindMouseDown(OxyMouseButton.Left, PlotCommands.PanAt);
        controller.BindMouseDown(OxyMouseButton.Right, PlotCommands.SnapTrack);
        return controller;
    }

    public static PlotModel GetPlot(this UnitedPortfolio portfolio)
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
        FactVolPorfolio.Items.Add(new BarItem { Value = portfolio.ShareInitReqs });
        MaxVolPorfolio.Items.Add(new BarItem { Value = portfolio.PotentialShareInitReqs });
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

    public static PlotModel GetPlot(this IEnumerable<Tool> tools, IEnumerable<Position> positions,
        double saldo, string filter, bool onlyWithPositions, bool excludeBaseBals)
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
                Position Pos = positions.SingleOrDefault(x => x.Seccode == MyTools[i].Security.Seccode);
                if (Pos != null && Math.Abs(Pos.Saldo) > 0.0001)
                {
                    int shift = excludeBaseBals ? MyTools[i].BaseBalance : 0;
                    double FactReq = Math.Abs((Pos.Saldo > 0 ? (Pos.Saldo - shift) * MyTools[i].Security.InitReqLong :
                        (-Pos.Saldo - shift) * MyTools[i].Security.InitReqShort) / saldo * 100);
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
                    MyTools[i].ShareOfFunds + (MyTools[i].BaseBalance * MyTools[i].Security.InitReqLong / saldo * 100) :
                    MyTools[i].ShareOfFunds + (-MyTools[i].BaseBalance * MyTools[i].Security.InitReqShort / saldo * 100))
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
}
