using System.Text.Json.Serialization;

namespace LocalActivityTracker.Core.Export;

/// <summary>
/// Parameters for custom date range master CSV export.
/// Supports both snake_case and camelCase serialization.
/// </summary>
public sealed class CsvExportRequest
{
    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    [JsonPropertyName("profile_ids")]
    public List<string>? ProfileIds { get; set; } = new();

    [JsonPropertyName("include_report_metadata")]
    public bool IncludeReportMetadata { get; set; } = true;

    [JsonPropertyName("include_profile_summary")]
    public bool IncludeProfileSummary { get; set; } = true;

    [JsonPropertyName("include_url_activity")]
    public bool IncludeUrlActivity { get; set; } = true;

    [JsonPropertyName("include_screenshot_metadata")]
    public bool IncludeScreenshotMetadata { get; set; } = true;

    [JsonPropertyName("include_non_chrome_apps")]
    public bool IncludeNonChromeApps { get; set; } = true;
}
