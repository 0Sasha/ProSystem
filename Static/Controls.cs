using System;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows;

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
}
