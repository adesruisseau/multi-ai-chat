using System.Collections;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AgentGroupChat;

public static class UiThemeManager
{
    private sealed record AccentPalette(string Accent, string AccentStrong, string Focus, string StatusText);

    private static readonly IReadOnlyDictionary<string, string> LegacyBrushResourceMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["#F4EFE6"] = "AppWindowBackgroundBrush",
        ["#1D2A33"] = "AppHeroBackgroundBrush",
        ["#2B3A44"] = "AppHeroSurfaceBrush",
        ["#F7EFE5"] = "AppHeroTextBrush",
        ["#C4D1D6"] = "AppHeroMutedTextBrush",
        ["#F8D58B"] = "AppHeroStatusTextBrush",
        ["#FFF9F1"] = "AppPanelBackgroundBrush",
        ["#FFFDF9"] = "AppPanelAltBackgroundBrush",
        ["#FFF8EE"] = "AppPanelWarmBackgroundBrush",
        ["#F6EFE6"] = "AppSectionBackgroundBrush",
        ["#EFE7DB"] = "AppBadgeBrush",
        ["#E8DDD0"] = "AppBadgeStrongBrush",
        ["#E4D6C6"] = "AppPanelBorderBrush",
        ["#E0D7CC"] = "AppPanelBorderAltBrush",
        ["#2C221B"] = "AppTextBrush",
        ["#2F271F"] = "AppTextStrongBrush",
        ["#46372D"] = "AppSectionTextBrush",
        ["#5A493E"] = "AppBadgeTextBrush",
        ["#66584C"] = "AppMutedTextBrush",
        ["#6F6256"] = "AppSecondaryTextBrush",
        ["#7A6A5D"] = "AppTertiaryTextBrush",
        ["#7C6B5C"] = "AppTertiaryTextBrush",
    };

    public static IReadOnlyList<string> AvailableThemes { get; } = new[] { "System", "Light", "Dark" };

    public static IReadOnlyList<string> AvailableAccents { get; } = new[] { "Terracotta", "Ocean", "Berry", "Forest", "Gold" };

    public static string NormalizeThemeSelection(string? value)
    {
        return value?.Trim() switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => "System",
        };
    }

    public static string NormalizeAccentSelection(string? value)
    {
        return value?.Trim() switch
        {
            "Ocean" => "Ocean",
            "Berry" => "Berry",
            "Forest" => "Forest",
            "Gold" => "Gold",
            _ => "Terracotta",
        };
    }

    public static bool IsDarkTheme(string themeSelection)
    {
        return string.Equals(ResolveBaseTheme(themeSelection), "Dark", StringComparison.Ordinal);
    }

    public static void Apply(string themeSelection, string accentSelection)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        var accent = GetAccentPalette(accentSelection);
        var baseTheme = ResolveBaseTheme(themeSelection);
        var palette = string.Equals(baseTheme, "Dark", StringComparison.Ordinal)
            ? CreateDarkPalette(accent)
            : CreateLightPalette(accent);

        foreach (var entry in palette)
        {
            ApplyBrush(resources, entry.Key, entry.Value);
        }
    }

    public static void EnsureMutableBrushResources()
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        EnsureMutableBrushResources(resources);
    }

    public static void ApplyLegacyBrushMapping(DependencyObject root)
    {
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        ApplyLegacyBrushesToObject(root, resources);

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            ApplyLegacyBrushMapping(VisualTreeHelper.GetChild(root, index));
        }
    }

    private static void ApplyLegacyBrushesToObject(DependencyObject root, ResourceDictionary resources)
    {
        switch (root)
        {
            case Window window:
                ReplaceBrush(window, Window.BackgroundProperty, resources);
                break;
            case Border border:
                ReplaceBrush(border, Border.BackgroundProperty, resources);
                ReplaceBrush(border, Border.BorderBrushProperty, resources);
                break;
            case TextBlock textBlock:
                ReplaceBrush(textBlock, TextBlock.ForegroundProperty, resources);
                ReplaceBrush(textBlock, TextBlock.BackgroundProperty, resources);
                break;
            case Control control:
                ReplaceBrush(control, Control.BackgroundProperty, resources);
                ReplaceBrush(control, Control.ForegroundProperty, resources);
                ReplaceBrush(control, Control.BorderBrushProperty, resources);
                break;
            case Panel panel:
                ReplaceBrush(panel, Panel.BackgroundProperty, resources);
                break;
            case Shape shape:
                ReplaceBrush(shape, Shape.FillProperty, resources);
                ReplaceBrush(shape, Shape.StrokeProperty, resources);
                break;
        }
    }

    private static void ReplaceBrush(DependencyObject root, DependencyProperty property, ResourceDictionary resources)
    {
        var localValue = root.ReadLocalValue(property);
        if (localValue is not SolidColorBrush brush)
        {
            return;
        }

        var legacyHex = ToHex(brush.Color);
        if (!LegacyBrushResourceMap.TryGetValue(legacyHex, out var resourceKey))
        {
            return;
        }

        if (resources[resourceKey] is Brush themedBrush && !ReferenceEquals(themedBrush, brush))
        {
            if (root is FrameworkElement frameworkElement)
            {
                frameworkElement.SetResourceReference(property, resourceKey);
            }
            else if (root is FrameworkContentElement frameworkContentElement)
            {
                frameworkContentElement.SetResourceReference(property, resourceKey);
            }
        }
    }

    private static Dictionary<string, string> CreateLightPalette(AccentPalette accent)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppSurfaceBrush"] = "#FFF8F0",
            ["AppSurfaceAltBrush"] = "#FFF2E5",
            ["AppSurfaceMutedBrush"] = "#F4E7D8",
            ["AppBorderBrush"] = "#D8C6B2",
            ["AppBorderStrongBrush"] = "#C7A487",
            ["AppTextBrush"] = "#2C221B",
            ["AppMutedTextBrush"] = "#66584C",
            ["AppAccentBrush"] = accent.Accent,
            ["AppAccentStrongBrush"] = accent.AccentStrong,
            ["AppAccentContrastBrush"] = "#FFFDF9",
            ["AppSlateBrush"] = "#516273",
            ["AppSlateStrongBrush"] = "#384654",
            ["AppSlateContrastBrush"] = "#F7F3EE",
            ["AppDisabledBrush"] = "#C4B8AB",
            ["AppDisabledTextBrush"] = "#8E8378",
            ["AppFocusBrush"] = accent.Focus,
            ["AppWindowBackgroundBrush"] = "#F4EFE6",
            ["AppHeroBackgroundBrush"] = "#1D2A33",
            ["AppHeroSurfaceBrush"] = "#2B3A44",
            ["AppHeroTextBrush"] = "#F7EFE5",
            ["AppHeroMutedTextBrush"] = "#C4D1D6",
            ["AppHeroStatusTextBrush"] = accent.StatusText,
            ["AppPanelBackgroundBrush"] = "#FFF9F1",
            ["AppPanelAltBackgroundBrush"] = "#FFFDF9",
            ["AppPanelWarmBackgroundBrush"] = "#FFF8EE",
            ["AppSectionBackgroundBrush"] = "#F6EFE6",
            ["AppPanelBorderBrush"] = "#E4D6C6",
            ["AppPanelBorderAltBrush"] = "#E0D7CC",
            ["AppBadgeBrush"] = "#EFE7DB",
            ["AppBadgeStrongBrush"] = "#E8DDD0",
            ["AppBadgeTextBrush"] = "#5A493E",
            ["AppTextStrongBrush"] = "#2F271F",
            ["AppSectionTextBrush"] = "#46372D",
            ["AppSecondaryTextBrush"] = "#6F6256",
            ["AppTertiaryTextBrush"] = "#7C6B5C",
            ["AppHoverBrush"] = "#FFF7EF",
            ["AppSelectionBrush"] = "#F7D9C8",
            ["AppSelectionMutedBrush"] = "#F2E2D4",
            ["AppUserMessageAccentBrush"] = "#34536B",
            ["AppUserMessageBackgroundBrush"] = "#E4EEF6",
        };
    }

    private static Dictionary<string, string> CreateDarkPalette(AccentPalette accent)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AppSurfaceBrush"] = "#161D24",
            ["AppSurfaceAltBrush"] = "#1C252F",
            ["AppSurfaceMutedBrush"] = "#263340",
            ["AppBorderBrush"] = "#32404C",
            ["AppBorderStrongBrush"] = "#4B6072",
            ["AppTextBrush"] = "#EAF1F7",
            ["AppMutedTextBrush"] = "#B7C5D1",
            ["AppAccentBrush"] = accent.Accent,
            ["AppAccentStrongBrush"] = accent.AccentStrong,
            ["AppAccentContrastBrush"] = "#F8FBFF",
            ["AppSlateBrush"] = "#44556A",
            ["AppSlateStrongBrush"] = "#6A7D95",
            ["AppSlateContrastBrush"] = "#F1F6FB",
            ["AppDisabledBrush"] = "#2C3845",
            ["AppDisabledTextBrush"] = "#7D8B99",
            ["AppFocusBrush"] = accent.Focus,
            ["AppWindowBackgroundBrush"] = "#0F1318",
            ["AppHeroBackgroundBrush"] = "#0C1117",
            ["AppHeroSurfaceBrush"] = "#18222D",
            ["AppHeroTextBrush"] = "#F4F8FB",
            ["AppHeroMutedTextBrush"] = "#A9BBC9",
            ["AppHeroStatusTextBrush"] = accent.StatusText,
            ["AppPanelBackgroundBrush"] = "#121920",
            ["AppPanelAltBackgroundBrush"] = "#151D25",
            ["AppPanelWarmBackgroundBrush"] = "#17212A",
            ["AppSectionBackgroundBrush"] = "#1A232C",
            ["AppPanelBorderBrush"] = "#2E3A46",
            ["AppPanelBorderAltBrush"] = "#354351",
            ["AppBadgeBrush"] = "#202B36",
            ["AppBadgeStrongBrush"] = "#273440",
            ["AppBadgeTextBrush"] = "#CBD8E2",
            ["AppTextStrongBrush"] = "#F4F7FA",
            ["AppSectionTextBrush"] = "#D9E4ED",
            ["AppSecondaryTextBrush"] = "#B8C6D1",
            ["AppTertiaryTextBrush"] = "#96A7B5",
            ["AppHoverBrush"] = "#202C36",
            ["AppSelectionBrush"] = "#2C3C4C",
            ["AppSelectionMutedBrush"] = "#23313E",
            ["AppUserMessageAccentBrush"] = "#6C95B4",
            ["AppUserMessageBackgroundBrush"] = "#1A2C3C",
        };
    }

    private static AccentPalette GetAccentPalette(string accentSelection)
    {
        return NormalizeAccentSelection(accentSelection) switch
        {
            "Ocean" => new AccentPalette("#3E84C6", "#2E6799", "#6EACDF", "#9FD6FF"),
            "Berry" => new AccentPalette("#C05B86", "#984566", "#D889AB", "#FFB1CC"),
            "Forest" => new AccentPalette("#4E8A63", "#3A6C4C", "#7AB18B", "#BFE6B8"),
            "Gold" => new AccentPalette("#B88322", "#8F6617", "#D6A44A", "#F2CF7B"),
            _ => new AccentPalette("#D96C54", "#BE533B", "#E39B74", "#F8D58B"),
        };
    }

    private static string ResolveBaseTheme(string themeSelection)
    {
        return NormalizeThemeSelection(themeSelection) switch
        {
            "Light" => "Light",
            "Dark" => "Dark",
            _ => TryGetSystemUsesLightTheme(out var usesLightTheme) && usesLightTheme ? "Light" : "Dark",
        };
    }

    private static bool TryGetSystemUsesLightTheme(out bool usesLightTheme)
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = personalizeKey?.GetValue("AppsUseLightTheme");
            usesLightTheme = value switch
            {
                int intValue => intValue != 0,
                byte byteValue => byteValue != 0,
                _ => true,
            };
            return value is not null;
        }
        catch
        {
            usesLightTheme = true;
            return false;
        }
    }

    private static void ApplyBrush(ResourceDictionary resources, string resourceKey, string colorValue)
    {
        var color = (Color)ColorConverter.ConvertFromString(colorValue);
        resources[resourceKey] = new SolidColorBrush(color);
    }

    private static void EnsureMutableBrushResources(ResourceDictionary resources)
    {
        var replacements = new List<(object Key, SolidColorBrush Brush)>();

        foreach (DictionaryEntry entry in resources)
        {
            if (entry.Value is SolidColorBrush brush && brush.IsFrozen)
            {
                replacements.Add((entry.Key, brush.CloneCurrentValue()));
            }
        }

        foreach (var replacement in replacements)
        {
            resources[replacement.Key] = replacement.Brush;
        }

        foreach (var mergedDictionary in resources.MergedDictionaries)
        {
            EnsureMutableBrushResources(mergedDictionary);
        }
    }

    private static string ToHex(Color color)
    {
        return color.A == byte.MaxValue
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}