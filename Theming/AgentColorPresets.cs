namespace AgentGroupChat.Hybrid.Theming;

public static class AgentColorPresets
{
    public record ColorPreset(string Name, string AccentHex, string BackgroundHex);

    public static readonly ColorPreset[] LightPresets =
    [
        new("Terracotta", "#C56A54", "#F9E5DE"),
        new("Ocean",      "#2E6799", "#DDE9F3"),
        new("Berry",      "#984566", "#F3DFEA"),
        new("Forest",     "#3A6C4C", "#DEF0E4"),
        new("Gold",       "#8F6617", "#F5EDDA"),
        new("Slate",      "#506070", "#E4E8EC"),
    ];

    public static readonly ColorPreset[] DarkPresets =
    [
        new("Terracotta", "#E39B74", "#3A2018"),
        new("Ocean",      "#6EACDF", "#142838"),
        new("Berry",      "#D889AB", "#321828"),
        new("Forest",     "#7AB18B", "#142A1C"),
        new("Gold",       "#D6A44A", "#2E2410"),
        new("Slate",      "#96A7B5", "#1C2630"),
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
}
