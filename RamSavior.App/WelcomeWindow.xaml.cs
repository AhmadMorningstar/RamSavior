using System.Windows;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class WelcomeWindow : FluentWindow
{
    /// <summary>Read after ShowDialog() returns — true unless the user unchecked the box before closing.</summary>
    public bool DontShowAgain { get; private set; } = true;

    public WelcomeWindow()
    {
        InitializeComponent();
    }

    private void GotItButton_Click(object sender, RoutedEventArgs e)
    {
        DontShowAgain = DontShowAgainCheckBox.IsChecked == true;
        Close();
    }
}
