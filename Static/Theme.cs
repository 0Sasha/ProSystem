using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using System.Windows.Media;

namespace ProSystem;

internal static class Theme
{
    private static readonly OxyColorConverter converter = new();

    public static readonly SolidColorBrush Green = new() { Color = new() { R = 80, G = 210, B = 125, A = 255 } };
    public static readonly SolidColorBrush Red = new() { Color = new() { R = 255, G = 80, B = 100, A = 255 } };
    public static readonly SolidColorBrush Orange = new() { Color = new() { R = 255, G = 190, B = 0, A = 255 } };
    public static readonly SolidColorBrush Gray = Brushes.LightGray;

    private static readonly OxyColor greenOxy =
        (OxyColor)converter.ConvertBack(Green, typeof(OxyColor), null, null);

    private static readonly OxyColor redOxy =
        (OxyColor)converter.ConvertBack(Red, typeof(OxyColor), null, null);

    private static readonly OxyColor almostBlackOxy = (OxyColor)converter.
        ConvertBack(new Color() { R = 50, G = 50, B = 50, A = 255 }, typeof(OxyColor), null, null);

    private static readonly OxyColor darkGrayOxy = (OxyColor)converter.
        ConvertBack(new Color() { R = 90, G = 90, B = 90, A = 255 }, typeof(OxyColor), null, null);

    private static readonly OxyColor orangeOxy =
        (OxyColor)converter.ConvertBack(Orange, typeof(OxyColor), null, null);

    public static OxyColor Back { get; } = OxyColors.Black;
    public static OxyColor Front { get; } = OxyColors.White;
    public static OxyColor Text { get; } = OxyColors.LightGray;
    public static OxyColor Indicator { get; } = orangeOxy;
    public static OxyColor GreenBar { get; } = greenOxy;
    public static OxyColor RedBar { get; } = redOxy;
    public static OxyColor FadedBar { get; } = OxyColors.LightGray;
    public static OxyColor Gridline { get; } = almostBlackOxy;
    public static OxyColor[] Indicators { get; } =
    [
        greenOxy,
        redOxy,
        orangeOxy,
        OxyColors.SkyBlue
    ];

    public static OxyColor LongPosition { get; } = greenOxy;
    public static OxyColor ShortPosition { get; } = orangeOxy;
    public static OxyColor MaxVolume { get; } = darkGrayOxy;

    public static void Color(this PlotModel model)
    {
        model.Background = Back;
        model.PlotAreaBackground = Back;
        model.PlotAreaBorderColor = Front;
        model.TextColor = Text;
        model.TitleColor = Text;
        model.SubtitleColor = Text;
        model.SelectionColor = Text;
    }

    public static void Color(this Axis axis, bool mainModel = true)
    {
        axis.TextColor = Front;
        axis.TitleColor = Front;
        axis.AxislineColor = Front;
        axis.TicklineColor = Front;
        axis.MajorGridlineColor = Front;
        axis.MinorGridlineColor = Front;
        axis.MinorTicklineColor = Front;
        axis.ExtraGridlineColor = mainModel ? Gridline : OxyColors.LightGray;
    }

    public static void Color(this CandleStickSeries candles, OxyColor? decreasingColor)
    {
        candles.Color = Front;
        candles.TextColor = Text;
        candles.IncreasingColor = GreenBar;
        candles.DecreasingColor = decreasingColor ?? RedBar;
    }
}
