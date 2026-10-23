namespace LocalActivityTracker.Core.Models;

public class ChromeProfile
{
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string? ChromeProfileDir { get; set; }
    public bool IsEnrolled { get; set; } = true;
    public string ConnectionStatus { get; set; } = "OFFLINE";
    public DateTime EnrolledAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastScreenshotAtUtc { get; set; }
}
