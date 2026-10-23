namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Service contract for monitoring Windows desktop foreground window activations in the interactive user session.
/// </summary>
public interface IFocusMonitor : IDisposable
{
    /// <summary>
    /// Event raised when the Windows desktop foreground focus changes.
    /// </summary>
    event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

    /// <summary>
    /// Indicates whether the focus monitor hook and message pump are actively running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the Win32 foreground hook and message pump on a background thread.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the Win32 foreground hook and shuts down the message pump.
    /// </summary>
    void Stop();

    /// <summary>
    /// Synchronously retrieves the current foreground window focus state.
    /// </summary>
    ForegroundChangedEventArgs? GetCurrentForeground();
}
