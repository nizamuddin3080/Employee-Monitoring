using System.Diagnostics;
using System.Reflection;
using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

public class ProcessInfoResolverTests
{
    private readonly ProcessInfoResolver _resolver = new();

    [Fact]
    public void ResolveByProcessId_CurrentProcess_ResolvesProcessAndDisplayName()
    {
        // Arrange
        var currentPid = (uint)Process.GetCurrentProcess().Id;

        // Act
        var info = _resolver.ResolveByProcessId(currentPid);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(currentPid, info.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(info.ProcessName));
        Assert.EndsWith(".exe", info.ProcessName, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(info.ApplicationDisplayName));
        Assert.NotNull(info.ExecutablePath);
        Assert.True(File.Exists(info.ExecutablePath));
    }

    [Fact]
    public void ResolveByProcessId_SystemProcess_ResolvesSystem()
    {
        // Act
        var info = _resolver.ResolveByProcessId(4);

        // Assert
        Assert.NotNull(info);
        Assert.Equal(4u, info.ProcessId);
        Assert.Equal("System", info.ProcessName);
        Assert.Equal("System", info.ApplicationDisplayName);
    }

    [Fact]
    public void ResolveByProcessId_NonExistentPid_ReturnsNullGracefully()
    {
        // Act: PID 99999999 is extraordinarily unlikely to exist
        var info = _resolver.ResolveByProcessId(99_999_999);

        // Assert
        Assert.Null(info);
    }

    [Theory]
    [InlineData("chrome.exe", true)]
    [InlineData("CHROME.EXE", true)]
    [InlineData("Chrome.exe", true)]
    [InlineData("Code.exe", false)]
    [InlineData("notepad.exe", false)]
    [InlineData("msedge.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsChromeProcess_EvaluatesCorrectly(string? processName, bool expected)
    {
        // Act
        var result = _resolver.IsChromeProcess(processName!);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void PrivacyInvariant_ProcessInfoHasZeroWindowTitleProperties()
    {
        // Assert: ADR-012 non-Chrome privacy invariant
        var properties = typeof(ProcessInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var propertyNames = properties.Select(p => p.Name).ToList();

        Assert.DoesNotContain("WindowTitle", propertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Title", propertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Caption", propertyNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("DocumentName", propertyNames, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrivacyInvariant_Win32NativeDoesNotDeclareGetWindowText()
    {
        // Assert: Ensure no window title P/Invoke APIs exist in Win32Native
        var methods = typeof(Win32Native).GetMethods(BindingFlags.Public | BindingFlags.Static);
        var methodNames = methods.Select(m => m.Name).ToList();

        Assert.DoesNotContain("GetWindowText", methodNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetWindowTextLength", methodNames, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetWindowTextW", methodNames, StringComparer.OrdinalIgnoreCase);
    }
}
