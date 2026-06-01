using AgentGroupChat.Core.Models.Domain;

namespace AgentGroupChat.Core.Services;

public static class RoomSpeechResolver
{
    public static bool IsSpeechEnabled(RoomConfig? room, AppSettings appSettings) =>
        room?.TtsEnabledOverride ?? appSettings.TtsEnabled;

    public static string GetProvider(RoomConfig? room, AppSettings appSettings)
    {
        var provider = room?.TtsProviderOverride;
        return string.IsNullOrWhiteSpace(provider) ? appSettings.TtsProvider : provider.Trim();
    }

    public static bool UsesKokoro(RoomConfig? room, AppSettings appSettings) =>
        string.Equals(GetProvider(room, appSettings), TtsProviders.Kokoro, StringComparison.OrdinalIgnoreCase);

    public static string GetFallbackVoice(RoomConfig? room, AppSettings appSettings)
    {
        var roomVoice = room?.TtsFallbackVoice;
        if (!string.IsNullOrWhiteSpace(roomVoice))
            return roomVoice.Trim();

        if (UsesKokoro(room, appSettings))
            return string.IsNullOrWhiteSpace(appSettings.KokoroVoice) ? "af_heart" : appSettings.KokoroVoice.Trim();

        return string.IsNullOrWhiteSpace(appSettings.TtsVoice) ? string.Empty : appSettings.TtsVoice.Trim();
    }

    public static string GetUserVoice(RoomConfig? room, AppSettings appSettings)
    {
        var roomVoice = room?.TtsUserVoice;
        if (!string.IsNullOrWhiteSpace(roomVoice))
            return roomVoice.Trim();

        if (UsesKokoro(room, appSettings) && !string.IsNullOrWhiteSpace(appSettings.KokoroUserVoice))
            return appSettings.KokoroUserVoice.Trim();

        return GetFallbackVoice(room, appSettings);
    }

    public static SpeechService.SpeechSettings BuildSettings(
        RoomConfig? room, AppSettings appSettings, bool forceEnabled = false) =>
        new(
            forceEnabled || IsSpeechEnabled(room, appSettings),
            GetProvider(room, appSettings),
            appSettings.TtsRate,
            appSettings.KokoroBaseUrl,
            appSettings.KokoroModel,
            GetFallbackVoice(room, appSettings),
            appSettings.KokoroLangCode,
            appSettings.KokoroSpeed,
            appSettings.PiperExePath,
            appSettings.PiperModelsDir);
}