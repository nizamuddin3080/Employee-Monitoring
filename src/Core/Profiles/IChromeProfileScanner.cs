namespace LocalActivityTracker.Core.Profiles;

/// <summary>
/// Scans the local workstation's Google Chrome User Data to discover all Chrome profiles
/// and report coverage honesty (enrolled vs unenrolled profiles).
/// </summary>
public interface IChromeProfileScanner
{
    IReadOnlyList<DiscoveredChromeProfile> DiscoverLocalProfiles(string? customUserDataPath = null);
}
