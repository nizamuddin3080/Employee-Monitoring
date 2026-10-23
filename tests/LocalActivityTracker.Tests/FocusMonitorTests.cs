using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

public class FocusMonitorTests
{
    [Fact]
    public void FocusMonitor_Lifecycle_StartsAndStopsCleanly()
    {
        var resolver = new ProcessInfoResolver();
        using var monitor = new FocusMonitor(resolver);

        Assert.False(monitor.IsRunning);

        monitor.Start();
        Assert.True(monitor.IsRunning);

        // Get current foreground synchronously
        var current = monitor.GetCurrentForeground();
        if (current != null)
        {
            Assert.False(string.IsNullOrWhiteSpace(current.ProcessName));
            Assert.False(string.IsNullOrWhiteSpace(current.ApplicationDisplayName));
        }

        monitor.Stop();
        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public void FocusMonitor_MultipleStartStopCycles_DoesNotThrow()
    {
        var resolver = new ProcessInfoResolver();
        using var monitor = new FocusMonitor(resolver);

        for (int i = 0; i < 3; i++)
        {
            monitor.Start();
            Assert.True(monitor.IsRunning);

            monitor.Stop();
            Assert.False(monitor.IsRunning);
        }
    }
}
