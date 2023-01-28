using System;
using System.Windows.Controls;
namespace ProSystem;

public partial class MainDictionary
{
    private void AutoMinimize_Event(object sender, System.Windows.RoutedEventArgs e)
    {
        var Window = (sender as Button).TemplatedParent as System.Windows.Window;
        if (Window == null) return;
        Window.WindowState = Window.WindowState == System.Windows.WindowState.Maximized ?
            Window.WindowState = System.Windows.WindowState.Normal : Window.WindowState = System.Windows.WindowState.Maximized;
    }

    private void Minimize_Event(object sender, System.Windows.RoutedEventArgs e)
    {
        var Window = (sender as Button).TemplatedParent as System.Windows.Window;
        if (Window == null) return;
        Window.WindowState = System.Windows.WindowState.Minimized;
    }

    private void CloseWindow_Event(object sender, System.Windows.RoutedEventArgs e)
    {
        var Window = (sender as Button).TemplatedParent as System.Windows.Window;
        if (Window == null) return;
        Window.Close();
    }
}
