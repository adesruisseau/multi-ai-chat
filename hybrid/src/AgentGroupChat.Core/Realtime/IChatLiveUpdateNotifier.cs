using System;
using System.Collections.Generic;
using System.Text;

namespace AgentGroupChat.Core.Realtime
{
    public interface IChatLiveUpdateNotifier
    {
        IDisposable Subscribe(string roomId, Func<ChatLiveEvent, Task> handler);
        Task PublishAsync(ChatLiveEvent chatUpdate, CancellationToken cancellationToken = default);
    }
}
