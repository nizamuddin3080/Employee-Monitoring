namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Service contract for receiving Windows session lock/unlock and system power suspend/resume events in the interactive desktop session.
/// </summary>
public interface ISessionNotificationListener : IDisposable
{
    /// <summary>
    /// Event raised when the Windows user session is locked (WTS_SESSION_LOCK).
    /// </summary>
    event EventHandler? SessionLocked;

    /// <summary>
    /// Event raised when the Windows user session is unlocked (WTS_SESSION_UNLOCK).
    /// </summary>
    event EventHandler? SessionUnlocked;

    /// <summary>
    /// Event raised when the system enters sleep or suspend (PBT_APMSUSPEND).
    /// </summary>
    event EventHandler? Suspended;

    /// <summary>
    /// Event raised when the system resumes from sleep or suspend (PBT_APMRESUMESUSPEND).
    /// </summary>
    event EventHandler? Resumed;

    /// <summary>
    /// Indicates whether the listener is active and registered with Windows Terminal Services.
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// Registers for session and power notifications.
    /// </summary>
    void Start();

    /// <summary>
    /// Unregisters and frees native window handles.
    /// </summary>
    void Stop();
}
