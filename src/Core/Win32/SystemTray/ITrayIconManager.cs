namespace LocalActivityTracker.Core.Win32.SystemTray;

/// <summary>
/// Service contract for managing the Windows notification area (system tray) icon and user interactions.
/// </summary>
public interface ITrayIconManager : IDisposable
{
    /// <summary>
    /// Event raised when the user requests to open the local dashboard.
    /// </summary>
    event EventHandler? OpenDashboardRequested;

    /// <summary>
    /// Event raised when the user requests to exit the coordinator application.
    /// </summary>
    event EventHandler? ExitRequested;

    /// <summary>
    /// Initializes and displays the system tray icon with a tooltip.
    /// </summary>
    void Initialize(string initialTooltip);

    /// <summary>
    /// Updates the system tray icon tooltip and status.
    /// </summary>
    void UpdateTooltip(string tooltip);

    /// <summary>
    /// Removes the system tray icon from the notification area.
    /// </summary>
    void Remove();
}
