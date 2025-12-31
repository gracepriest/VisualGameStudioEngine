using System.Collections.Concurrent;

namespace VisualGameStudio.Core.Events;

public class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, List<WeakReference>> _subscribers = new();
    private readonly object _lock = new();

    public void Publish<TEvent>(TEvent eventToPublish) where TEvent : class
    {
        if (eventToPublish == null) return;

        var eventType = typeof(TEvent);
        if (!_subscribers.TryGetValue(eventType, out var subscribers)) return;

        List<WeakReference> deadRefs = new();
        List<Action<TEvent>> handlersToInvoke = new();

        lock (_lock)
        {
            foreach (var weakRef in subscribers)
            {
                if (weakRef.Target is Action<TEvent> handler)
                {
                    handlersToInvoke.Add(handler);
                }
                else
                {
                    deadRefs.Add(weakRef);
                }
            }

            foreach (var deadRef in deadRefs)
            {
                subscribers.Remove(deadRef);
            }
        }

        foreach (var handler in handlersToInvoke)
        {
            try
            {
                handler(eventToPublish);
            }
            catch
            {
                // Swallow exceptions from handlers to prevent one handler from breaking others
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var eventType = typeof(TEvent);
        var subscribers = _subscribers.GetOrAdd(eventType, _ => new List<WeakReference>());

        lock (_lock)
        {
            subscribers.Add(new WeakReference(handler));
        }

        return new Subscription<TEvent>(this, handler);
    }

    public void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class
    {
        var eventType = typeof(TEvent);
        if (!_subscribers.TryGetValue(eventType, out var subscribers)) return;

        lock (_lock)
        {
            var toRemove = subscribers.FirstOrDefault(w => ReferenceEquals(w.Target, handler));
            if (toRemove != null)
            {
                subscribers.Remove(toRemove);
            }
        }
    }

    private class Subscription<TEvent> : IDisposable where TEvent : class
    {
        private readonly EventAggregator _aggregator;
        private readonly Action<TEvent> _handler;
        private bool _disposed;

        public Subscription(EventAggregator aggregator, Action<TEvent> handler)
        {
            _aggregator = aggregator;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _aggregator.Unsubscribe(_handler);
        }
    }
}
