namespace AgentGroupChat.UI.Shared.Theming;

public static class AgentColorPresets
{
    public record ColorPreset(string Name, string AccentHex, string BackgroundHex);

    public static readonly ColorPreset[] LightPresets =
    [
        new("Terracotta", "#C56A54", "#F9E5DE"),
        new("Ocean",      "#2E6799", "#DDE9F3"),
        new("Berry",      "#984566", "#F3DFEA"),
        new("Plum",       "#800080", "#FFCCFF"),
        new("Forest",     "#3A6C4C", "#DEF0E4"),
        new("Mustard",    "#B4B43C", "#ECECC6"),
        new("Gold",       "#8F6617", "#F5EDDA"),
        new("Slate",      "#506070", "#E4E8EC"),
        new("Mono",       "#D9D9D9", "#F2F2F2")
    ];

    public static readonly ColorPreset[] DarkPresets =
    [
        new("Terracotta", "#E39B74", "#3A2018"),
        new("Ocean",      "#6EACDF", "#142838"),
        new("Berry",      "#D889AB", "#321828"),
        new("Plum",       "#660066", "#330033"),
        new("Forest",     "#7AB18B", "#142A1C"),
        new("Mustard",    "#CCCC66", "#606020"),
        new("Gold",       "#D6A44A", "#2E2410"),
        new("Slate",      "#96A7B5", "#1C2630"),
        new("Mono",       "#0D0D0D", "#262626")
    ];

    public static ColorPreset[] GetPresets(bool isDarkMode) => isDarkMode ? DarkPresets : LightPresets;

    public static ColorPreset FindByAccent(string accentHex, bool isDarkMode)
    {
        var presets = GetPresets(isDarkMode);
        foreach (var p in presets)
            if (string.Equals(p.AccentHex, accentHex, StringComparison.OrdinalIgnoreCase))
                return p;
        return presets[0];
    }

    public static ColorPreset FindByName(string colorName, bool isDarkMode)
    {
        var presets = GetPresets(isDarkMode);
        foreach (var p in presets)
            if (string.Equals(p.Name, colorName, StringComparison.OrdinalIgnoreCase))
                return p;
        return presets[0];
    }
}
