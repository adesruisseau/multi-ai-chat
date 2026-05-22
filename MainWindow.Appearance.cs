using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;

namespace AgentGroupChat;

public partial class MainWindow
{
    public string UiThemeSelection
    {
        get => _uiThemeSelection;
        set
        {
            var normalized = UiThemeManager.NormalizeThemeSelection(value);
            if (!SetProperty(ref _uiThemeSelection, normalized))
            {
                return;
            }

            ApplyCurrentAppearance();
            PersistAppearanceSettings();
        }
    }

    public string UiAccentSelection
    {
        get => _uiAccentSelection;
        set
        {
            var normalized = UiThemeManager.NormalizeAccentSelection(value);
            if (!SetProperty(ref _uiAccentSelection, normalized))
            {
                return;
            }

            ApplyCurrentAppearance();
            PersistAppearanceSettings();
        }
    }

    private void ApplyAppearanceSettings(ConnectionSettings settings)
    {
        SetProperty(ref _uiThemeSelection, UiThemeManager.NormalizeThemeSelection(settings.UiTheme), nameof(UiThemeSelection));
        SetProperty(ref _uiAccentSelection, UiThemeManager.NormalizeAccentSelection(settings.UiAccent), nameof(UiAccentSelection));
        ApplyCurrentAppearance();
    }

    private void PersistAppearanceSettings()
    {
        try
        {
            var settings = _connectionSettingsStore.Load();
            settings.UiTheme = UiThemeSelection;
            settings.UiAccent = UiAccentSelection;
            _connectionSettingsStore.Save(settings);
            ConnectionSettingsSummary = BuildConnectionSettingsSummary(settings);
        }
        catch (Exception exception)
        {
            ConnectionSettingsSummary = exception.Message;
        }
    }

    private void ApplyCurrentAppearance()
    {
        UiThemeManager.Apply(UiThemeSelection, UiAccentSelection);
        RefreshAppearanceSensitiveMessages();
        Dispatcher.BeginInvoke(
            () => UiThemeManager.ApplyLegacyBrushMapping(this),
            DispatcherPriority.Loaded);
    }

    private void RefreshAppearanceSensitiveMessages()
    {
        var userAccentBrush = GetApplicationBrush("AppUserMessageAccentBrush", "#34536B");
        var userBackgroundBrush = GetApplicationBrush("AppUserMessageBackgroundBrush", "#E4EEF6");

        foreach (var message in Messages)
        {
            if (string.Equals(message.Author, "You", StringComparison.OrdinalIgnoreCase))
            {
                message.AccentBrush = userAccentBrush;
                message.CardBackground = userBackgroundBrush;
                continue;
            }

            var agent = SelectedTheme?.Agents.FirstOrDefault(candidate => string.Equals(candidate.Name, message.Author, StringComparison.OrdinalIgnoreCase));
            if (agent is null)
            {
                continue;
            }

            message.AccentBrush = CreateBrush(agent.AccentHex);
            message.CardBackground = CreateAgentCardBackgroundBrush(agent.BackgroundHex);
        }
    }

    private void SubscribeToAppearanceSync()
    {
        if (_appearanceSyncSubscribed)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
        _appearanceSyncSubscribed = true;
    }

    private void UnsubscribeFromAppearanceSync()
    {
        if (!_appearanceSyncSubscribed)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
        _appearanceSyncSubscribed = false;
    }

    private void OnSystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (!string.Equals(UiThemeSelection, "System", StringComparison.Ordinal))
        {
            return;
        }

        if (e.Category is not (UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle
            or UserPreferenceCategory.Window))
        {
            return;
        }

        Dispatcher.BeginInvoke(ApplyCurrentAppearance, DispatcherPriority.Background);
    }

    private void HandleLoadedElementForAppearance(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dependencyObject)
        {
            UiThemeManager.ApplyLegacyBrushMapping(dependencyObject);
        }
    }
}