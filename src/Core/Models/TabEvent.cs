namespace LocalActivityTracker.Core.Models;

public class TabEvent
{
    public long EventId { get; set; }
    public string ProfileId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public int ChromeTabId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? EventPayloadJson { get; set; }
    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;
}
