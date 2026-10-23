namespace LocalActivityTracker.Core.Models;

public class ScreenshotMetadata
{
    public string ScreenshotId { get; set; } = Guid.NewGuid().ToString();
    public string ProfileId { get; set; } = string.Empty;
    public string? UrlSessionId { get; set; }
    public DateTime CaptureTimeUtc { get; set; } = DateTime.UtcNow;
    public string Url { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; } = 0;
    public string CaptureStatus { get; set; } = "SUCCESS";
    public string? FailureReason { get; set; }
}
