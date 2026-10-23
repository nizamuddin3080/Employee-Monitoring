using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

public class SessionNotificationListenerTests
{
    [Fact]
    public void SessionNotificationListener_Lifecycle_StartsAndStopsCleanly()
    {
        using var listener = new SessionNotificationListener();

        Assert.False(listener.IsListening);

        listener.Start();
        Assert.True(listener.IsListening);

        listener.Stop();
        Assert.False(listener.IsListening);
    }

    [Fact]
    public void SessionNotificationListener_MultipleStartStopCycles_DoesNotThrow()
    {
        using var listener = new SessionNotificationListener();

        for (int i = 0; i < 3; i++)
        {
            listener.Start();
            Assert.True(listener.IsListening);

            listener.Stop();
            Assert.False(listener.IsListening);
        }
    }
}
