using OxyPlot.SkiaSharp.Wpf;
using OxyPlot.Wpf;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ProSystem;

internal static class Controls
{
    public static TabItem GetTabForTool(Tool tool)
    {
        return new()
        {
            Name = tool.Name,
            Header = tool.Name,
            Width = 54,
            Height = 24,
            Content = GetGrid(tool)
        };
    }

    private static Grid GetGrid(Tool tool)
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


    public static void UpdateControlPanel(Tool tool, bool updateScriptPanel,
        RoutedEventHandler changeActivityTool, SelectionChangedEventHandler updateViewTool)
    {
        if (tool == null) throw new ArgumentNullException(nameof(tool));
        if (changeActivityTool == null) throw new ArgumentNullException(nameof(changeActivityTool));
        if (updateViewTool == null) throw new ArgumentNullException(nameof(updateViewTool));

        tool.ControlPanel ??= (Grid)(tool.Tab.Content as Grid).Children[1];
        UpdateMainControlPanel(tool, changeActivityTool, updateViewTool);
        if (updateScriptPanel) UpdateScriptControlPanel(tool);
    }

    private static void UpdateMainControlPanel(Tool tool,
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
            new Binding() { Source = tool, Path = new PropertyPath(nameof(Tool.BaseTF)), Mode = BindingMode.TwoWay });

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

        var grid = tool.ControlPanel.Children[0] as Grid;
        grid.Children.Clear();
        grid.Children.Add(activeButton);
        grid.Children.Add(GetTextBlock("BaseTF", 5, 33));
        grid.Children.Add(baseTFBox);
        grid.Children.Add(scriptsBox);

        grid.Children.Add(borderState);
        grid.Children.Add(GetCheckBox(tool, "Stop trading", nameof(Tool.StopTrading), 5, 70));
        grid.Children.Add(GetCheckBox(tool, "Normalization", nameof(Tool.UseNormalization), 5, 110));
        grid.Children.Add(GetCheckBox(tool, "Trade share", nameof(Tool.TradeShare), 5, 130));

        grid.Children.Add(GetCheckBox(tool, "Basic security", nameof(Tool.ShowBasicSecurity), 105, 70));
        grid.Children.Add(GetTextBlock("Wait limit", 105, 110));
        grid.Children.Add(GetTextBox(tool, nameof(Tool.WaitingLimit), 165, 110));
        grid.Children.Add(GetCheckBox(tool, "Shift balance", nameof(Tool.UseShiftBalance), 105, 130));

        if (tool.TradeShare)
        {
            grid.Children.Add(GetTextBlock("Share fund", 5, 150));
            grid.Children.Add(GetTextBox(tool, nameof(Tool.ShareOfFunds), 65, 150));

            grid.Children.Add(GetTextBlock("Min lots", 5, 170));
            grid.Children.Add(GetTextBox(tool, nameof(Tool.MinNumberOfLots), 65, 170));

            grid.Children.Add(GetTextBlock("Max lots", 105, 170));
            grid.Children.Add(GetTextBox(tool, nameof(Tool.MaxNumberOfLots), 165, 170));
        }
        else
        {
            grid.Children.Add(GetTextBlock("Num lots", 5, 150));
            grid.Children.Add(GetTextBox(tool, nameof(Tool.NumberOfLots), 65, 150));
        }
        if (tool.UseShiftBalance)
        {
            grid.Children.Add(GetTextBlock("Base balance", 105, 150));
            grid.Children.Add(GetTextBox(tool, nameof(Tool.BaseBalance), 165, 150));
        }

        TextBlock mainBlock = GetTextBlock("Main info", 5, 190);
        tool.MainBlockInfo = mainBlock;
        grid.Children.Add(mainBlock);

        TextBlock block = GetTextBlock("Info", 105, 190);
        tool.BlockInfo = block;
        grid.Children.Add(block);
    }

    private static void UpdateScriptControlPanel(Tool tool)
    {
        foreach (var grid in (tool.ControlPanel.Children.OfType<Grid>().Skip(1))) grid.Children.Clear();

        var scripts = tool.Scripts;
        for (int i = 0; i < scripts.Length; i++)
        {
            var props = scripts[i].Properties;
            if (props.IsOSC)
            {
                var plot = ((tool.Tab.Content as Grid).Children[0] as Grid).Children[0] as PlotView;
                if (plot.Visibility == Visibility.Hidden)
                {
                    Grid.SetRow(((tool.Tab.Content as Grid).Children[0] as Grid).Children[1] as PlotView, 1);
                    plot.Visibility = Visibility.Visible;
                }
            }
            var collection = (((tool.Tab.Content as Grid).Children[1] as Grid).Children[i + 1] as Grid).Children;

            collection.Add(GetTextBlock(scripts[i].Name, 5, 0));
            AddUpperControls(scripts[i], collection, props);
            if (props.MiddleProperties != null) AddMiddleControls(scripts[i], collection, props);

            var textBlock = GetTextBlock("Block Info", 5, 170);
            scripts[i].BlockInfo = textBlock;
            collection.Add(textBlock);
        }
    }


    private static void AddUpperControls(Script script,
        UIElementCollection uiCollection, ScriptProperties properties)
    {
        var props = properties.UpperProperties;

        uiCollection.Add(GetTextBlock(props[0], 5, 20));
        uiCollection.Add(GetTextBox(script, props[0], 65, 20));

        uiCollection.Add(GetTextBlock(props[1], 5, 40));
        uiCollection.Add(GetTextBox(script, props[1], 65, 40));

        if (props.Length > 2)
        {
            uiCollection.Add(GetTextBlock(props[2], 5, 60));
            uiCollection.Add(GetTextBox(script, props[2], 65, 60));
        }
        if (props.Length > 3)
        {
            uiCollection.Add(GetTextBlock(props[3], 105, 20));
            uiCollection.Add(GetTextBox(script, props[3], 165, 20));
        }
        if (props.Length > 4)
        {
            uiCollection.Add(GetTextBlock(props[4], 105, 40));
            uiCollection.Add(GetTextBox(script, props[4], 165, 40));
        }
        if (props.Length > 5)
        {
            uiCollection.Add(GetTextBlock(props[5], 105, 60));
            uiCollection.Add(GetTextBox(script, props[5], 165, 60));
        }
        if (props.Length > 6) throw new ArgumentException(script.Name + ": UpperProperties.Length > 6");

        ComboBox comboBox = new()
        {
            Width = 90,
            Margin = new System.Windows.Thickness(5, 80, 0, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            ItemsSource = new PositionType[] { PositionType.Long, PositionType.Short, PositionType.Neutral }
        };
        Binding binding = new()
        {
            Source = script,
            Path = new PropertyPath(nameof(Script.CurrentPosition)),
            Mode = BindingMode.TwoWay
        };
        comboBox.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, binding);
        uiCollection.Add(comboBox);

        if (properties.MAProperty != null)
        {
            ComboBox comboBox2 = new()
            {
                Width = 90,
                Margin = new System.Windows.Thickness(105, 80, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                ItemsSource = properties.MAObjects
            };
            Binding binding2 = new()
            {
                Source = script,
                Path = new System.Windows.PropertyPath(properties.MAProperty),
                Mode = BindingMode.TwoWay
            };
            comboBox2.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, binding2);
            uiCollection.Add(comboBox2);
        }
    }

    private static void AddMiddleControls(Script script,
        UIElementCollection uiCollection, ScriptProperties properties)
    {
        var props = properties.MiddleProperties;
        uiCollection.Add(GetCheckBox(script, props[0], props[0], 5, 110));
        if (props.Length > 1) uiCollection.Add(GetCheckBox(script, props[1], props[1], 5, 130));
        if (props.Length > 2) uiCollection.Add(GetCheckBox(script, props[2], props[2], 5, 150));
        if (props.Length > 3) uiCollection.Add(GetCheckBox(script, props[3], props[3], 105, 110));
        if (props.Length > 4) uiCollection.Add(GetCheckBox(script, props[4], props[4], 105, 130));
        if (props.Length > 5) uiCollection.Add(GetCheckBox(script, props[5], props[5], 105, 150));
        if (props.Length > 6) throw new ArgumentException(script.Name + ": MiddleProperties.Length > 6");
    }


    private static TextBlock GetTextBlock(string property, double left, double top)
    {
        return new()
        {
            Text = property.Length > 14 ? property[0..14] : property,
            Margin = new Thickness(left, top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static TextBox GetTextBox(object sourceBinding, string property, double left, double top)
    {
        TextBox textBox = new()
        {
            Width = 30,
            Margin = new Thickness(left, top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Binding binding = new()
        {
            Source = sourceBinding,
            Path = new PropertyPath(property),
            Mode = BindingMode.TwoWay
        };
        textBox.SetBinding(TextBox.TextProperty, binding);
        return textBox;
    }

    private static CheckBox GetCheckBox(object sourceBinding, string boxName, string property, double left, double top)
    {
        CheckBox checkBox = new()
        {
            Content = boxName.Length > 14 ? boxName[0..14] : boxName,
            Margin = new Thickness(left, top, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Binding binding = new()
        {
            Source = sourceBinding,
            Path = new PropertyPath(property),
            Mode = BindingMode.TwoWay
        };
        checkBox.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, binding);
        return checkBox;
    }
}
