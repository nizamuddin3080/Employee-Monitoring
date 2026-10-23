namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Immutable representation of an application's process identity.
/// Strictly omits window titles, captions, or document names to preserve privacy.
/// </summary>
public sealed record ProcessInfo(
    uint ProcessId,
    string ProcessName,
    string ApplicationDisplayName,
    string? ExecutablePath
);
