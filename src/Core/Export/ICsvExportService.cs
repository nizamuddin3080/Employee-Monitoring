namespace LocalActivityTracker.Core.Export;

/// <summary>
/// Contract for generating unified, multi-section CSV reports with date boundary clipping
/// and formula injection neutralization.
/// </summary>
public interface ICsvExportService
{
    Task<CsvExportResult> ExportToCsvAsync(CsvExportRequest request, CancellationToken cancellationToken = default);
}
