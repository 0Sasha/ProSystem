using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
namespace ProSystem;

public partial class MainDictionary
{
    public static MainDictionary Dictionary;
    public MainDictionary()
    {
        InitializeComponent();
        Dictionary = this;
    }

    private void AutoMinimize_Event(object sender, RoutedEventArgs e)
    {
        var Window = (sender as Button).TemplatedParent as Window;
        if (Window == null) return;
        Window.WindowState = Window.WindowState == WindowState.Maximized ?
            Window.WindowState = WindowState.Normal : Window.WindowState = WindowState.Maximized;
    }

    private void Minimize_Event(object sender, RoutedEventArgs e)
    {
        var Window = (sender as Button).TemplatedParent as Window;
        if (Window == null) return;
        Window.WindowState = WindowState.Minimized;
    }

    private void CloseWindow_Event(object sender, RoutedEventArgs e)
    {
        var Window = (sender as Button).TemplatedParent as Window;
        if (Window == null) return;
        Window.Close();
    }

    private void TxtKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
            try
            {
                if (sender is TextBox tbx) tbx.GetBindingExpression(TextBox.TextProperty).UpdateSource();
            }
            catch (Exception ex) { MainWindow.AddInfo("TxtKeyDown: " + ex.Message); }
        }
    }
}
