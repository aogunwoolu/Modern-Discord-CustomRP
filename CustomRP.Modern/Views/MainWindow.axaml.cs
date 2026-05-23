using Avalonia.Controls;

namespace CustomRP.Modern.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (App.IsExiting) return;

        // Default: hide to tray instead of exiting so background presences
        // keep running. User can disable MinimizeToTray in Settings.
        if (App.Services.Settings.Current.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
