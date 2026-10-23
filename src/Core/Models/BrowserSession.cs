namespace LocalActivityTracker.Core.Models;

public class BrowserSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public string ProfileId { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndTimeUtc { get; set; }
    public int DurationSeconds { get; set; } = 0;
    public bool IsCompleted { get; set; } = false;
    public string SessionStatus { get; set; } = "ACTIVE";
}
