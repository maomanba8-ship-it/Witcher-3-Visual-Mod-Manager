using System;
using Witcher3ModManager.Core;
using Xunit;

namespace Witcher3ModManager.Tests;

public class EventSubscriptionTests
{
    [Fact]
    public void Dispose_runs_the_unsubscribe_action_once()
    {
        var calls = 0;
        var subscription = new EventSubscription(() => calls++);

        Assert.False(subscription.IsDisposed);
        subscription.Dispose();
        subscription.Dispose();
        subscription.Dispose();

        Assert.Equal(1, calls);
        Assert.True(subscription.IsDisposed);
    }

    [Fact]
    public void It_removes_only_its_own_handler_from_a_real_event()
    {
        var raised = 0;
        Action? evt = null;
        Action first = () => raised += 1;
        Action second = () => raised += 100;
        evt += first;
        evt += second;

        // Mirrors real use: the token removes exactly the handler it was created for, leaving
        // other subscribers untouched.
        var token = new EventSubscription(() => evt -= second);
        token.Dispose();
        evt?.Invoke();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Subscribing_again_after_dispose_does_not_stack_handlers()
    {
        var raised = 0;
        Action? evt = null;
        Action handler = () => raised += 10;

        var first = new EventSubscription(() => evt -= handler);
        evt += handler;
        first.Dispose();               // the view unloaded, so nothing is subscribed

        var second = new EventSubscription(() => evt -= handler);
        evt += handler;
        evt?.Invoke();

        Assert.Equal(10, raised);      // exactly one handler, so nothing stacked up

        second.Dispose();
        evt?.Invoke();

        Assert.Equal(10, raised);      // and it is gone again
    }

    [Fact]
    public void A_null_unsubscribe_action_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new EventSubscription(null!));
    }
}
