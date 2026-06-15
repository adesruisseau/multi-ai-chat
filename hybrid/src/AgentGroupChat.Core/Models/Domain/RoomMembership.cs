namespace AgentGroupChat.Core.Models.Domain;

public sealed class RoomMembership
{
    public int Id { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = RoomMembershipRoles.Player;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}