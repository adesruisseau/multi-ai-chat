namespace AgentGroupChat.Core;

public static class XmlTags
{
    public const string PrivilegedActions = "privileged_actions";
    public const string SpawnNpc = "spawn_npc";
    public const string DismissNpc = "dismiss_npc";
    public const string SuspendAgent = "suspend_agent";
    public const string ResumeAgent = "resume_agent";
    public const string Reply = "reply";
    public const string FutureNote = "future_note";
    public const string DurableMemory = "durable_memory";
}

public static class TtsProviders
{
    public const string Local = "Local";
    public const string Piper = "Piper";
    public const string Kokoro = "Kokoro";
}

public static class LlmTransports
{
    public const string OpenAiCompatible = "OpenAI Compatible";
    public const string Groq = "Groq";
    public const string Gemini = "Gemini";
    public const string HuggingFace = "HuggingFace";
    public const string Ollama = "Ollama";
}

public static class ImageTransports
{
    public const string ComfyUI = "ComfyUI";
    public const string OpenAiImages = "OpenAI Images";

    public const string GoogleImagen = "Google Imagen";
}

public static class LlmRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
}

public static class ChatPlaceholders
{
    public const string Thinking = "Thinking...";
}

public static class ActorKinds
{
    public const string HumanParticipant = "HumanParticipant";
    public const string AiAgent = "AiAgent";
}

public static class SpeakerNames
{
    public const string DefaultHuman = "You";
}
