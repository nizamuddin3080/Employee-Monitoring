using System.Reflection;
using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

/// <summary>
/// Milestone 7 Verification Suite:
/// Non-Chrome Application Usage Tracker (Duration Only, Zero Window Titles, Zero Overlap, Chrome Exclusion).
/// </summary>
public class NonChromeApplicationTrackingTests
{
    private static async Task<(SqliteConnectionFactory Factory, ActivityRepository ActRepo, ProfileRepository ProfRepo, string DbPath)> CreateTestDatabaseAsync()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), "LAT_NonChromeTest_" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(tempDb);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var actRepo = new ActivityRepository(factory);
        var profRepo = new ProfileRepository(factory);

        return (factory, actRepo, profRepo, tempDb);
    }

    [Fact]
    public async Task TASK038_NonChromeApplicationFocusDetection_SwitchesBetweenExternalAppsCorrectly()
    {
        var (factory, actRepo, profRepo, dbPath) = await CreateTestDatabaseAsync();
        try
        {
            var profileId = Guid.NewGuid().ToString("N");
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId, ProfileName = "TestProfile" });

            using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());

            var t0 = DateTime.UtcNow.AddMinutes(-10);

            // 1. Initially on Chrome
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(100), 1001, "chrome.exe", "Google Chrome", true, t0));

            var tabMsg = IpcMessage.Create(IpcMessage.EventTabNavigated, profileId, new
            {
                tab_id = 1,
                window_id = 1,
                raw_url = "https://example.com",
                page_title = "Example"
            });
            tabMsg.TimestampUtc = t0;
            await arbiter.OnIpcMessageReceivedAsync(profileId, tabMsg);

            Assert.True(arbiter.IsChromeForeground);
            Assert.NotNull(arbiter.CurrentUrlSession);
            Assert.Null(arbiter.CurrentAppSession);

            // 2. Switch to Visual Studio Code (Code.exe) at t0 + 60s
            var t1 = t0.AddSeconds(60);
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(200), 2002, "Code.exe", "Visual Studio Code", false, t1));

            Assert.False(arbiter.IsChromeForeground);
            Assert.Null(arbiter.CurrentUrlSession);
            Assert.NotNull(arbiter.CurrentAppSession);
            Assert.Equal("Code.exe", arbiter.CurrentAppSession.ProcessName);
            Assert.Equal("Visual Studio Code", arbiter.CurrentAppSession.ApplicationDisplayName);
            Assert.Equal(t1, arbiter.CurrentAppSession.StartTimeUtc);

            var codeSessionId = arbiter.CurrentAppSession.SessionId;

            // 3. Switch to Microsoft Excel (EXCEL.EXE) at t1 + 120s
            var t2 = t1.AddSeconds(120);
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(300), 3003, "EXCEL.EXE", "Microsoft Excel", false, t2));

            // Verify Code.exe session was closed and Excel session was opened
            Assert.NotNull(arbiter.CurrentAppSession);
            Assert.Equal("EXCEL.EXE", arbiter.CurrentAppSession.ProcessName);
            Assert.Equal("Microsoft Excel", arbiter.CurrentAppSession.ApplicationDisplayName);
            Assert.Equal(t2, arbiter.CurrentAppSession.StartTimeUtc);

            var codeDbSession = await actRepo.GetApplicationSessionByIdAsync(codeSessionId);
            Assert.NotNull(codeDbSession);
            Assert.True(codeDbSession.IsCompleted);
            Assert.Equal(120, codeDbSession.DurationSeconds);
            Assert.Equal(t2, codeDbSession.EndTimeUtc);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK039_StrictZeroWindowTitle_SchemaAndModelEnforcement()
    {
        var (factory, _, _, dbPath) = await CreateTestDatabaseAsync();
        try
        {
            // 1. Inspect SQLite schema via PRAGMA table_info
            using var conn = await factory.OpenConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(application_sessions);";

            var columns = new List<string>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1)); // column name
                }
            }

            // Assert: Absolutely no window title or document content columns exist
            Assert.Contains("session_id", columns);
            Assert.Contains("application_display_name", columns);
            Assert.Contains("process_name", columns);
            Assert.Contains("duration_seconds", columns);

            Assert.DoesNotContain("window_title", columns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("title", columns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("caption", columns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("document", columns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("document_name", columns, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("window_text", columns, StringComparer.OrdinalIgnoreCase);

            // 2. Inspect ApplicationSession C# class properties via reflection
            var modelProps = typeof(ApplicationSession).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name).ToList();

            Assert.DoesNotContain("WindowTitle", modelProps, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Title", modelProps, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Caption", modelProps, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("DocumentName", modelProps, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("Document", modelProps, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("WindowText", modelProps, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK040_NonChromeDurationCalculation_ZeroOverlapAndZeroDoubleCounting()
    {
        var (factory, actRepo, profRepo, dbPath) = await CreateTestDatabaseAsync();
        try
        {
            var profileId = Guid.NewGuid().ToString("N");
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId, ProfileName = "ZeroOverlapProfile" });

            using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());

            var t0 = new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);

            // Period 1: Chrome (10:00:00 -> 10:05:00, 300s)
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(100), 1001, "chrome.exe", "Google Chrome", true, t0));

            var tabMsg = IpcMessage.Create(IpcMessage.EventTabNavigated, profileId, new
            {
                tab_id = 1,
                window_id = 1,
                raw_url = "https://github.com",
                page_title = "GitHub"
            });
            tabMsg.TimestampUtc = t0;
            await arbiter.OnIpcMessageReceivedAsync(profileId, tabMsg);
            var chromeSessionId = arbiter.CurrentUrlSession!.SessionId;

            // Period 2: App A - Slack (10:05:00 -> 10:08:00, 180s)
            var t1 = t0.AddSeconds(300);
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(200), 2002, "slack.exe", "Slack", false, t1));
            var slackSessionId = arbiter.CurrentAppSession!.SessionId;

            // Period 3: App B - Visual Studio Code (10:08:00 -> 10:15:00, 420s)
            var t2 = t1.AddSeconds(180);
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(300), 3003, "Code.exe", "Visual Studio Code", false, t2));
            var codeSessionId = arbiter.CurrentAppSession!.SessionId;

            // Period 4: Back to Chrome (10:15:00 -> 10:20:00, 300s)
            var t3 = t2.AddSeconds(420);
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(100), 1001, "chrome.exe", "Google Chrome", true, t3));

            var t4 = t3.AddSeconds(300);
            // End tracking by switching away to trigger closure
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(400), 4004, "notepad.exe", "Notepad", false, t4));

            // Verify Durations
            var chromeSession = await actRepo.GetUrlSessionByIdAsync(chromeSessionId);
            var slackSession = await actRepo.GetApplicationSessionByIdAsync(slackSessionId);
            var codeSession = await actRepo.GetApplicationSessionByIdAsync(codeSessionId);

            Assert.NotNull(chromeSession);
            Assert.NotNull(slackSession);
            Assert.NotNull(codeSession);

            Assert.Equal(300, chromeSession.ActiveDurationSeconds);
            Assert.Equal(180, slackSession.DurationSeconds);
            Assert.Equal(420, codeSession.DurationSeconds);

            // Assert Non-Overlapping Boundaries:
            // chrome.EndTime == slack.StartTime
            Assert.Equal(chromeSession.EndTimeUtc, slackSession.StartTimeUtc);
            // slack.EndTime == code.StartTime
            Assert.Equal(slackSession.EndTimeUtc, codeSession.StartTimeUtc);
            // code.EndTime == t3 (back to Chrome)
            Assert.Equal(codeSession.EndTimeUtc, t3);

            // Zero double-counting invariant:
            var totalTrackedSeconds = chromeSession.ActiveDurationSeconds + slackSession.DurationSeconds + codeSession.DurationSeconds;
            var elapsedSeconds = (int)(t3 - t0).TotalSeconds;
            Assert.Equal(elapsedSeconds, totalTrackedSeconds);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public void TASK041_StrictPrivacyBoundaryEnforcement_ZeroInternalInspectionAPIs()
    {
        // 1. Audit Win32Native for forbidden inspection APIs
        var win32Methods = typeof(Win32Native).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(m => m.Name).ToList();

        // No window title reading
        Assert.DoesNotContain("GetWindowText", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetWindowTextA", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetWindowTextW", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetWindowTextLength", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("InternalGetWindowText", win32Methods, StringComparer.OrdinalIgnoreCase);

        // No clipboard scraping
        Assert.DoesNotContain("GetClipboardData", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpenClipboard", win32Methods, StringComparer.OrdinalIgnoreCase);

        // No keyboard hooks / keylogging
        Assert.DoesNotContain("GetAsyncKeyState", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetKeyboardState", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("SetWindowsHookEx", win32Methods, StringComparer.OrdinalIgnoreCase);

        // No screen scraping / GDI BitBlt
        Assert.DoesNotContain("BitBlt", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateCompatibleDC", win32Methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateCompatibleBitmap", win32Methods, StringComparer.OrdinalIgnoreCase);

        // 2. Audit ProcessInfoResolver for permitted APIs only
        var resolverFields = typeof(ProcessInfoResolver).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Select(f => f.Name).ToList();

        Assert.DoesNotContain("clipboard", resolverFields, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("keystroke", resolverFields, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("title", resolverFields, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TASK042_ChromeExclusionFromApplicationUsageReport_GuaranteesZeroChromeInAppSessions()
    {
        var (factory, actRepo, _, dbPath) = await CreateTestDatabaseAsync();
        try
        {
            // 1. Attempting to directly open an application session for chrome.exe must throw
            var chromeSession = new ApplicationSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ProcessName = "chrome.exe",
                ApplicationDisplayName = "Google Chrome",
                StartTimeUtc = DateTime.UtcNow
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => actRepo.OpenApplicationSessionAsync(chromeSession));

            // Also test uppercase CHROME.EXE
            chromeSession.ProcessName = "CHROME.EXE";
            await Assert.ThrowsAsync<InvalidOperationException>(() => actRepo.OpenApplicationSessionAsync(chromeSession));

            // 2. Arbiter must never create an application session for chrome.exe
            using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());

            var t0 = DateTime.UtcNow;
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(100), 1234, "chrome.exe", "Google Chrome", true, t0));

            Assert.Null(arbiter.CurrentAppSession);

            // 3. Insert a legitimate non-Chrome application session (e.g. Notepad)
            var notepadSession = new ApplicationSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ProcessName = "notepad.exe",
                ApplicationDisplayName = "Notepad",
                StartTimeUtc = t0.AddMinutes(-5),
                EndTimeUtc = t0,
                DurationSeconds = 300,
                IsCompleted = true,
                SessionStatus = "COMPLETED"
            };
            await actRepo.OpenApplicationSessionAsync(notepadSession);

            // 4. Query application sessions range
            var appSessions = await actRepo.GetApplicationSessionsInRangeAsync(t0.AddHours(-1), t0.AddHours(1));

            Assert.Single(appSessions);
            Assert.Equal("notepad.exe", appSessions[0].ProcessName);

            // Assert that chrome.exe is 100% absent
            Assert.DoesNotContain(appSessions, s => s.ProcessName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
        }
    }
}
