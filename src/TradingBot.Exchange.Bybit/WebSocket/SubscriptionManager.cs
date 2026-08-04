using System.Collections.Concurrent;
using System.Collections.Generic;

namespace TradingBot.Exchange.Bybit.WebSocket;

public class SubscriptionManager
{
    private readonly ConcurrentDictionary<string, bool> _publicSubscriptions = new();
    private readonly ConcurrentDictionary<string, bool> _privateSubscriptions = new();

    public bool AddPublicSubscription(string topic)
    {
        return _publicSubscriptions.TryAdd(topic, true);
    }

    public bool AddPrivateSubscription(string topic)
    {
        return _privateSubscriptions.TryAdd(topic, true);
    }

    public IReadOnlyCollection<string> GetPublicSubscriptions() => _publicSubscriptions.Keys.ToArray();
    public IReadOnlyCollection<string> GetPrivateSubscriptions() => _privateSubscriptions.Keys.ToArray();

    public void Clear()
    {
        _publicSubscriptions.Clear();
        _privateSubscriptions.Clear();
    }
}
