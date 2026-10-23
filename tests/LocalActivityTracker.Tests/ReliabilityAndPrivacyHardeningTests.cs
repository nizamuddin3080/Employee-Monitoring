using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using LocalActivityTracker.Coordinator.Api;
using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Profiles;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Screenshots;
using LocalActivityTracker.Core.Storage;
using Xunit;

namespace LocalActivityTracker.Tests;

public class ReliabilityAndPrivacyHardeningTests : IDisposable
{
    private readonly string _testDir;
    private readonly StoragePathProvider _storage;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ActivityRepository _activityRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly DatabaseMaintenanceService _maintenanceService;
    private readonly UrlSanitizer _urlSanitizer;

    public ReliabilityAndPrivacyHardeningTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "LAT_ReliabilityTests_" + Guid.NewGuid().ToString("N"));
        _storage = new StoragePathProvider(_testDir);
        _storage.EnsureDirectoriesExist();

        _connectionFactory = new SqliteConnectionFactory(_storage);
        var migrationRunner = new MigrationRunner(_connectionFactory);
        migrationRunner.ApplyMigrationsAsync().GetAwaiter().GetResult();

        _activityRepository = new ActivityRepository(_connectionFactory);
        _profileRepository = new ProfileRepository(_connectionFactory);
        _settingsRepository = new SettingsRepository(_connectionFactory);
        _maintenanceService = new DatabaseMaintenanceService(_connectionFactory, _storage);
        _urlSanitizer = new UrlSanitizer();
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

    [Fact]
    public async Task TASK064_SessionLockAndSuspend_ClosesActiveSessionsAndHaltsAccrual()
    {
        var arbiter = new ActivityArbiter(_activityRepository, _urlSanitizer);

        var profileId = "profile-lock-001";
        await _profileRepository.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "Lock Test Profile",
            ChromeProfileDir = "Profile 1",
            IsEnrolled = true
        });

        // 1. Start active URL session via arbiter
        var now = DateTime.UtcNow;
        var foreground = new Core.Win32.ForegroundChangedEventArgs(
            (IntPtr)0x1234,
            1234,
            "chrome.exe",
            "Google Chrome",
            true,
            now);
        await arbiter.OnForegroundWindowChangedAsync(foreground);

        var tabMsg = IpcMessage.Create(
            IpcMessage.EventTabNavigated,
            profileId,
            new
            {
                tab_id = 100,
                window_id = 1,
                raw_url = "https://github.com/active-repo",
                page_title = "Active Repository"
            });
        tabMsg.TimestampUtc = now;
        await arbiter.OnIpcMessageReceivedAsync(profileId, tabMsg);

        Assert.NotNull(arbiter.CurrentUrlSession);
        Assert.False(arbiter.IsTrackingPaused);

        // 2. Trigger Windows Session Lock (WTS_SESSION_LOCK)
        await arbiter.OnSessionLockedAsync();

        Assert.True(arbiter.IsTrackingPaused);
        Assert.Null(arbiter.CurrentUrlSession);
        Assert.Null(arbiter.CurrentAppSession);

        // 3. Trigger Unlock
        await arbiter.OnSessionUnlockedAsync();
        Assert.False(arbiter.IsTrackingPaused);
    }

    [Fact]
    public async Task TASK065_CrashRecovery_ReconcilesDanglingSessionsWithLastHeartbeat()
    {
        var profileId = "crash-profile-001";
        await _profileRepository.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "Crash Profile",
            ChromeProfileDir = "Default",
            IsEnrolled = true
        });

        var tStart = DateTime.UtcNow.AddMinutes(-30);
        var tHeartbeat = tStart.AddMinutes(12); // Last heartbeat was 12 min in

        // Unclosed URL Session (simulating abrupt kill)
        var urlSessId = Guid.NewGuid().ToString("N");
        await _activityRepository.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = urlSessId,
            ProfileId = profileId,
            ChromeTabId = 42,
            Url = "https://crash.test.org",
            SanitizedUrl = "https://crash.test.org",
            Domain = "crash.test.org",
            PageTitle = "Crash Test Page",
            StartTimeUtc = tStart,
            LastHeartbeatUtc = tHeartbeat,
            ActiveDurationSeconds = 0,
            IsCompleted = false,
            SessionStatus = "ACTIVE"
        });

        // Unclosed Application Session
        var appSessId = Guid.NewGuid().ToString("N");
        await _activityRepository.OpenApplicationSessionAsync(new ApplicationSession
        {
            SessionId = appSessId,
            ApplicationDisplayName = "Notepad",
            ProcessName = "notepad.exe",
            StartTimeUtc = tStart,
            LastHeartbeatUtc = tHeartbeat,
            DurationSeconds = 0,
            IsCompleted = false,
            SessionStatus = "ACTIVE"
        });

        // Execute Crash Recovery
        var recovered = await _activityRepository.RecoverDanglingSessionsAsync();
        Assert.True(recovered >= 2);

        // Assert URL session reconciled
        var urlRecovered = await _activityRepository.GetUrlSessionByIdAsync(urlSessId);
        Assert.NotNull(urlRecovered);
        Assert.True(urlRecovered.IsCompleted);
        Assert.Equal("RECOVERED_AFTER_CRASH", urlRecovered.SessionStatus);
        Assert.Equal(tHeartbeat, urlRecovered.EndTimeUtc);
        Assert.Equal(720, urlRecovered.ActiveDurationSeconds); // 12 min * 60s = 720s

        // Assert Application session reconciled
        var appRecovered = await _activityRepository.GetApplicationSessionByIdAsync(appSessId);
        Assert.NotNull(appRecovered);
        Assert.True(appRecovered.IsCompleted);
        Assert.Equal("RECOVERED_AFTER_CRASH", appRecovered.SessionStatus);
        Assert.Equal(tHeartbeat, appRecovered.EndTimeUtc);
        Assert.Equal(720, appRecovered.DurationSeconds);
    }

    [Fact]
    public async Task TASK066_DatabaseBackupAndSafePurge_OperatesCorrectly()
    {
        var profileId = "maintenance-profile-001";
        await _profileRepository.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "Maintenance Profile",
            ChromeProfileDir = "Profile 1",
            IsEnrolled = true
        });

        // 1. Test Backup Creation (VACUUM INTO)
        var backupPath = await _maintenanceService.CreateBackupAsync();
        Assert.True(File.Exists(backupPath));
        Assert.True(new FileInfo(backupPath).Length > 0);

        // 2. Seed Old and New Screenshot Records
        var oldPng = Path.Combine(_storage.ScreenshotsDirectory, "old_test.png");
        var newPng = Path.Combine(_storage.ScreenshotsDirectory, "new_test.png");
        File.WriteAllBytes(oldPng, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(newPng, new byte[] { 4, 5, 6 });

        var oldTime = DateTime.UtcNow.AddDays(-45);
        var newTime = DateTime.UtcNow.AddDays(-5);

        await _activityRepository.InsertScreenshotMetadataAsync(new ScreenshotMetadata
        {
            ScreenshotId = "sc-old",
            ProfileId = profileId,
            Url = "https://old.com",
            FilePath = oldPng,
            FileSizeBytes = 3,
            CaptureStatus = "SUCCESS",
            CaptureTimeUtc = oldTime
        });

        await _activityRepository.InsertScreenshotMetadataAsync(new ScreenshotMetadata
        {
            ScreenshotId = "sc-new",
            ProfileId = profileId,
            Url = "https://new.com",
            FilePath = newPng,
            FileSizeBytes = 3,
            CaptureStatus = "SUCCESS",
            CaptureTimeUtc = newTime
        });

        // 3. Purge older than 30 days
        var deletedRows = await _maintenanceService.PurgeOldRecordsAsync(30);
        Assert.True(deletedRows >= 1);

        // Verify physical file was unlinked for old screenshot
        Assert.False(File.Exists(oldPng));
        Assert.True(File.Exists(newPng));

        // 4. Purge All Data
        await _maintenanceService.PurgeAllDataAsync();
        Assert.False(File.Exists(newPng));

        var remainingScreenshots = await _activityRepository.GetScreenshotsInRangeAsync(null, DateTime.UtcNow.AddYears(-1), DateTime.UtcNow.AddYears(1));
        Assert.Empty(remainingScreenshots);
    }

    [Theory]
    [InlineData("https://app.com/login?token=abc123secret", "https://app.com/login")]
    [InlineData("https://app.com/oauth?access_token=bearer_xyz&client_id=123", "https://app.com/oauth?client_id=123")]
    [InlineData("https://app.com/api?api_key=key_456&search=docs", "https://app.com/api?search=docs")]
    [InlineData("https://app.com/auth?password=supersecret&user=john", "https://app.com/auth?user=john")]
    [InlineData("https://app.com/session?session_id=sess_789&view=grid", "https://app.com/session?view=grid")]
    [InlineData("https://app.com/callback?code=auth_code_999&state=state_value", "https://app.com/callback")]
    [InlineData("https://app.com/jwt?jwt=eyJhbGciOi...&page=1", "https://app.com/jwt?page=1")]
    [InlineData("https://app.com/aws?x-amz-signature=sig123&action=list", "https://app.com/aws?action=list")]
    [InlineData("https://app.com/saml?samlresponse=base64_data", "https://app.com/saml")]
    [InlineData("https://app.com/checkout?stripe_token=tok_visa&amount=500", "https://app.com/checkout?amount=500")]
    [InlineData("https://app.com/verify?pin=1234&ssn=000-00-0000", "https://app.com/verify")]
    [InlineData("https://app.com/secure?client_secret=secret123&private_key=key789", "https://app.com/secure")]
    [InlineData("https://admin:mypassword@example.com/dashboard", "https://example.com/dashboard")]
    [InlineData("https://app.com/redirect#access_token=token123&expires_in=3600", "https://app.com/redirect")]
    [InlineData("chrome://settings/passwords", "chrome://settings/passwords")]
    public void TASK067_UrlSanitizer_ExhaustiveAuthParameterRedaction_SanitizesCorrectly(string rawUrl, string expectedSanitized)
    {
        var result = _urlSanitizer.Sanitize(rawUrl);
        Assert.Equal(expectedSanitized, result.SanitizedUrl);
    }

    [Fact]
    public void TASK068_FilesystemAclHardening_ConfiguresStrictUserAcl()
    {
        var targetDir = Path.Combine(_testDir, "AclTestDir");
        Directory.CreateDirectory(targetDir);

        _storage.EnforceStrictUserAcl(targetDir);

        if (OperatingSystem.IsWindows())
        {
            var dirInfo = new DirectoryInfo(targetDir);
            var acl = dirInfo.GetAccessControl();
            Assert.True(acl.AreAccessRulesProtected); // Inheritance stripped
        }
    }

    private sealed class NoopIpcServer : INamedPipeIpcServer
    {
        public event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected { add { } remove { } }
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected { add { } remove { } }

        public bool IsRunning => true;
        public string PipeName => "NoopPipe";
        public IReadOnlyList<string> ConnectedProfileIds => Array.Empty<string>();

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<bool> SendMessageAsync(string profileId, IpcMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    [Fact]
    public async Task TASK066_ApiEndpoints_BackupAndPurge_RespondOverHttp()
    {
        var testPort = 48129;
        var tokenProvider = new LocalCapabilityTokenProvider(_storage);
        var arbiter = new ActivityArbiter(_activityRepository);
        var scheduler = new ScreenshotScheduler(_settingsRepository, arbiter, new NoopIpcServer(), _activityRepository, _storage);

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
            databaseMaintenanceService: _maintenanceService);

        await host.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{testPort}") };
        var token = tokenProvider.GetOrCreateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-LAT-Requested-With", "LocalActivityTracker");

        // 1. POST /api/data/backup
        var backupRes = await client.PostAsync("/api/data/backup", null);
        Assert.Equal(HttpStatusCode.OK, backupRes.StatusCode);
        var backupJson = await backupRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SUCCESS", backupJson.GetProperty("status").GetString());
        Assert.NotEmpty(backupJson.GetProperty("backup_path").GetString()!);

        // 2. POST /api/data/purge
        var purgeRes = await client.PostAsJsonAsync("/api/data/purge", new { older_than_days = 30 });
        Assert.Equal(HttpStatusCode.OK, purgeRes.StatusCode);
        var purgeJson = await purgeRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SUCCESS", purgeJson.GetProperty("status").GetString());
        Assert.False(purgeJson.GetProperty("purged_all").GetBoolean());
    }
}
