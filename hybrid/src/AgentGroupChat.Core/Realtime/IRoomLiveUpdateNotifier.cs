using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Core.Realtime
{
    public interface IRoomLiveUpdateNotifier
    {
        IDisposable Subscribe(string roomId, Func<RoomLiveEvent, Task> handler);
        Task PublishAsync(RoomLiveEvent update, CancellationToken cancellationToken = default);
    }
}
