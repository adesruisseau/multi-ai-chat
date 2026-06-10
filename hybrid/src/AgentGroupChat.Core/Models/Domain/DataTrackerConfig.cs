using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Core.Models.Domain
{
    public sealed class DataTrackerConfig
    {
        public int Id { get; set; }
        public string RoomId { get; set; } = string.Empty;
        public string? AgentId { get; set; } = null;
        public string DataKey { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string ValueType { get; set; } = "string";
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public bool Enabled { get; set; } = true;
        public DateTimeOffset CreatedAt { get; set; }
        public string PromptText { get; set; }
        public string PrivilegedAgentPrompt { get; set; }
    }
}
