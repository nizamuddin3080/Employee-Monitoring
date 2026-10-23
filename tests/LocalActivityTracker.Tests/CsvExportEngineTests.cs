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
using LocalActivityTracker.Core.Profiles;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Screenshots;
using LocalActivityTracker.Core.Storage;
using Xunit;

namespace LocalActivityTracker.Tests;

public class CsvExportEngineTests : IDisposable
{
    private readonly string _testDir;
    private readonly StoragePathProvider _storage;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ActivityRepository _activityRepository;
    private readonly ProfileRepository _profileRepository;
    private readonly SettingsRepository _settingsRepository;
    private readonly CsvExportService _exportService;

    public CsvExportEngineTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "LAT_CsvExportTests_" + Guid.NewGuid().ToString("N"));
        _storage = new StoragePathProvider(_testDir);
        _storage.EnsureDirectoriesExist();

        _connectionFactory = new SqliteConnectionFactory(_storage);
        var migrationRunner = new MigrationRunner(_connectionFactory);
        migrationRunner.ApplyMigrationsAsync().GetAwaiter().GetResult();

        _activityRepository = new ActivityRepository(_connectionFactory);
        _profileRepository = new ProfileRepository(_connectionFactory);
        _settingsRepository = new SettingsRepository(_connectionFactory);
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

    [Theory]
    [InlineData("=cmd|'/c calc'!A0", "\"'=cmd|'/c calc'!A0\"")]
    [InlineData("+12345", "\"'+12345\"")]
    [InlineData("-500", "\"'-500\"")]
    [InlineData("@SUM(A1:B2)", "\"'@SUM(A1:B2)\"")]
    [InlineData("\tTabPayload", "\"'\tTabPayload\"")]
    [InlineData("\rReturnPayload", "\"'\rReturnPayload\"")]
    [InlineData("Normal Title", "\"Normal Title\"")]
    [InlineData("Title with \"Quotes\"", "\"Title with \"\"Quotes\"\"\"")]
    [InlineData("Title, with comma", "\"Title, with comma\"")]
    public void CsvFormatter_FormulaInjectionAndEscaping_BehavesCorrectly(string input, string expected)
    {
        var result = CsvFormatter.EscapeAndSanitize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CsvFormatter_FormatValue_HandlesScalarsCorrectly()
    {
        Assert.Equal("630", CsvFormatter.FormatValue(630));
        Assert.Equal("0", CsvFormatter.FormatValue(0));
        Assert.Equal("\"Normal\"", CsvFormatter.FormatValue("Normal"));
        Assert.Equal("\"'=1+1\"", CsvFormatter.FormatValue("=1+1"));
    }

    [Fact]
    public async Task ExportToCsvAsync_InvalidDateRange_ThrowsArgumentException()
    {
        var req = new CsvExportRequest
        {
            StartDate = "2026-10-25",
            EndDate = "2026-10-20"
        };

        await Assert.ThrowsAsync<ArgumentException>(() => _exportService.ExportToCsvAsync(req));
    }

    [Fact]
    public async Task ExportToCsvAsync_GeneratesUtf8BomAndAllFiveSections()
    {
        // 1. Seed Profile
        var profileId = "test-profile-001";
        await _profileRepository.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "Work Profile",
            ChromeProfileDir = "Profile 1",
            IsEnrolled = true
        });

        // 2. Seed URL session
        var baseTime = DateTime.UtcNow.Date.AddHours(10);
        await _activityRepository.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = "url-sess-001",
            ProfileId = profileId,
            ChromeTabId = 42,
            Url = "https://github.com/project",
            SanitizedUrl = "https://github.com/project",
            Domain = "github.com",
            PageTitle = "GitHub: Where code lives",
            StartTimeUtc = baseTime,
            EndTimeUtc = baseTime.AddMinutes(15),
            ActiveDurationSeconds = 900,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        // 3. Seed Screenshot
        var screenshotPath = Path.Combine(_storage.ScreenshotsDirectory, "test.png");
        File.WriteAllBytes(screenshotPath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        await _activityRepository.InsertScreenshotMetadataAsync(new ScreenshotMetadata
        {
            ScreenshotId = "sc-001",
            ProfileId = profileId,
            Url = "https://github.com/project",
            FilePath = screenshotPath,
            FileSizeBytes = 4,
            CaptureStatus = "SUCCESS",
            CaptureTimeUtc = baseTime.AddMinutes(5)
        });

        // 4. Seed Non-Chrome Application session
        await _activityRepository.OpenApplicationSessionAsync(new ApplicationSession
        {
            SessionId = "app-sess-001",
            ApplicationDisplayName = "Visual Studio Code",
            ProcessName = "Code.exe",
            StartTimeUtc = baseTime.AddHours(1),
            EndTimeUtc = baseTime.AddHours(2),
            DurationSeconds = 3600,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        // 5. Execute Export
        var req = new CsvExportRequest
        {
            StartDate = baseTime.ToString("yyyy-MM-dd"),
            EndDate = baseTime.ToString("yyyy-MM-dd"),
            ProfileIds = new List<string> { "ALL" }
        };

        var result = await _exportService.ExportToCsvAsync(req);

        Assert.Equal("SUCCESS", result.Status);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(1, result.RecordCounts.ProfilesIncluded);
        Assert.Equal(1, result.RecordCounts.UrlSessionsExported);
        Assert.Equal(1, result.RecordCounts.ScreenshotRecordsExported);
        Assert.Equal(1, result.RecordCounts.ApplicationSessionsExported);

        // Verify UTF-8 BOM (0xEF, 0xBB, 0xBF)
        var rawBytes = File.ReadAllBytes(result.FilePath);
        Assert.True(rawBytes.Length >= 3);
        Assert.Equal(0xEF, rawBytes[0]);
        Assert.Equal(0xBB, rawBytes[1]);
        Assert.Equal(0xBF, rawBytes[2]);

        // Read string content
        var csvText = File.ReadAllText(result.FilePath, Encoding.UTF8);

        // Verify all 5 section headers
        Assert.Contains("# SECTION: REPORT_METADATA", csvText);
        Assert.Contains("# SECTION: PROFILE_SUMMARY", csvText);
        Assert.Contains($"# SECTION: CHROME_PROFILE: {profileId} (Work Profile)", csvText);
        Assert.Contains("# SECTION: SCREENSHOT_METADATA", csvText);
        Assert.Contains("# SECTION: NON_CHROME_APPLICATION_USAGE", csvText);

        // Verify data contents
        Assert.Contains("github.com", csvText);
        Assert.Contains("Visual Studio Code", csvText);
        Assert.Contains("Code.exe", csvText);
        Assert.Contains("SUCCESS", csvText);

        // Verify NON_CHROME_APPLICATION_USAGE headers and zero window titles
        var section5Start = csvText.IndexOf("# SECTION: NON_CHROME_APPLICATION_USAGE", StringComparison.Ordinal);
        var section5Text = csvText.Substring(section5Start);
        Assert.Contains("Application Name,Executable,Date,Start Time,End Time,Duration Seconds,Session Status", section5Text);
        Assert.DoesNotContain("window_title", section5Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Page Title", section5Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportToCsvAsync_PerformsMathematicalDateBoundaryClipping()
    {
        var profileId = "clipping-profile-001";
        await _profileRepository.UpsertProfileAsync(new ChromeProfile
        {
            ProfileId = profileId,
            ProfileName = "Clipping Profile",
            ChromeProfileDir = "Profile 2",
            IsEnrolled = true
        });

        // Define a strict range: 12:00:00 to 13:00:00 UTC
        var rangeStartUtc = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var rangeEndUtc = new DateTime(2026, 10, 20, 13, 0, 0, DateTimeKind.Utc);

        // Case 1: Session starts before range (11:30:00) and ends within range (12:30:00)
        // Expected clipped duration = 12:00 to 12:30 = 1800s; status = CLIPPED_BOUNDARY
        await _activityRepository.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = "sess-pre-boundary",
            ProfileId = profileId,
            ChromeTabId = 10,
            Url = "https://example.com/pre",
            SanitizedUrl = "https://example.com/pre",
            Domain = "example.com",
            PageTitle = "Pre Boundary Page",
            StartTimeUtc = rangeStartUtc.AddMinutes(-30),
            EndTimeUtc = rangeStartUtc.AddMinutes(30),
            ActiveDurationSeconds = 3600,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        // Case 2: Session starts within range (12:30:00) and ends after range (13:30:00)
        // Expected clipped duration = 12:30 to 13:00 = 1800s; status = CLIPPED_BOUNDARY
        await _activityRepository.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = "sess-post-boundary",
            ProfileId = profileId,
            ChromeTabId = 11,
            Url = "https://example.com/post",
            SanitizedUrl = "https://example.com/post",
            Domain = "example.com",
            PageTitle = "Post Boundary Page",
            StartTimeUtc = rangeStartUtc.AddMinutes(30),
            EndTimeUtc = rangeStartUtc.AddMinutes(90),
            ActiveDurationSeconds = 3600,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        // Case 3: Session completely outside range (14:00 to 15:00) -> should NOT be included
        await _activityRepository.OpenUrlSessionAsync(new UrlActivitySession
        {
            SessionId = "sess-outside",
            ProfileId = profileId,
            ChromeTabId = 12,
            Url = "https://example.com/outside",
            SanitizedUrl = "https://example.com/outside",
            Domain = "example.com",
            PageTitle = "Outside Page",
            StartTimeUtc = rangeStartUtc.AddHours(2),
            EndTimeUtc = rangeStartUtc.AddHours(3),
            ActiveDurationSeconds = 3600,
            IsCompleted = true,
            SessionStatus = "COMPLETED"
        });

        var req = new CsvExportRequest
        {
            StartDate = rangeStartUtc.ToString("o"),
            EndDate = rangeEndUtc.ToString("o"),
            ProfileIds = new List<string> { profileId }
        };

        var result = await _exportService.ExportToCsvAsync(req);

        Assert.Equal(2, result.RecordCounts.UrlSessionsExported);
        var csvText = File.ReadAllText(result.FilePath, Encoding.UTF8);

        Assert.Contains("CLIPPED_BOUNDARY", csvText);
        Assert.Contains("Pre Boundary Page", csvText);
        Assert.Contains("Post Boundary Page", csvText);
        Assert.DoesNotContain("Outside Page", csvText);
    }

    private sealed class TestIpcServer : INamedPipeIpcServer
    {
        public event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived { add { } remove { } }
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected { add { } remove { } }
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected { add { } remove { } }

        public bool IsRunning => true;
        public string PipeName => "TestPipe";
        public IReadOnlyList<string> ConnectedProfileIds => Array.Empty<string>();

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public Task<bool> SendMessageAsync(string profileId, IpcMessage message, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    [Fact]
    public async Task ApiEndpoint_PostExportCsv_And_Download_WorksOverHttp()
    {
        var testPort = 48128;
        var tokenProvider = new LocalCapabilityTokenProvider(_storage);
        var arbiter = new ActivityArbiter(_activityRepository);
        var scheduler = new ScreenshotScheduler(_settingsRepository, arbiter, new TestIpcServer(), _activityRepository, _storage);

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

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{testPort}") };
        var token = tokenProvider.GetOrCreateToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-LAT-Requested-With", "LocalActivityTracker");

        // 1. POST /api/export/csv
        var postReq = new CsvExportRequest
        {
            StartDate = "2026-10-01",
            EndDate = "2026-10-22",
            ProfileIds = new List<string> { "ALL" }
        };

        var response = await client.PostAsJsonAsync("/api/export/csv", postReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("SUCCESS", json.GetProperty("status").GetString());
        var fileName = json.GetProperty("file_name").GetString()!;
        Assert.NotEmpty(fileName);

        // 2. GET /api/export/download
        var downloadResponse = await client.GetAsync($"/api/export/download?file={Uri.EscapeDataString(fileName)}");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("text/csv", downloadResponse.Content.Headers.ContentType?.MediaType);

        var contentBytes = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.True(contentBytes.Length >= 3);
        Assert.Equal(0xEF, contentBytes[0]); // UTF-8 BOM
        Assert.Equal(0xBB, contentBytes[1]);
        Assert.Equal(0xBF, contentBytes[2]);
    }
}
