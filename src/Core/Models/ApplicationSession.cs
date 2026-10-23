namespace LocalActivityTracker.Core.Models;

public class ApplicationSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string ApplicationDisplayName { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndTimeUtc { get; set; }
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public int DurationSeconds { get; set; } = 0;
    public bool IsCompleted { get; set; } = false;
    public string SessionStatus { get; set; } = "ACTIVE";
}
