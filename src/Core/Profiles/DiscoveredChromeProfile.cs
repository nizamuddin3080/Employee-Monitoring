namespace LocalActivityTracker.Core.Profiles;

/// <summary>
/// Represents a Chrome profile discovered on the local workstation filesystem.
/// </summary>
public sealed record DiscoveredChromeProfile(
    string ProfileDirectoryName,
    string DisplayName,
    string? AvatarIcon,
    bool IsEnrolled
);
