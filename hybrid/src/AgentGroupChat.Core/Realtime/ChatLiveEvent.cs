using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Core.Realtime
{
    public sealed record ChatLiveEvent(string RoomId, string Kind);
}
