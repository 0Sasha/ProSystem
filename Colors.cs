using OxyPlot;
using OxyPlot.Wpf;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Windows.Media;
using static ProSystem.MainDictionary;
namespace ProSystem;

static class Colors
{
    private static readonly SolidColorBrush lightGreen = new() { Color = new() { R = 90, G = 235, B = 140, A = 220 } };
    private static readonly SolidColorBrush lightGreen2 = new() { Color = new() { R = 80, G = 180, B = 120, A = 255 } };
    private static readonly SolidColorBrush lightRed = new() { Color = new() { R = 255, G = 80, B = 100, A = 255 } };
    private static readonly SolidColorBrush orange = new() { Color = new() { R = 255, G = 190, B = 0, A = 255 } };

    public static bool DarkTheme { get; set; } = true;
    public static SolidColorBrush Back { get => Dictionary.txtBack; }
    public static SolidColorBrush Front { get => Dictionary.txtFront; }
    public static SolidColorBrush Border { get => Dictionary.txtBorder; }
    public static SolidColorBrush Green { get => DarkTheme ? lightGreen : Brushes.Green; }
    public static SolidColorBrush Red { get => DarkTheme ? lightRed : Brushes.Red; }
    public static SolidColorBrush Orange { get => orange; }
    public static SolidColorBrush Gray { get => DarkTheme ? Brushes.LightGray : Brushes.DarkGray; }
}
static class PlotColors
{
    private static readonly OxyColorConverter converter = new();

    private static readonly OxyColor greenBar =
        (OxyColor)converter.ConvertBack(new Color() { R = 90, G = 235, B = 140, A = 220 }, typeof(OxyColor), null, null);
    private static readonly OxyColor redBar =
        (OxyColor)converter.ConvertBack(new Color() { R = 255, G = 80, B = 100, A = 255 }, typeof(OxyColor), null, null);
    private static readonly OxyColor almostBlack =
        (OxyColor)converter.ConvertBack(new Color() { R = 50, G = 50, B = 50, A = 255 }, typeof(OxyColor), null, null);
    private static readonly OxyColor darkGray =
        (OxyColor)converter.ConvertBack(new Color() { R = 90, G = 90, B = 90, A = 255 }, typeof(OxyColor), null, null);
    private static readonly OxyColor longPos =
        (OxyColor)converter.ConvertBack(new Color() { R = 80, G = 180, B = 120, A = 255 }, typeof(OxyColor), null, null);
    private static readonly OxyColor orange = (OxyColor)converter.ConvertBack(Colors.Orange, typeof(OxyColor), null, null);

    private static readonly OxyColor[] blackIndicators = new OxyColor[]
    {
        greenBar, redBar,
        orange, OxyColors.SkyBlue
    };
    private static readonly OxyColor[] whiteIndicators = new OxyColor[]
    {
        OxyColors.Green, OxyColors.Red,
        OxyColors.DarkGoldenrod, OxyColors.DarkBlue
    };

    public static OxyColor Back { get => Colors.DarkTheme ? OxyColors.Black : OxyColors.White; }
    public static OxyColor Front { get => Colors.DarkTheme ? OxyColors.White : OxyColors.Black; }
    public static OxyColor Text { get => Colors.DarkTheme ? OxyColors.LightGray : OxyColors.Black; }
    public static OxyColor Indicator { get => Colors.DarkTheme ? orange : OxyColors.DarkBlue; }
    public static OxyColor GreenBar { get => Colors.DarkTheme ? greenBar : OxyColors.Green; }
    public static OxyColor RedBar { get => Colors.DarkTheme ? redBar : OxyColors.Red; }
    public static OxyColor FadedBar { get => Colors.DarkTheme ? OxyColors.LightGray : OxyColors.Black; }
    public static OxyColor Gridline { get => Colors.DarkTheme ? almostBlack : OxyColors.LightGray; }
    public static OxyColor[] Indicators { get => Colors.DarkTheme ? blackIndicators : whiteIndicators; }

    public static OxyColor LongPosition { get => Colors.DarkTheme ? greenBar : OxyColors.Green; }
    public static OxyColor ShortPosition { get => Colors.DarkTheme ? OxyColors.Orange : OxyColors.DarkGoldenrod; }
    public static OxyColor MaxVolume { get => Colors.DarkTheme ? darkGray : OxyColors.Black; }

    public static void Color(PlotModel model)
    {
        model.Background = Back;
        model.PlotAreaBackground = Back;
        model.PlotAreaBorderColor = Front;
        model.TextColor = Text;
        model.TitleColor = Text;
        model.SubtitleColor = Text;
        model.SelectionColor = Text;
    }
    public static void Color(Axis axis, bool mainModel = true)
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
    public static void Color(CandleStickSeries candles)
    {
        candles.Color = Front;
        candles.TextColor = Text;
        candles.IncreasingColor = GreenBar;
        candles.DecreasingColor = RedBar;
    }
}
