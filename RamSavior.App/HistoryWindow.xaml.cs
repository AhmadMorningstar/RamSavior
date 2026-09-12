using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RamSavior.Core.Logging;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class HistoryWindow : FluentWindow
{
    public HistoryWindow(string logPath)
    {
        InitializeComponent();
        Load(logPath);
    }

    private void Load(string logPath)
    {
        var entries = JsonLogger.ReadRecent(logPath, 50);

        if (entries.Count == 0)
        {
            EntriesPanel.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "No cleaning history yet.",
                Opacity = 0.6,
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var entry in entries)
        {
            var row = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var stack = new System.Windows.Controls.StackPanel();

            string timeText = DateTimeOffset.TryParse(entry.Timestamp, out var dto)
                ? dto.ToLocalTime().ToString("MMM d, h:mm tt")
                : entry.Timestamp;

            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = $"{timeText} \u2014 {entry.Mode} ({entry.Source})",
                FontWeight = FontWeights.SemiBold
            });

            string detail = entry.Success
                ? $"Freed {entry.FreedGB:F2} GB \u2022 {entry.BeforeAvailableGB:F2} \u2192 {entry.AfterAvailableGB:F2} GB \u2022 {entry.DurationMs:F0}ms"
                : $"Failed: {entry.Error}";

            stack.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = detail,
                Opacity = 0.7,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            row.Child = stack;
            EntriesPanel.Children.Add(row);
        }
    }
}
