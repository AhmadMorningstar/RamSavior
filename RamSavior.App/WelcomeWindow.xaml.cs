using System.Windows;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class WelcomeWindow : FluentWindow
{
    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void GotItButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
