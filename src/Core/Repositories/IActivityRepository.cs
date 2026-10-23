using LocalActivityTracker.Core.Models;

namespace LocalActivityTracker.Core.Repositories;

public interface IActivityRepository
{
    // URL Activity Sessions
    Task OpenUrlSessionAsync(UrlActivitySession session, CancellationToken cancellationToken = default);
    Task UpdateUrlSessionHeartbeatAsync(string sessionId, DateTime heartbeatUtc, CancellationToken cancellationToken = default);
    Task CloseUrlSessionAsync(string sessionId, DateTime endTimeUtc, int activeDurationSeconds, string status = "COMPLETED", CancellationToken cancellationToken = default);
    Task<UrlActivitySession?> GetUrlSessionByIdAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UrlActivitySession>> GetUrlSessionsInRangeAsync(string profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    // Browser Sessions (Chrome Foreground)
    Task OpenBrowserSessionAsync(BrowserSession session, CancellationToken cancellationToken = default);
    Task CloseBrowserSessionAsync(string sessionId, DateTime endTimeUtc, int durationSeconds, string status = "COMPLETED", CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BrowserSession>> GetBrowserSessionsInRangeAsync(string? profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    // Tab Events
    Task InsertTabEventAsync(TabEvent tabEvent, CancellationToken cancellationToken = default);

    // Non-Chrome Application Sessions (Strictly NO window_title)
    Task OpenApplicationSessionAsync(ApplicationSession session, CancellationToken cancellationToken = default);
    Task UpdateApplicationSessionHeartbeatAsync(string sessionId, DateTime heartbeatUtc, CancellationToken cancellationToken = default);
    Task CloseApplicationSessionAsync(string sessionId, DateTime endTimeUtc, int durationSeconds, string status = "COMPLETED", CancellationToken cancellationToken = default);
    Task<ApplicationSession?> GetApplicationSessionByIdAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApplicationSession>> GetApplicationSessionsInRangeAsync(DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);

    // Screenshot Metadata
    Task InsertScreenshotMetadataAsync(ScreenshotMetadata metadata, CancellationToken cancellationToken = default);
    Task<ScreenshotMetadata?> GetScreenshotMetadataByIdAsync(string screenshotId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScreenshotMetadata>> GetScreenshotsInRangeAsync(string? profileId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
    Task<bool> DeleteScreenshotMetadataAsync(string screenshotId, CancellationToken cancellationToken = default);

    // Crash Recovery
    Task<int> RecoverDanglingSessionsAsync(CancellationToken cancellationToken = default);
}
