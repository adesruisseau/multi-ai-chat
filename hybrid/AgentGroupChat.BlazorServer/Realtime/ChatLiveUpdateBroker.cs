using AgentGroupChat.Core.Realtime;
using Microsoft.AspNetCore.Components;
using System.Collections.Concurrent;

namespace AgentGroupChat.BlazorServer.Realtime
{
    public sealed class ChatLiveUpdateBroker : IChatLiveUpdateNotifier
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<ChatLiveEvent, Task>>> _subscriptions = new(StringComparer.Ordinal);

        public IDisposable Subscribe(string roomId, Func<ChatLiveEvent, Task> handler)
        {
            var subscriptionId = Guid.NewGuid();
            var roomSubscription = _subscriptions.GetOrAdd(roomId, static _ => new ConcurrentDictionary<Guid, Func<ChatLiveEvent, Task>>());

            roomSubscription[subscriptionId] = handler;

            return new Subscription(() => Unsubscribe(roomId, subscriptionId));
        }


        public async Task PublishAsync(ChatLiveEvent update, CancellationToken cancellationToken = default)
        {
            if (!_subscriptions.TryGetValue(update.RoomId, out var roomSubscriptions))
                return;

            foreach (var handler in roomSubscriptions.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await handler(update);
                }
                catch
                {
                    //log later
                }
            }
        }

        private void Unsubscribe(string roomId, Guid subscriptionId)
        {
            if (!_subscriptions.TryGetValue(roomId, out var roomSubscriptions))
                return;

            roomSubscriptions.TryRemove(subscriptionId, out _);

            if (roomSubscriptions.IsEmpty)
                _subscriptions.TryRemove(roomId, out _);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;
            private int _disposed;

            public Subscription(Action dispose) => _dispose = dispose;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 1)
                    return;

                _dispose();
            }
        }
    }
}
