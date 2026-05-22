using System.Windows;

namespace AgentGroupChat;

public partial class LogsWindow : Window
{
    public LogsWindow(MainWindow owner)
    {
        InitializeComponent();
        Owner = owner;
        DataContext = owner;
    }

    public void ScrollToLatest()
    {
        if (LogsListBox.Items.Count == 0)
        {
            return;
        }

        var latestItem = LogsListBox.Items[LogsListBox.Items.Count - 1];
        LogsListBox.ScrollIntoView(latestItem);
    }
}