namespace LocalActivityTracker.Core.Models;

public class UrlActivitySession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string ProfileId { get; set; } = string.Empty;
    public string? WindowSessionId { get; set; }
    public int ChromeTabId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string SanitizedUrl { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndTimeUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public int ActiveDurationSeconds { get; set; } = 0;
    public bool IsCompleted { get; set; } = false;
    public string SessionStatus { get; set; } = "ACTIVE";
    public string? CompletenessNotes { get; set; }
}
