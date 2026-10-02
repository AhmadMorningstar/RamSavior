using System.Windows;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class RestartRequiredWindow : FluentWindow
{
    /// <summary>True only if the user pressed "Restart now". Closing the window any
    /// other way (Later, the X, Esc/Alt+F4) counts as "later".</summary>
    public bool RestartRequested { get; private set; }

    public RestartRequiredWindow()
    {
        InitializeComponent();
        Icon = AppIcons.Window;
        Loaded += (_, _) => RestartButton.Focus();
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e)
    {
        RestartRequested = true;
        Close();
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e) => Close();
}
