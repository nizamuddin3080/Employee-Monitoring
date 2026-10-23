namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Event arguments fired when the active Windows desktop foreground window switches.
/// Strictly omits window titles, captions, and document names.
/// </summary>
public sealed class ForegroundChangedEventArgs : EventArgs
{
    public IntPtr WindowHandle { get; }
    public uint ProcessId { get; }
    public string ProcessName { get; }
    public string ApplicationDisplayName { get; }
    public bool IsChrome { get; }
    public DateTime TimestampUtc { get; }

    public ForegroundChangedEventArgs(
        IntPtr windowHandle,
        uint processId,
        string processName,
        string applicationDisplayName,
        bool isChrome,
        DateTime timestampUtc)
    {
        WindowHandle = windowHandle;
        ProcessId = processId;
        ProcessName = processName;
        ApplicationDisplayName = applicationDisplayName;
        IsChrome = isChrome;
        TimestampUtc = timestampUtc;
    }
}
