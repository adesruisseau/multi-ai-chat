using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentGroupChat;

public partial class MainWindow
{
    private void AddSystemMessage(string content)
    {
        AddLog(LogCategory.System, "System", content);
    }

    private void AddLog(LogCategory category, string source, string message, string? detail = null, long? durationMilliseconds = null)
    {
        if (ShouldSkipLog(category))
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(
                () => AddLog(category, source, message, detail, durationMilliseconds),
                DispatcherPriority.Background);
            return;
        }

        RegisterLogSource(source);
        var shouldAutoScroll = ShouldAutoScrollLogs();
        var entry = new LogEntry(category, source, message, detail, durationMilliseconds);
        Logs.Add(entry);
        TrimLogsIfNeeded();

        try
        {
            _sessionLogStore.Append(entry);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Unable to write session log: {exception.Message}");
        }

        RefreshLogFilters();
        if (shouldAutoScroll)
        {
            ScrollLogsToEnd(entry);
        }
    }

    private void TrimLogsIfNeeded()
    {
        while (Logs.Count > MaximumInMemoryLogEntries)
        {
            Logs.RemoveAt(0);
        }
    }

    private void ScrollLogsToEnd(LogEntry? targetEntry = null)
    {
        if (Logs.Count == 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            () => LogsListBox.ScrollIntoView(targetEntry ?? Logs[^1]),
            DispatcherPriority.Background);
    }

    private bool ShouldAutoScrollLogs()
    {
        if (LogsListBox is null)
        {
            return true;
        }

        if (Logs.Count == 0)
        {
            return true;
        }

        var scrollViewer = FindDescendant<ScrollViewer>(LogsListBox);
        if (scrollViewer is null)
        {
            return true;
        }

        return scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 32;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void RegisterLogSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source)
            || string.Equals(source, "All sources", StringComparison.OrdinalIgnoreCase)
            || LogSources.Contains(source, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        LogSources.Add(source);
    }

    private void RefreshLogFilters()
    {
        FilteredLogs.Refresh();
    }

    private bool ShouldSkipLog(LogCategory category)
    {
        return category == LogCategory.Speech && !ShowSpeechLogs;
    }

    private bool FilterLogEntry(object item)
    {
        if (item is not LogEntry entry)
        {
            return false;
        }

        var categoryVisible = entry.Category switch
        {
            LogCategory.System => ShowSystemLogs,
            LogCategory.Speech => ShowSpeechLogs,
            LogCategory.Timing => ShowTimingLogs,
            LogCategory.Request => ShowRequestLogs,
            LogCategory.Response => ShowResponseLogs,
            LogCategory.Memory => ShowMemoryLogs,
            _ => true,
        };

        if (!categoryVisible)
        {
            return false;
        }

        return string.Equals(SelectedLogSource, "All sources", StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Source, SelectedLogSource, StringComparison.OrdinalIgnoreCase);
    }
}