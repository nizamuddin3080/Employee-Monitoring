using System.Text.Json;
using LocalActivityTracker.Core.Repositories;

namespace LocalActivityTracker.Core.Profiles;

/// <summary>
/// Scans Google Chrome's Local State file in %LOCALAPPDATA%\Google\Chrome\User Data to identify all existing profiles
/// and verify which ones are enrolled in Local Activity Tracker.
/// </summary>
public sealed class ChromeProfileScanner : IChromeProfileScanner
{
    private readonly IProfileRepository? _profileRepository;

    public ChromeProfileScanner(IProfileRepository? profileRepository = null)
    {
        _profileRepository = profileRepository;
    }

    public IReadOnlyList<DiscoveredChromeProfile> DiscoverLocalProfiles(string? customUserDataPath = null)
    {
        var userDataPath = customUserDataPath;
        if (string.IsNullOrWhiteSpace(userDataPath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            userDataPath = Path.Combine(localAppData, @"Google\Chrome\User Data");
        }

        if (!Directory.Exists(userDataPath))
        {
            return Array.Empty<DiscoveredChromeProfile>();
        }

        var localStatePath = Path.Combine(userDataPath, "Local State");
        var profiles = new List<DiscoveredChromeProfile>();

        if (File.Exists(localStatePath))
        {
            try
            {
                using var stream = File.OpenRead(localStatePath);
                using var doc = JsonDocument.Parse(stream);

                if (doc.RootElement.TryGetProperty("profile", out var profileElem) &&
                    profileElem.TryGetProperty("info_cache", out var infoCacheElem))
                {
                    foreach (var prop in infoCacheElem.EnumerateObject())
                    {
                        var dirName = prop.Name;
                        var val = prop.Value;

                        var name = val.TryGetProperty("name", out var nProp) ? nProp.GetString() : null;
                        var userName = val.TryGetProperty("user_name", out var uProp) ? uProp.GetString() : null;
                        var avatar = val.TryGetProperty("avatar_icon", out var aProp) ? aProp.GetString() : null;

                        var displayName = !string.IsNullOrWhiteSpace(name) ? name :
                                          !string.IsNullOrWhiteSpace(userName) ? userName : dirName;

                        profiles.Add(new DiscoveredChromeProfile(dirName, displayName, avatar, false));
                    }
                }
            }
            catch
            {
                // Suppress JSON parse/read errors if Chrome is locking the file
            }
        }

        // Fallback: enumerate profile directories if Local State parsing yielded nothing
        if (profiles.Count == 0)
        {
            var defaultDir = Path.Combine(userDataPath, "Default");
            if (Directory.Exists(defaultDir))
            {
                profiles.Add(new DiscoveredChromeProfile("Default", "Default", null, false));
            }

            foreach (var dir in Directory.GetDirectories(userDataPath, "Profile *"))
            {
                var dirName = Path.GetFileName(dir);
                profiles.Add(new DiscoveredChromeProfile(dirName, dirName, null, false));
            }
        }

        // If repository provided, correlate with enrolled profiles
        if (_profileRepository != null)
        {
            try
            {
                var enrolledProfiles = _profileRepository.GetAllProfilesAsync().GetAwaiter().GetResult();
                var enrolledDirNames = new HashSet<string>(
                    enrolledProfiles.Select(p => p.ChromeProfileDir ?? string.Empty).Where(d => !string.IsNullOrEmpty(d)),
                    StringComparer.OrdinalIgnoreCase);

                var enrolledNames = new HashSet<string>(
                    enrolledProfiles.Select(p => p.ProfileName),
                    StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < profiles.Count; i++)
                {
                    var p = profiles[i];
                    var isEnrolled = enrolledDirNames.Contains(p.ProfileDirectoryName) || enrolledNames.Contains(p.DisplayName);
                    if (isEnrolled)
                    {
                        profiles[i] = p with { IsEnrolled = true };
                    }
                }
            }
            catch
            {
                // Suppress repository query exceptions
            }
        }

        return profiles;
    }
}
