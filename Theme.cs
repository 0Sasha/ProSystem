﻿using OxyPlot;
using OxyPlot.Wpf;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Windows.Media;

namespace ProSystem
{
    static class Theme
    {
        private static bool black = true;
        private static readonly OxyColorConverter converter = new();
        private static readonly OxyColor greenBar =
            (OxyColor)converter.ConvertBack(new Color() { R = 90, G = 235, B = 140, A = 220 }, typeof(OxyColor), null, null);
        private static readonly OxyColor redBar =
            (OxyColor)converter.ConvertBack(new Color() { R = 255, G = 80, B = 100, A = 255 }, typeof(OxyColor), null, null);
        private static readonly OxyColor darkGray =
            (OxyColor)converter.ConvertBack(new Color() { R = 50, G = 50, B = 50, A = 255 }, typeof(OxyColor), null, null);

        public static bool Black
        {
            get => black;
            set
            {
                black = value;
            }
        }

        public static OxyColor Back { get => Black ? OxyColors.Black : OxyColors.White; }
        public static OxyColor Front { get => Black ? OxyColors.White : OxyColors.Black; }
        public static OxyColor Text { get => Black ? OxyColors.LightGray : OxyColors.Black; }
        public static OxyColor Indicator { get => Black ? OxyColors.DarkGoldenrod : OxyColors.DarkBlue; }
        public static OxyColor GreenBar { get => Black ? greenBar : OxyColors.Green; }
        public static OxyColor RedBar { get => Black ? redBar : OxyColors.Red; }
        public static OxyColor FadedBar { get => Black ? OxyColors.LightGray : OxyColors.Black; }
        public static OxyColor Gridline { get => Black ? darkGray : OxyColors.LightGray; }

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
        public static void Color(Axis axis)
        {
            axis.TextColor = Front;
            axis.TitleColor = Front;
            axis.AxislineColor = Front;
            axis.TicklineColor = Front;
            axis.MajorGridlineColor = Front;
            axis.MinorGridlineColor = Front;
            axis.MinorTicklineColor = Front;
            axis.ExtraGridlineColor = Gridline;
        }
        public static void Color(CandleStickSeries candles)
        {
            candles.Color = Front;
            candles.TextColor = Text;
            candles.IncreasingColor = GreenBar;
            candles.DecreasingColor = RedBar;
        }
    }
}
