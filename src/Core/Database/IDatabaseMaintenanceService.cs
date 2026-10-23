namespace LocalActivityTracker.Core.Database;

/// <summary>
/// Service managing online database backups, data retention purges, and safe data eradication.
/// </summary>
public interface IDatabaseMaintenanceService
{
    Task<string> CreateBackupAsync(CancellationToken cancellationToken = default);
    Task<int> PurgeOldRecordsAsync(int olderThanDays, CancellationToken cancellationToken = default);
    Task PurgeAllDataAsync(CancellationToken cancellationToken = default);
}
