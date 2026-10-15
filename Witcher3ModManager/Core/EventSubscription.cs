namespace Witcher3ModManager.Core;

/// <summary>
/// Token returned when a view subscribes to a long-lived event. Disposing it removes exactly the
/// handler that was registered, and disposing twice is a no-op, so a view that subscribes on
/// every load can drop the previous subscription first instead of stacking copies up.
/// </summary>
public sealed class EventSubscription : IDisposable
{
    private Action? _unsubscribe;

    public EventSubscription(Action unsubscribe)
    {
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
    }

    public bool IsDisposed => _unsubscribe is null;

    public void Dispose()
    {
        var unsubscribe = _unsubscribe;
        _unsubscribe = null;
        unsubscribe?.Invoke();
    }
}
