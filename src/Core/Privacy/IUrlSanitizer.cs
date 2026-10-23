namespace LocalActivityTracker.Core.Privacy;

/// <summary>
/// Service contract for sanitizing URLs, stripping sensitive query tokens and passwords, and identifying restricted schemes.
/// </summary>
public interface IUrlSanitizer
{
    SanitizedUrlResult Sanitize(string? rawUrl);
}
