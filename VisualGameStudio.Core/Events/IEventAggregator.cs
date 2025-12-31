namespace VisualGameStudio.Core.Events;

public interface IEventAggregator
{
    void Publish<TEvent>(TEvent eventToPublish) where TEvent : class;
    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
    void Unsubscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
}
