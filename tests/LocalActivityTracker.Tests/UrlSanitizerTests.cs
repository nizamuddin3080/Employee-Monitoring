using LocalActivityTracker.Core.Privacy;
using Xunit;

namespace LocalActivityTracker.Tests;

public class UrlSanitizerTests
{
    private readonly UrlSanitizer _sanitizer = new();

    [Theory]
    [InlineData("https://example.com/login?token=secret12345&user=john", "https://example.com/login?user=john")]
    [InlineData("https://api.github.com/data?access_token=ghp_xyz999", "https://api.github.com/data")]
    [InlineData("https://service.com/auth?password=supersecret&apiKey=abc-123&lang=en", "https://service.com/auth?lang=en")]
    [InlineData("https://cloud.com/v1?sig=sha256hash&session_id=sess99&view=grid", "https://cloud.com/v1?view=grid")]
    [InlineData("https://admin:pass1234@secure.internal/dashboard", "https://secure.internal/dashboard")]
    public void Sanitize_StripsSensitiveParametersAndCredentials(string input, string expected)
    {
        var result = _sanitizer.Sanitize(input);

        Assert.Equal(expected, result.SanitizedUrl);
        Assert.False(result.IsRestricted);
    }

    [Fact]
    public void Sanitize_PreservesNormalQueryParameters()
    {
        var input = "https://github.com/udaysharmadev/Gravitas?tab=readme&sort=newest";
        var result = _sanitizer.Sanitize(input);

        Assert.Equal(input, result.SanitizedUrl);
        Assert.Equal("github.com", result.Domain);
        Assert.Equal("github.com", result.Hostname);
        Assert.False(result.IsRestricted);
    }

    [Fact]
    public void Sanitize_StripsFragmentContainingAccessToken()
    {
        var input = "https://oauth.example.com/callback#access_token=xyz123&state=abc";
        var result = _sanitizer.Sanitize(input);

        Assert.DoesNotContain("access_token", result.SanitizedUrl);
        Assert.DoesNotContain("#", result.SanitizedUrl);
    }

    [Theory]
    [InlineData("chrome://version", true, "chrome")]
    [InlineData("chrome://extensions", true, "chrome")]
    [InlineData("chrome-extension://abcdefghijklmnop/popup.html", true, "chrome-extension")]
    [InlineData("edge://settings", true, "edge")]
    [InlineData("about:blank", true, "about")]
    [InlineData("file:///C:/Users/test/doc.pdf", true, "file")]
    public void Sanitize_IdentifiesRestrictedSchemes(string input, bool expectedRestricted, string expectedDomain)
    {
        var result = _sanitizer.Sanitize(input);

        Assert.Equal(expectedRestricted, result.IsRestricted);
        Assert.Equal(expectedDomain, result.Domain);
    }

    [Fact]
    public void Sanitize_ExtractsRootDomainCorrectly()
    {
        var result = _sanitizer.Sanitize("https://subdomain.developer.docs.microsoft.com/en-us/windows");

        Assert.Equal("microsoft.com", result.Domain);
        Assert.Equal("subdomain.developer.docs.microsoft.com", result.Hostname);
    }
}
