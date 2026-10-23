using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Resolves process executable names and friendly application display names without reading window titles or document contents.
/// </summary>
public sealed class ProcessInfoResolver : IProcessInfoResolver
{
    private readonly ConcurrentDictionary<string, (string ProcessName, string DisplayName)> _cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> KnownDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "chrome.exe", "Google Chrome" },
        { "code.exe", "Visual Studio Code" },
        { "notepad.exe", "Notepad" },
        { "explorer.exe", "Windows Explorer" },
        { "msedge.exe", "Microsoft Edge" },
        { "taskmgr.exe", "Task Manager" },
        { "cmd.exe", "Command Prompt" },
        { "powershell.exe", "Windows PowerShell" },
        { "pwsh.exe", "PowerShell" },
        { "excel.exe", "Microsoft Excel" },
        { "winword.exe", "Microsoft Word" },
        { "powerpnt.exe", "Microsoft PowerPoint" },
        { "outlook.exe", "Microsoft Outlook" },
        { "slack.exe", "Slack" },
        { "spotify.exe", "Spotify" },
        { "calc.exe", "Calculator" },
        { "calculatorapp.exe", "Calculator" },
        { "systemsettings.exe", "Settings" },
        { "windowsterminal.exe", "Windows Terminal" }
    };

    public ProcessInfo? Resolve(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        Win32Native.GetWindowThreadProcessId(hWnd, out var processId);
        if (processId == 0)
        {
            return null;
        }

        return ResolveByProcessId(processId);
    }

    public ProcessInfo? ResolveByProcessId(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        if (processId == 4)
        {
            return new ProcessInfo(4, "System", "System", null);
        }

        string? fullPath = null;
        var hProcess = Win32Native.OpenProcess(Win32Native.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                var size = (uint)sb.Capacity;
                if (Win32Native.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                {
                    fullPath = sb.ToString();
                }
            }
            finally
            {
                Win32Native.CloseHandle(hProcess);
            }
        }

        if (string.IsNullOrEmpty(fullPath))
        {
            try
            {
                using var proc = Process.GetProcessById((int)processId);
                try
                {
                    fullPath = proc.MainModule?.FileName;
                }
                catch
                {
                    // Access denied on MainModule for elevated processes
                }

                if (string.IsNullOrEmpty(fullPath))
                {
                    var rawName = proc.ProcessName;
                    var procName = rawName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? rawName : rawName + ".exe";
                    var displayName = ResolveDisplayNameFromKnown(procName) ?? rawName;
                    return new ProcessInfo(processId, procName, displayName, null);
                }
            }
            catch
            {
                return null;
            }
        }

        if (_cache.TryGetValue(fullPath, out var cached))
        {
            return new ProcessInfo(processId, cached.ProcessName, cached.DisplayName, fullPath);
        }

        var processFileName = Path.GetFileName(fullPath);
        var resolvedDisplayName = ResolveDisplayName(fullPath, processFileName);

        _cache.TryAdd(fullPath, (processFileName, resolvedDisplayName));
        return new ProcessInfo(processId, processFileName, resolvedDisplayName, fullPath);
    }

    public bool IsChromeProcess(string processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        return string.Equals(processName, "chrome.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDisplayName(string fullPath, string processName)
    {
        var known = ResolveDisplayNameFromKnown(processName);
        if (!string.IsNullOrEmpty(known))
        {
            return known;
        }

        try
        {
            if (File.Exists(fullPath))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(fullPath);
                if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
                {
                    return versionInfo.FileDescription.Trim();
                }
                if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
                {
                    return versionInfo.ProductName.Trim();
                }
            }
        }
        catch
        {
            // Ignore file access exceptions
        }

        var withoutExt = Path.GetFileNameWithoutExtension(processName);
        return string.IsNullOrWhiteSpace(withoutExt) ? processName : withoutExt;
    }

    private static string? ResolveDisplayNameFromKnown(string processName)
    {
        return KnownDisplayNames.TryGetValue(processName, out var name) ? name : null;
    }
}
