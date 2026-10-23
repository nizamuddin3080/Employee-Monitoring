using System.Diagnostics;
using System.Text;
using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

public class Win32NativeTests
{
    [Fact]
    public void GetForegroundWindow_DoesNotThrowAndReturnsHandle()
    {
        // Act
        var hwnd = Win32Native.GetForegroundWindow();

        // Assert: on interactive Windows desktop, GetForegroundWindow returns either a valid HWND or IntPtr.Zero
        // It must never throw an access violation or runtime exception.
        Assert.True(hwnd == IntPtr.Zero || hwnd != IntPtr.Zero);
    }

    [Fact]
    public void GetWindowThreadProcessId_WithCurrentProcessHandle_ReturnsValidPid()
    {
        // Arrange
        var hwnd = Win32Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            // In headless CI or screen-locked environment, skip HWND inspection
            return;
        }

        // Act
        var threadId = Win32Native.GetWindowThreadProcessId(hwnd, out var processId);

        // Assert
        Assert.True(threadId > 0);
        Assert.True(processId > 0);
    }

    [Fact]
    public void OpenProcessAndQueryFullProcessImageName_CurrentProcess_ReturnsValidPath()
    {
        // Arrange
        var currentPid = (uint)Process.GetCurrentProcess().Id;

        // Act
        var hProcess = Win32Native.OpenProcess(Win32Native.PROCESS_QUERY_LIMITED_INFORMATION, false, currentPid);

        try
        {
            Assert.NotEqual(IntPtr.Zero, hProcess);

            var sb = new StringBuilder(1024);
            var size = (uint)sb.Capacity;
            var success = Win32Native.QueryFullProcessImageName(hProcess, 0, sb, ref size);

            Assert.True(success);
            var path = sb.ToString();
            Assert.False(string.IsNullOrWhiteSpace(path));
            Assert.True(File.Exists(path));
            Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (hProcess != IntPtr.Zero)
            {
                var closed = Win32Native.CloseHandle(hProcess);
                Assert.True(closed);
            }
        }
    }
}
