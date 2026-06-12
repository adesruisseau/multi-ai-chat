using MudBlazor;

namespace AgentGroupChat.UI.Shared.Theming;

public static class AppTheme
{
    public static readonly string[] AccentNames = ["Terracotta", "Ocean", "Berry", "Forest", "Gold"];

    public static MudTheme Build(string accent = "Terracotta")
    {
        var (primary, primaryDarken, primaryLighten) = ResolveAccent(accent);

        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                // Primary accent
                Primary = primary,
                PrimaryDarken = primaryDarken,
                PrimaryLighten = primaryLighten,
                PrimaryContrastText = "#FFFDF9",

                // Secondary — warm slate from the WPF hero
                Secondary = "#1D2A33",
                SecondaryDarken = "#141E25",
                SecondaryLighten = "#2B3A44",
                SecondaryContrastText = "#F7EFE5",

                // Tertiary — muted warm
                Tertiary = "#7C6B5C",
                TertiaryDarken = "#5A493E",
                TertiaryLighten = "#96887A",
                TertiaryContrastText = "#FFFDF9",

                // Surfaces — warm parchment tones
                Background = "#F4EFE6",
                Surface = "#FFF9F1",
                DrawerBackground = "#FFF8EE",
                DrawerText = "#2C221B",
                AppbarBackground = "#1D2A33",
                AppbarText = "#F7EFE5",

                // Text
                TextPrimary = "#2C221B",
                TextSecondary = "#66584C",
                TextDisabled = "#B0A498",

                // Lines & borders
                Divider = "#E4D6C6",
                DividerLight = "#EFE7DB",
                LinesDefault = "#E4D6C6",
                LinesInputs = "#D8C6B2",

                // Actions
                ActionDefault = "#66584C",
                ActionDisabled = "#C4B8AB",
                ActionDisabledBackground = "#EFE7DB",

                // Table
                TableLines = "#E4D6C6",
                TableStriped = "#FAF5ED",
                TableHover = "#FFF7EF",

                // Feedback
                Info = "#3E84C6",
                Success = "#4E8A63",
                Warning = "#B88322",
                Error = "#C05050",
                Dark = "#1D2A33",
            },
            PaletteDark = new PaletteDark
            {
                // Primary accent (slightly brighter for dark bg)
                Primary = primaryLighten,
                PrimaryDarken = primary,
                PrimaryLighten = primaryLighten,
                PrimaryContrastText = "#0F1318",

                // Secondary — dark surface
                Secondary = "#44556A",
                SecondaryDarken = "#32404C",
                SecondaryLighten = "#6A7D95",
                SecondaryContrastText = "#F1F6FB",

                // Tertiary
                Tertiary = "#96A7B5",
                TertiaryDarken = "#7D8B99",
                TertiaryLighten = "#B8C6D1",
                TertiaryContrastText = "#0F1318",

                // Surfaces — deep blue-grey
                Background = "#0F1318",
                Surface = "#161D24",
                DrawerBackground = "#121920",
                DrawerText = "#EAF1F7",
                AppbarBackground = "#0C1117",
                AppbarText = "#F4F8FB",

                // Text
                TextPrimary = "#EAF1F7",
                TextSecondary = "#B7C5D1",
                TextDisabled = "#556575",

                // Lines & borders
                Divider = "#2E3A46",
                DividerLight = "#23303C",
                LinesDefault = "#2E3A46",
                LinesInputs = "#354351",

                // Actions
                ActionDefault = "#B7C5D1",
                ActionDisabled = "#3A4855",
                ActionDisabledBackground = "#1A232C",

                // Table
                TableLines = "#2E3A46",
                TableStriped = "#1A232C",
                TableHover = "#202C36",

                // Feedback
                Info = "#6EACDF",
                Success = "#7AB18B",
                Warning = "#D6A44A",
                Error = "#E07070",
                Dark = "#0C1117",
            },
            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    FontFamily = ["'Segoe UI'", "Roboto", "'Helvetica Neue'", "Arial", "sans-serif"],
                    FontSize = "0.875rem",
                    FontWeight = "400",
                    LineHeight = "1.5",
                    LetterSpacing = "normal",
                },
                H4 = new H4Typography
                {
                    FontSize = "1.75rem",
                    FontWeight = "600",
                    LineHeight = "1.3",
                },
                H5 = new H5Typography
                {
                    FontSize = "1.4rem",
                    FontWeight = "600",
                    LineHeight = "1.35",
                },
                H6 = new H6Typography
                {
                    FontSize = "1.125rem",
                    FontWeight = "600",
                    LineHeight = "1.4",
                },
                Subtitle1 = new Subtitle1Typography
                {
                    FontSize = "0.95rem",
                    FontWeight = "600",
                },
                Body1 = new Body1Typography
                {
                    FontSize = "0.875rem",
                    LineHeight = "1.6",
                },
                Body2 = new Body2Typography
                {
                    FontSize = "0.8125rem",
                    LineHeight = "1.55",
                },
                Caption = new CaptionTypography
                {
                    FontSize = "0.75rem",
                    FontWeight = "400",
                },
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "10px",
                DrawerWidthLeft = "240px",
            },
            Shadows = new MudBlazor.Shadow(),
        };
    }

    private static (string primary, string darken, string lighten) ResolveAccent(string accent)
    {
        return accent switch
        {
            "Ocean" => ("#3E84C6", "#2E6799", "#6EACDF"),
            "Berry" => ("#C05B86", "#984566", "#D889AB"),
            "Forest" => ("#4E8A63", "#3A6C4C", "#7AB18B"),
            "Gold" => ("#B88322", "#8F6617", "#D6A44A"),
            _ => ("#D96C54", "#BE533B", "#E39B74"), // Terracotta
        };
    }
}
