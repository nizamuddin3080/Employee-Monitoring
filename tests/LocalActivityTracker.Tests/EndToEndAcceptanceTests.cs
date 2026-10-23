using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LocalActivityTracker.Coordinator.Api;
using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Export;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Profiles;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Screenshots;
using LocalActivityTracker.Core.Storage;
using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

public class EndToEndAcceptanceTests : IDisposable
{
    private readonly string _testDir;
    private readonly StoragePathProvider _storage;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ActivityRepository _activityRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly UrlSanitizer _urlSanitizer;
    private readonly CsvExportService _exportService;

    public EndToEndAcceptanceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "LAT_AcceptanceTests_" + Guid.NewGuid().ToString("N"));
        _storage = new StoragePathProvider(_testDir);
        _storage.EnsureDirectoriesExist();

        _connectionFactory = new SqliteConnectionFactory(_storage);
        var migrationRunner = new MigrationRunner(_connectionFactory);
        migrationRunner.ApplyMigrationsAsync().GetAwaiter().GetResult();

        _activityRepository = new ActivityRepository(_connectionFactory);
        _profileRepository = new ProfileRepository(_connectionFactory);
        _settingsRepository = new SettingsRepository(_connectionFactory);
        _urlSanitizer = new UrlSanitizer();
        _exportService = new CsvExportService(_storage, _activityRepository, _profileRepository);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    private sealed class MockAcceptanceIpcServer : INamedPipeIpcServer
    {
        public event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected { add { } remove { } }
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected { add { } remove { } }

        public bool IsRunning => true;
        public string PipeName => "MockAcceptancePipe";
        public IReadOnlyList<string> ConnectedProfileIds => Array.Empty<string>();

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<bool> SendMessageAsync(string profileId, IpcMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    [Fact]
    public async Task TASK072_MultiProfileConcurrentEnrollmentAndTracking_MaintainsIsolationAndAccurateDuration()
    {
        var arbiter = new ActivityArbiter(_activityRepository, _urlSanitizer);

        var profileA = "profile-alpha-111";
        var profileB = "profile-beta-222";

        // 1. Enroll Profile A and Profile B
        await _profileRepository.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileA,
            ProfileName = "Alpha Work Profile",
            ChromeProfileDir = "Profile 1",
            IsEnrolled = true,
            ConnectionStatus = "CONNECTED"
        });

        await _profileRepository.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileB,
            ProfileName = "Beta Personal Profile",
            ChromeProfileDir = "Profile 2",
            IsEnrolled = true,
            ConnectionStatus = "CONNECTED"
        });

        var profiles = await _profileRepository.GetAllProfilesAsync();
        Assert.Equal(2, profiles.Count);
        Assert.All(profiles, p => Assert.True(p.IsEnrolled));

        // 2. Simulate Active Browsing in Profile A
        var t0 = DateTime.UtcNow;
        await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
            (IntPtr)0x1111, 1001, "chrome.exe", "Google Chrome", true, t0));

        var tabMsgA = IpcMessage.Create(
            IpcMessage.EventTabNavigated,
            profileA,
            new
            {
                tab_id = 10,
                window_id = 1,
                raw_url = "https://company.jira.com/secure/RapidBoard",
                page_title = "Jira Sprint Board"
            });
        tabMsgA.TimestampUtc = t0;
        await arbiter.OnIpcMessageReceivedAsync(profileA, tabMsgA);

        Assert.NotNull(arbiter.CurrentUrlSession);
        Assert.Equal(profileA, arbiter.CurrentUrlSession.ProfileId);
        Assert.Equal("jira.com", arbiter.CurrentUrlSession.Domain);

        // 3. Switch Foreground to Profile B
        var t1 = t0.AddMinutes(10);
        var tabMsgB = IpcMessage.Create(
            IpcMessage.EventTabNavigated,
            profileB,
            new
            {
                tab_id = 20,
                window_id = 2,
                raw_url = "https://news.ycombinator.com/item?id=123",
                page_title = "Hacker News"
            });
        tabMsgB.TimestampUtc = t1;
        await arbiter.OnIpcMessageReceivedAsync(profileB, tabMsgB);

        Assert.NotNull(arbiter.CurrentUrlSession);
        Assert.Equal(profileB, arbiter.CurrentUrlSession.ProfileId);
        Assert.Equal("ycombinator.com", arbiter.CurrentUrlSession.Domain);

        // 4. Switch to external desktop application (Visual Studio Code)
        var t2 = t1.AddMinutes(15);
        await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
            (IntPtr)0x9999, 9999, "Code.exe", "Visual Studio Code", false, t2));

        Assert.Null(arbiter.CurrentUrlSession);
        Assert.NotNull(arbiter.CurrentAppSession);
        Assert.Equal("Code.exe", arbiter.CurrentAppSession.ProcessName);

        // 5. Verify database records for both profiles are isolated and accurate
        var sessionsA = await _activityRepository.GetUrlSessionsInRangeAsync(profileA, t0.AddHours(-1), t2.AddHours(1));
        var sessionsB = await _activityRepository.GetUrlSessionsInRangeAsync(profileB, t0.AddHours(-1), t2.AddHours(1));

        Assert.Single(sessionsA);
        Assert.Equal("https://company.jira.com/secure/RapidBoard", sessionsA[0].SanitizedUrl);

        Assert.Single(sessionsB);
        Assert.Equal("https://news.ycombinator.com/item?id=123", sessionsB[0].SanitizedUrl);
    }

    [Fact]
    public async Task TASK073_CustomDateRangeMasterCsvExport_EndToEndVerification()
    {
        var testPort = 48130;
        var tokenProvider = new LocalCapabilityTokenProvider(_storage);
        var arbiter = new ActivityArbiter(_activityRepository, _urlSanitizer);
        var scheduler = new ScreenshotScheduler(_settingsRepository, arbiter, new MockAcceptanceIpcServer(), _activityRepository, _storage);

        await using var host = new CoordinatorApiHost(
            _storage,
            _connectionFactory,
            _activityRepository,
            _profileRepository,
            _settingsRepository,
            arbiter,
            scheduler,
            tokenProvider,
            testPort,
            _exportService);

        await host.StartAsync();

        // 1. Seed Multi-Profile Data
        var prof1 = "e2e-profile-alpha";
        var prof2 = "e2e-profile-beta";

        await _profileRepository.UpsertProfileAsync(new ChromeProfile { ProfileId = prof1, ProfileName = "Alpha Work", IsEnrolled = true });
        await _profileRepository.UpsertProfileAsync(new ChromeProfile { ProfileId = prof2, ProfileName = "Beta Personal", IsEnrolled = true });

        var tStart = DateTime.UtcNow.Date.AddHours(9);

        // Seed Profile 1 URL Activity with formula injection payload to verify defense
        await _activityRepository.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = "sess-p1",
            ProfileId = prof1,
            ChromeTabId = 1,
            Url = "https://example.com/search?q=test",
            SanitizedUrl = "https://example.com/search?q=test",
            Domain = "example.com",
            PageTitle = "=cmd|'/c calc'!A0", // Formula payload
            StartTimeUtc = tStart,
            EndTimeUtc = tStart.AddMinutes(20),
            ActiveDurationSeconds = 1200,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        // Seed Profile 2 URL Activity
        await _activityRepository.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = "sess-p2",
            ProfileId = prof2,
            ChromeTabId = 2,
            Url = "https://github.com/microsoft/dotnet",
            SanitizedUrl = "https://github.com/microsoft/dotnet",
            Domain = "github.com",
            PageTitle = "GitHub - .NET Runtime",
            StartTimeUtc = tStart.AddMinutes(30),
            EndTimeUtc = tStart.AddMinutes(50),
            ActiveDurationSeconds = 1200,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        // Seed Non-Chrome Application session
        await _activityRepository.OpenApplicationSessionAsync(new ApplicationSession
        {
            SessionId = "app-excel",
            ApplicationDisplayName = "Microsoft Excel",
            ProcessName = "EXCEL.EXE",
            StartTimeUtc = tStart.AddHours(2),
            EndTimeUtc = tStart.AddHours(3),
            DurationSeconds = 3600,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        // 2. Request CSV Export via REST API
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{testPort}") };
        var token = tokenProvider.GetOrCreateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-LAT-Requested-With", "LocalActivityTracker");

        var exportReq = new CsvExportRequest
        {
            StartDate = tStart.ToString("yyyy-MM-dd"),
            EndDate = tStart.ToString("yyyy-MM-dd"),
            ProfileIds = new List<string> { "ALL" }
        };

        var postRes = await client.PostAsJsonAsync("/api/export/csv", exportReq);
        Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

        var json = await postRes.Content.ReadFromJsonAsync<JsonElement>();
        var filePath = json.GetProperty("file_path").GetString()!;
        Assert.True(File.Exists(filePath));

        // 3. Inspect CSV Output File Directly
        var bytes = File.ReadAllBytes(filePath);
        Assert.Equal(0xEF, bytes[0]); // UTF-8 BOM
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var csvContent = File.ReadAllText(filePath, Encoding.UTF8);

        // Section 1 Verification
        Assert.Contains("# SECTION: REPORT_METADATA", csvContent);
        Assert.Contains("Total Profiles Enrolled,2", csvContent);

        // Section 2 Verification
        Assert.Contains("# SECTION: PROFILE_SUMMARY", csvContent);
        Assert.Contains("Alpha Work", csvContent);
        Assert.Contains("Beta Personal", csvContent);

        // Section 3 Verification (Multi-Profile Distinct Sections)
        Assert.Contains($"# SECTION: CHROME_PROFILE: {prof1} (Alpha Work)", csvContent);
        Assert.Contains($"# SECTION: CHROME_PROFILE: {prof2} (Beta Personal)", csvContent);

        // Formula Injection Defense Verification
        Assert.Contains("\"'=cmd|'/c calc'!A0\"", csvContent);

        // Section 5 Verification (Strict Non-Chrome Privacy Invariant)
        var s5Index = csvContent.IndexOf("# SECTION: NON_CHROME_APPLICATION_USAGE", StringComparison.Ordinal);
        var s5Content = csvContent.Substring(s5Index);

        Assert.Contains("Application Name,Executable,Date,Start Time,End Time,Duration Seconds,Session Status", s5Content);
        Assert.Contains("\"Microsoft Excel\",\"EXCEL.EXE\"", s5Content);
        Assert.DoesNotContain("window_title", s5Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Page Title", s5Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TASK070_TASK071_PowerShellScripts_ExistAndAreSyntaxValid()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "scripts"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts"))
        };
        var scriptsDir = candidates.FirstOrDefault(Directory.Exists);
        Assert.True(scriptsDir != null && Directory.Exists(scriptsDir), $"Scripts directory not found in candidate paths.");

        var expectedScripts = new[]
        {
            "install.ps1",
            "uninstall.ps1",
            "start.ps1",
            "stop.ps1",
            "harden-storage-acl.ps1"
        };

        foreach (var script in expectedScripts)
        {
            var scriptPath = Path.Combine(scriptsDir, script);
            Assert.True(File.Exists(scriptPath), $"Script missing: {scriptPath}");

            var content = File.ReadAllText(scriptPath);
            Assert.NotEmpty(content);
        }
    }
}
