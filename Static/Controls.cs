using OxyPlot.SkiaSharp.Wpf;
using OxyPlot.Wpf;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ProSystem;

internal static class Controls
{
    public static TextBlock GetTextBlock(string property, double left, double top)
    {
        return new()
        {
            Text = property.Length > 14 ? property[0..14] : property,
            Margin = new Thickness(left, top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    public static TextBox GetTextBox(object sourceBinding, string property, double left, double top)
    {
        TextBox textBox = new()
        {
            Width = 30,
            Margin = new Thickness(left, top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Binding Binding = new() { Source = sourceBinding, Path = new PropertyPath(property), Mode = BindingMode.TwoWay };
        textBox.SetBinding(TextBox.TextProperty, Binding);
        return textBox;
    }

    public static CheckBox GetCheckBox(object sourceBinding, string boxName, string property, double left, double top)
    {
        CheckBox checkBox = new()
        {
            Content = boxName.Length > 14 ? boxName[0..14] : boxName,
            Margin = new Thickness(left, top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Binding Binding = new() { Source = sourceBinding, Path = new PropertyPath(property), Mode = BindingMode.TwoWay };
        checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Binding);
        return checkBox;
    }

    public static Grid GetGridForToolTab(Tool tool)
    {
        PlotView plot = new() { Visibility = Visibility.Hidden };
        plot.SetBinding(PlotViewBase.ModelProperty,
            new Binding() { Source = tool, Path = new PropertyPath(nameof(Tool.Model)) });
        plot.SetBinding(PlotViewBase.ControllerProperty,
            new Binding() { Source = tool, Path = new PropertyPath(nameof(Tool.Controller)) });

        PlotView mainPlot = new();
        mainPlot.SetBinding(PlotViewBase.ModelProperty,
            new Binding() { Source = tool, Path = new PropertyPath(nameof(Tool.MainModel)) });
        mainPlot.SetBinding(PlotViewBase.ControllerProperty,
            new Binding() { Source = tool, Path = new PropertyPath(nameof(Tool.Controller)) });
        Grid.SetRowSpan(mainPlot, 2);

        Grid plotGrid = new();
        plotGrid.RowDefinitions.Add(new() { MinHeight = 50, MaxHeight = 120 });
        plotGrid.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star) });
        plotGrid.Children.Add(plot);
        plotGrid.Children.Add(mainPlot);

        Grid controlGrid = new();
        Grid.SetColumn(controlGrid, 1);
        controlGrid.RowDefinitions.Add(new() { Height = new GridLength(1.2, GridUnitType.Star) });
        controlGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        controlGrid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });

        Grid controlGrid1 = new();
        Grid controlGrid2 = new();
        Grid.SetRow(controlGrid2, 1);
        Grid controlGrid3 = new();
        Grid.SetRow(controlGrid3, 2);

        Border border = new()
        {
            BorderBrush = MainDictionary.Dictionary.txtBorder,
            BorderThickness = new Thickness(1)
        };
        Border border1 = new()
        {
            BorderBrush = MainDictionary.Dictionary.txtBorder,
            BorderThickness = new Thickness(1)
        };
        Grid.SetRow(border1, 1);
        Border border2 = new()
        {
            BorderBrush = MainDictionary.Dictionary.txtBorder,
            BorderThickness = new Thickness(1)
        };
        Grid.SetRow(border2, 2);

        controlGrid.Children.Add(controlGrid1);
        controlGrid.Children.Add(controlGrid2);
        controlGrid.Children.Add(controlGrid3);
        controlGrid.Children.Add(border);
        controlGrid.Children.Add(border1);
        controlGrid.Children.Add(border2);

        Grid globalGrid = new();
        globalGrid.ColumnDefinitions.Add(new());
        globalGrid.ColumnDefinitions.Add(new() { Width = new GridLength(200), MinWidth = 100 });
        globalGrid.Children.Add(plotGrid);
        globalGrid.Children.Add(controlGrid);
        return globalGrid;
    }

    public static void FillControlGrid(Tool tool, Grid grid,
        RoutedEventHandler changeActivityTool, SelectionChangedEventHandler updateViewTool)
    {
        Button activeButton = new()
        {
            Content = tool.Active ? "Deactivate tool" : "Activate tool",
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(5, 5, 0, 0),
            Width = 90,
            Height = 20
        };
        activeButton.Click += changeActivityTool;

        ComboBox baseTFBox = new()
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(45, 30, 0, 0),
            Width = 50,
            ItemsSource = new int[] { 1, 5, 15, 30, 60, 120, 240, 360, 720 }
        };
        baseTFBox.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty,
            new Binding() { Source = tool, Path = new PropertyPath("BaseTF"), Mode = BindingMode.TwoWay });

        var scripts = tool.Scripts.Select(s => s.Name).ToList();
        scripts.Add("AllScripts");
        scripts.Add("Nothing");
        ComboBox scriptsBox = new()
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(105, 30, 0, 0),
            Width = 90,
            ItemsSource = scripts,
            SelectedValue = scripts[^2],
            DataContext = tool
        };
        scriptsBox.SelectionChanged += updateViewTool;

        Border borderState = new()
        {
            Margin = new Thickness(0, 55, 0, 0),
            Height = 10,
            BorderThickness = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            Background = Theme.Orange
        };
        tool.BorderState = borderState;

        grid.Children.Clear();
        grid.Children.Add(activeButton);
        grid.Children.Add(GetTextBlock("BaseTF", 5, 33));
        grid.Children.Add(baseTFBox);
        grid.Children.Add(scriptsBox);

        grid.Children.Add(borderState);
        grid.Children.Add(GetCheckBox(tool, "Stop trading", "StopTrading", 5, 70));
        grid.Children.Add(GetCheckBox(tool, "Normalization", "UseNormalization", 5, 110));
        grid.Children.Add(GetCheckBox(tool, "Trade share", "TradeShare", 5, 130));

        grid.Children.Add(GetCheckBox(tool, "Basic security", "ShowBasicSecurity", 105, 70));
        grid.Children.Add(GetTextBlock("Wait limit", 105, 110));
        grid.Children.Add(GetTextBox(tool, "WaitingLimit", 165, 110));
        grid.Children.Add(GetCheckBox(tool, "Shift balance", "UseShiftBalance", 105, 130));

        if (tool.TradeShare)
        {
            grid.Children.Add(GetTextBlock("Share fund", 5, 150));
            grid.Children.Add(GetTextBox(tool, "ShareOfFunds", 65, 150));

            grid.Children.Add(GetTextBlock("Min lots", 5, 170));
            grid.Children.Add(GetTextBox(tool, "MinNumberOfLots", 65, 170));

            grid.Children.Add(GetTextBlock("Max lots", 105, 170));
            grid.Children.Add(GetTextBox(tool, "MaxNumberOfLots", 165, 170));
        }
        else
        {
            grid.Children.Add(GetTextBlock("Num lots", 5, 150));
            grid.Children.Add(GetTextBox(tool, "NumberOfLots", 65, 150));
        }
        if (tool.UseShiftBalance)
        {
            grid.Children.Add(GetTextBlock("Base balance", 105, 150));
            grid.Children.Add(GetTextBox(tool, "BaseBalance", 165, 150));
        }

        TextBlock mainBlock = GetTextBlock("Main info", 5, 190);
        tool.MainBlockInfo = mainBlock;
        grid.Children.Add(mainBlock);

        TextBlock block = GetTextBlock("Info", 105, 190);
        tool.BlockInfo = block;
        grid.Children.Add(block);
    }
}
