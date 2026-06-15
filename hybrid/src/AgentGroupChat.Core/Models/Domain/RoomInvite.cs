using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace AgentGroupChat.Core.Models.Domain
{
    public class RoomInvite
    {
        public Guid Id { get; set; }
        public string HostUserId { get; set; }
        public string RoomId { get; set; }
        public DateTimeOffset CreatedDate { get; set; }
        public DateTimeOffset ExpirationDate { get; set; }
        public string? RedeemedByUserId { get; set; }
        public DateTimeOffset? RedeemedDate { get; set; }

    }
}
