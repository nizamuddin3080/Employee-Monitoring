namespace LocalActivityTracker.Core.Privacy;

/// <summary>
/// Result of URL sanitization with sensitive query parameters and credentials stripped.
/// </summary>
public sealed record SanitizedUrlResult(
    string RawUrl,
    string SanitizedUrl,
    string Domain,
    string Hostname,
    bool IsRestricted
);
