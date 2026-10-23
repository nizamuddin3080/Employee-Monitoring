using System.Text.Json.Serialization;

namespace LocalActivityTracker.Core.Export;

/// <summary>
/// Result metadata returned after generating the master CSV export.
/// </summary>
public sealed class CsvExportResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "SUCCESS";

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = string.Empty;

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("record_counts")]
    public CsvRecordCounts RecordCounts { get; set; } = new();
}

public sealed class CsvRecordCounts
{
    [JsonPropertyName("profiles_included")]
    public int ProfilesIncluded { get; set; }

    [JsonPropertyName("url_sessions_exported")]
    public int UrlSessionsExported { get; set; }

    [JsonPropertyName("screenshot_records_exported")]
    public int ScreenshotRecordsExported { get; set; }

    [JsonPropertyName("application_sessions_exported")]
    public int ApplicationSessionsExported { get; set; }
}
