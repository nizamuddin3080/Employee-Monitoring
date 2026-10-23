using LocalActivityTracker.Core.Models;

namespace LocalActivityTracker.Core.Repositories;

public interface IProfileRepository
{
    Task UpsertProfileAsync(ChromeProfile profile, CancellationToken cancellationToken = default);
    Task<ChromeProfile?> GetProfileByIdAsync(string profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChromeProfile>> GetAllProfilesAsync(CancellationToken cancellationToken = default);
    Task UpdateConnectionStatusAsync(string profileId, string status, CancellationToken cancellationToken = default);
    Task UpdateLastSeenAsync(string profileId, DateTime lastSeenUtc, CancellationToken cancellationToken = default);
    Task UpdateLastScreenshotTimeAsync(string profileId, DateTime screenshotTimeUtc, CancellationToken cancellationToken = default);
}
