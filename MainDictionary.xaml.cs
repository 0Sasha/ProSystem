using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace ProSystem;

public partial class MainDictionary
{
    public static MainDictionary Dictionary { get; private set; } = [];

    public MainDictionary()
    {
        InitializeComponent();
        Dictionary = this;
    }

    private void ChangeWindow(object sender, RoutedEventArgs e)
    {
        if (sender is Button { TemplatedParent: Window window })
            window.WindowState =
                window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e)
    {
        if (sender is Button { TemplatedParent: Window window })
            window.WindowState = WindowState.Minimized;
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        if (sender is Button { TemplatedParent: Window window })
            window.Close();
    }

    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Keyboard.ClearFocus();
            if (sender is TextBox box)
                box.GetBindingExpression(TextBox.TextProperty).UpdateSource();
        }
    }
}
