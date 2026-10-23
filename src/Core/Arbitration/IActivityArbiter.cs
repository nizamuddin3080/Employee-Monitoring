using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Win32;

namespace LocalActivityTracker.Core.Arbitration;

/// <summary>
/// Service contract for dual-sourced focus reconciliation and duration arbitration.
/// Accrues active URL duration strictly when Chrome holds OS desktop focus and the tab is selected.
/// Accrues non-Chrome duration without window titles when other applications are focused.
/// </summary>
public interface IActivityArbiter : IDisposable
{
    bool IsChromeForeground { get; }
    bool IsTrackingPaused { get; }
    string? CurrentChromeProfileId { get; }
    UrlActivitySession? CurrentUrlSession { get; }
    BrowserSession? CurrentBrowserSession { get; }
    ApplicationSession? CurrentAppSession { get; }

    Task OnForegroundWindowChangedAsync(ForegroundChangedEventArgs e);
    Task OnIpcMessageReceivedAsync(string profileId, IpcMessage message);

    Task OnSessionLockedAsync();
    Task OnSessionUnlockedAsync();
    Task OnSuspendedAsync();
    Task OnResumedAsync();
}
