namespace AgentGroupChat.Core.Models.Domain;

public sealed class AiConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Transport { get; set; } = "OpenAI Compatible";
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
