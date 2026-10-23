namespace LocalActivityTracker.Core.Models;

public class BrowserWindow
{
    public string WindowSessionId { get; set; } = Guid.NewGuid().ToString();
    public string ProfileId { get; set; } = string.Empty;
    public int ChromeWindowId { get; set; }
    public string WindowType { get; set; } = "normal";
    public bool IsFocused { get; set; } = false;
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
}
