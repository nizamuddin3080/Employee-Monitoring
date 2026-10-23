namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Service contract for resolving process and application display names from Win32 window handles or process IDs.
/// Strictly enforces zero collection of window titles, document names, or captions.
/// </summary>
public interface IProcessInfoResolver
{
    /// <summary>
    /// Resolves process identity for a given window handle.
    /// </summary>
    ProcessInfo? Resolve(IntPtr hWnd);

    /// <summary>
    /// Resolves process identity for a given process identifier.
    /// </summary>
    ProcessInfo? ResolveByProcessId(uint processId);

    /// <summary>
    /// Returns true if the specified process name corresponds to Google Chrome (chrome.exe).
    /// </summary>
    bool IsChromeProcess(string processName);
}
