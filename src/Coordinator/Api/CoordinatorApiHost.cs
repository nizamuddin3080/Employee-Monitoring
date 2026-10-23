using System.Diagnostics;
using System.Net;
using System.Text.Json;
using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Export;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Profiles;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Screenshots;
using LocalActivityTracker.Core.Storage;

namespace LocalActivityTracker.Coordinator.Api;

/// <summary>
/// Embedded Kestrel Minimal API server bound strictly to loopback 127.0.0.1:48120.
/// Hosts the hardened local REST API and serves the bundled React dashboard SPA.
/// </summary>
public sealed class CoordinatorApiHost : IAsyncDisposable
{
    public const int DefaultPort = 48120;
    public const string DefaultBindAddress = "http://127.0.0.1";

    private readonly StoragePathProvider _storage;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IActivityRepository _activityRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IActivityArbiter _arbiter;
    private readonly IScreenshotScheduler _screenshotScheduler;
    private readonly ICsvExportService _csvExportService;
    private readonly IDatabaseMaintenanceService _databaseMaintenanceService;
    private readonly LocalCapabilityTokenProvider _tokenProvider;
    private readonly int _port;

    private WebApplication? _app;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public int Port => _port;
    public string ServerUrl => $"{DefaultBindAddress}:{_port}";

    public CoordinatorApiHost(
        StoragePathProvider storage,
        ISqliteConnectionFactory connectionFactory,
        IActivityRepository activityRepository,
        IProfileRepository profileRepository,
        ISettingsRepository settingsRepository,
        IActivityArbiter arbiter,
        IScreenshotScheduler screenshotScheduler,
        LocalCapabilityTokenProvider? tokenProvider = null,
        int port = DefaultPort,
        ICsvExportService? csvExportService = null,
        IDatabaseMaintenanceService? databaseMaintenanceService = null)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _activityRepository = activityRepository ?? throw new ArgumentNullException(nameof(activityRepository));
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _arbiter = arbiter ?? throw new ArgumentNullException(nameof(arbiter));
        _screenshotScheduler = screenshotScheduler ?? throw new ArgumentNullException(nameof(screenshotScheduler));
        _csvExportService = csvExportService ?? new CsvExportService(_storage, _activityRepository, _profileRepository);
        _databaseMaintenanceService = databaseMaintenanceService ?? new DatabaseMaintenanceService(_connectionFactory, _storage);
        _tokenProvider = tokenProvider ?? new LocalCapabilityTokenProvider(_storage);
        _port = port;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
        {
            return;
        }

        var builder = WebApplication.CreateBuilder();

        // Bind strictly to loopback IPv4 on designated port
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, _port);
        });

        builder.Services.AddSingleton(_storage);
        builder.Services.AddSingleton(_connectionFactory);
        builder.Services.AddSingleton(_activityRepository);
        builder.Services.AddSingleton(_profileRepository);
        builder.Services.AddSingleton(_settingsRepository);
        builder.Services.AddSingleton(_arbiter);
        builder.Services.AddSingleton(_screenshotScheduler);
        builder.Services.AddSingleton(_csvExportService);
        builder.Services.AddSingleton(_databaseMaintenanceService);
        builder.Services.AddSingleton(_tokenProvider);

        var app = builder.Build();

        // 1. Hardened Security Middleware
        app.UseMiddleware<ApiSecurityMiddleware>(_tokenProvider, _port);

        // 2. Static SPA Files Serving
        var spaDistPath = ResolveSpaDistDirectory();
        if (Directory.Exists(spaDistPath))
        {
            app.UseDefaultFiles(new DefaultFilesOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaDistPath)
            });
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaDistPath)
            });
        }

        // 3. Register REST API Endpoints
        MapApiEndpoints(app);

        // 4. SPA Fallback Routing
        if (Directory.Exists(spaDistPath))
        {
            app.MapFallbackToFile("index.html", new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaDistPath)
            });
        }

        _app = app;
        await _app.StartAsync(cancellationToken);
        _isRunning = true;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning || _app == null)
        {
            return;
        }

        _isRunning = false;
        await _app.StopAsync(cancellationToken);
        await _app.DisposeAsync();
        _app = null;
    }

    private void MapApiEndpoints(WebApplication app)
    {
        // --- Authentication & Capability Token ---
        app.MapGet("/api/auth/token", (LocalCapabilityTokenProvider tokenProv) =>
        {
            return Results.Ok(new { token = tokenProv.GetOrCreateToken() });
        });

        app.MapGet("/api/auth/bootstrap", (LocalCapabilityTokenProvider tokenProv) =>
        {
            return Results.Ok(new { token = tokenProv.GetOrCreateToken() });
        });

        // --- Overview & Live Activity Summary (TASK-051) ---
        app.MapGet("/api/overview", async (IActivityRepository actRepo, IProfileRepository profRepo, IActivityArbiter arbiter) =>
        {
            var now = DateTime.UtcNow;
            var startOfDayUtc = DateTime.UtcNow.Date;
            var endOfDayUtc = startOfDayUtc.AddDays(1);

            var profiles = await profRepo.GetAllProfilesAsync();
            var enrolledCount = profiles.Count(p => p.IsEnrolled);

            var allUrlSessions = new List<UrlActivitySession>();
            foreach (var profile in profiles)
            {
                var sessions = await actRepo.GetUrlSessionsInRangeAsync(profile.ProfileId, startOfDayUtc, endOfDayUtc);
                allUrlSessions.AddRange(sessions);
            }

            var activeUrlSeconds = allUrlSessions.Sum(s => s.ActiveDurationSeconds);
            var visitsCount = allUrlSessions.Count;

            var appSessions = await actRepo.GetApplicationSessionsInRangeAsync(startOfDayUtc, endOfDayUtc);
            var nonChromeAppSeconds = appSessions.Sum(a => a.DurationSeconds);

            var screenshots = await actRepo.GetScreenshotsInRangeAsync(null, startOfDayUtc, endOfDayUtc);
            var successScreenshots = screenshots.Count(s => s.CaptureStatus == "SUCCESS");
            var failedScreenshots = screenshots.Count(s => s.CaptureStatus != "SUCCESS");

            // Overall Chrome foreground time calculation
            var chromeForegroundSeconds = activeUrlSeconds; // baseline active time

            return Results.Ok(new
            {
                date_local = DateTime.Now.ToString("yyyy-MM-dd"),
                chrome_foreground_seconds = chromeForegroundSeconds,
                active_url_seconds = activeUrlSeconds,
                non_chrome_app_seconds = nonChromeAppSeconds,
                enrolled_profiles_count = enrolledCount,
                total_url_visits = visitsCount,
                screenshots_successful = successScreenshots,
                screenshots_failed = failedScreenshots,
                last_event_time_utc = allUrlSessions.LastOrDefault()?.StartTimeUtc ?? now,
                is_tracking_paused = arbiter.IsTrackingPaused
            });
        });

        // --- Chrome Activity Timeline (TASK-052) ---
        app.MapGet("/api/activity", async (
            IActivityRepository actRepo,
            IProfileRepository profRepo,
            string? startDate,
            string? endDate,
            string? profileId,
            string? domain,
            int? page,
            int? pageSize) =>
        {
            var pIndex = Math.Max(1, page ?? 1);
            var pSize = Math.Clamp(pageSize ?? 50, 1, 500);

            var startUtc = !string.IsNullOrWhiteSpace(startDate) && DateTime.TryParse(startDate, out var s) ? s.ToUniversalTime() : DateTime.UtcNow.Date.AddDays(-7);
            var endUtc = !string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, out var e) ? e.ToUniversalTime() : DateTime.UtcNow.Date.AddDays(1);

            var profiles = await profRepo.GetAllProfilesAsync();
            var profileMap = profiles.ToDictionary(p => p.ProfileId, p => p.ProfileName, StringComparer.OrdinalIgnoreCase);

            var targetProfile = (!string.IsNullOrWhiteSpace(profileId) && !profileId.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                ? profileId
                : null;
            var rawList = await actRepo.GetUrlSessionsInRangeAsync(targetProfile, startUtc, endUtc);
            var list = rawList.ToList();

            if (!string.IsNullOrWhiteSpace(domain))
            {
                list = list.Where(s => s.Domain.Contains(domain, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var totalCount = list.Count;
            var paginated = list.OrderByDescending(s => s.StartTimeUtc)
                .Skip((pIndex - 1) * pSize)
                .Take(pSize)
                .Select(s => new
                {
                    session_id = s.SessionId,
                    profile_id = s.ProfileId,
                    profile_name = profileMap.TryGetValue(s.ProfileId, out var name) ? name : "Unknown",
                    url = s.SanitizedUrl,
                    domain = s.Domain,
                    page_title = s.PageTitle,
                    start_time_utc = s.StartTimeUtc,
                    end_time_utc = s.EndTimeUtc,
                    duration_seconds = s.ActiveDurationSeconds,
                    session_status = s.SessionStatus
                })
                .ToList();

            return Results.Ok(new
            {
                total_records = totalCount,
                page = pIndex,
                page_size = pSize,
                records = paginated
            });
        });

        // --- Non-Chrome Application Usage (TASK-054 — Strictly NO Window Titles) ---
        app.MapGet("/api/applications", async (
            IActivityRepository actRepo,
            string? startDate,
            string? endDate) =>
        {
            var startUtc = !string.IsNullOrWhiteSpace(startDate) && DateTime.TryParse(startDate, out var s) ? s.ToUniversalTime() : DateTime.UtcNow.Date;
            var endUtc = !string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, out var e) ? e.ToUniversalTime() : DateTime.UtcNow.Date.AddDays(1);

            var sessions = await actRepo.GetApplicationSessionsInRangeAsync(startUtc, endUtc);

            var grouped = sessions
                .GroupBy(a => new { a.ProcessName, a.ApplicationDisplayName })
                .Select(g => new
                {
                    application_display_name = g.Key.ApplicationDisplayName,
                    process_name = g.Key.ProcessName,
                    total_duration_seconds = g.Sum(x => x.DurationSeconds),
                    session_count = g.Count(),
                    sessions = g.OrderByDescending(x => x.StartTimeUtc).Select(x => new
                    {
                        session_id = x.SessionId,
                        start_time_utc = x.StartTimeUtc,
                        end_time_utc = x.EndTimeUtc,
                        duration_seconds = x.DurationSeconds,
                        session_status = x.SessionStatus
                    }).ToList()
                })
                .OrderByDescending(x => x.total_duration_seconds)
                .ToList();

            return Results.Ok(grouped);
        });

        // --- Profile Management & Health (TASK-055) ---
        app.MapGet("/api/profiles", async (IProfileRepository profRepo) =>
        {
            var enrolled = await profRepo.GetAllProfilesAsync();
            var scanner = new ChromeProfileScanner(profRepo);
            var scanned = scanner.DiscoverLocalProfiles();

            var enrolledIds = new HashSet<string>(enrolled.Select(p => p.ProfileId), StringComparer.OrdinalIgnoreCase);
            var unmonitored = scanned.Where(s => !enrolledIds.Contains(s.ProfileDirectoryName)).ToList();

            return Results.Ok(new
            {
                enrolled_profiles = enrolled,
                unmonitored_profiles = unmonitored,
                has_unmonitored_profiles = unmonitored.Count > 0
            });
        });

        app.MapPut("/api/profiles/{id}", async (string id, JsonElement body, IProfileRepository profRepo) =>
        {
            var existing = await profRepo.GetProfileByIdAsync(id);
            if (existing == null)
            {
                return Results.NotFound(new { error = $"Profile '{id}' not found." });
            }

            if (body.TryGetProperty("profile_name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
            {
                existing.ProfileName = nameProp.GetString()!;
                await profRepo.UpsertProfileAsync(existing);
            }

            return Results.Ok(existing);
        });

        // --- Screenshot Gallery & Viewer (TASK-053) ---
        app.MapGet("/api/screenshots", async (
            IActivityRepository actRepo,
            string? profileId,
            string? startDate,
            string? endDate,
            int? page,
            int? pageSize) =>
        {
            var pIndex = Math.Max(1, page ?? 1);
            var pSize = Math.Clamp(pageSize ?? 30, 1, 100);

            var startUtc = !string.IsNullOrWhiteSpace(startDate) && DateTime.TryParse(startDate, out var s) ? s.ToUniversalTime() : DateTime.UtcNow.Date.AddDays(-7);
            var endUtc = !string.IsNullOrWhiteSpace(endDate) && DateTime.TryParse(endDate, out var e) ? e.ToUniversalTime() : DateTime.UtcNow.Date.AddDays(1);

            var allScreenshots = await actRepo.GetScreenshotsInRangeAsync(
                string.IsNullOrWhiteSpace(profileId) || profileId.Equals("ALL", StringComparison.OrdinalIgnoreCase) ? null : profileId,
                startUtc,
                endUtc);

            var totalCount = allScreenshots.Count;
            var paginated = allScreenshots.OrderByDescending(s => s.CaptureTimeUtc)
                .Skip((pIndex - 1) * pSize)
                .Take(pSize)
                .Select(s => new
                {
                    screenshot_id = s.ScreenshotId,
                    profile_id = s.ProfileId,
                    url = s.Url,
                    capture_time_utc = s.CaptureTimeUtc,
                    capture_status = s.CaptureStatus,
                    failure_reason = s.FailureReason,
                    file_size_bytes = s.FileSizeBytes,
                    has_image = !string.IsNullOrEmpty(s.FilePath) && File.Exists(s.FilePath)
                })
                .ToList();

            return Results.Ok(new
            {
                total_records = totalCount,
                page = pIndex,
                page_size = pSize,
                records = paginated
            });
        });

        app.MapGet("/api/screenshots/{id}/image", async (string id, IActivityRepository actRepo, StoragePathProvider storage) =>
        {
            var meta = await actRepo.GetScreenshotMetadataByIdAsync(id);
            if (meta == null || string.IsNullOrWhiteSpace(meta.FilePath))
            {
                return Results.NotFound(new { error = "Screenshot not found." });
            }

            // Path traversal defense
            var canonicalPath = Path.GetFullPath(meta.FilePath);
            var canonicalStorageDir = Path.GetFullPath(storage.ScreenshotsDirectory);

            if (!canonicalPath.StartsWith(canonicalStorageDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(canonicalPath))
            {
                return Results.NotFound(new { error = "Screenshot file not found on disk." });
            }

            return Results.File(canonicalPath, "image/png");
        });

        app.MapDelete("/api/screenshots/{id}", async (string id, IActivityRepository actRepo, StoragePathProvider storage) =>
        {
            var meta = await actRepo.GetScreenshotMetadataByIdAsync(id);
            if (meta != null && !string.IsNullOrWhiteSpace(meta.FilePath))
            {
                var canonicalPath = Path.GetFullPath(meta.FilePath);
                var canonicalStorageDir = Path.GetFullPath(storage.ScreenshotsDirectory);

                if (canonicalPath.StartsWith(canonicalStorageDir, StringComparison.OrdinalIgnoreCase) && File.Exists(canonicalPath))
                {
                    try { File.Delete(canonicalPath); } catch { }
                }
            }

            var deleted = await actRepo.DeleteScreenshotMetadataAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        app.MapPost("/api/screenshots/capture", async (IScreenshotScheduler scheduler, JsonElement? body) =>
        {
            string? targetProfileId = null;
            if (body.HasValue && body.Value.TryGetProperty("profile_id", out var pidProp))
            {
                targetProfileId = pidProp.GetString();
            }

            var result = await scheduler.TriggerCaptureAsync(targetProfileId);
            if (result == null)
            {
                return Results.BadRequest(new { error = "Capture cannot be executed. Chrome must hold OS desktop focus." });
            }

            return Results.Ok(result);
        });

        // --- Application Settings & Controls (TASK-056) ---
        app.MapGet("/api/settings", async (ISettingsRepository setRepo) =>
        {
            var settings = await setRepo.GetAllSettingsAsync();
            return Results.Ok(settings);
        });

        app.MapPost("/api/settings", async (ISettingsRepository setRepo, JsonElement body) =>
        {
            foreach (var prop in body.EnumerateObject())
            {
                await setRepo.SetSettingAsync(prop.Name, prop.Value.GetString() ?? prop.Value.GetRawText());
            }

            var updated = await setRepo.GetAllSettingsAsync();
            return Results.Ok(updated);
        });

        app.MapPost("/api/tracking/pause", async (IActivityArbiter arbiter) =>
        {
            await arbiter.OnSessionLockedAsync();
            return Results.Ok(new { is_tracking_paused = true });
        });

        app.MapPost("/api/tracking/resume", async (IActivityArbiter arbiter) =>
        {
            await arbiter.OnSessionUnlockedAsync();
            return Results.Ok(new { is_tracking_paused = false });
        });

        // --- Custom Date Range Master CSV Export (TASK-057, TASK-058, TASK-063) ---
        app.MapPost("/api/export/csv", async (ICsvExportService exportService, CsvExportRequest? request, CancellationToken ct) =>
        {
            request ??= new CsvExportRequest();
            try
            {
                var result = await exportService.ExportToCsvAsync(request, ct);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        app.MapGet("/api/export/download", (string file, StoragePathProvider storage) =>
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return Results.BadRequest(new { error = "File parameter is required." });
            }

            var fileName = Path.GetFileName(file);
            var canonicalPath = Path.GetFullPath(Path.Combine(storage.ExportsDirectory, fileName));
            var canonicalStorageDir = Path.GetFullPath(storage.ExportsDirectory);

            if (!canonicalPath.StartsWith(canonicalStorageDir, StringComparison.OrdinalIgnoreCase) || !File.Exists(canonicalPath))
            {
                return Results.NotFound(new { error = "Export file not found." });
            }

            return Results.File(canonicalPath, "text/csv", fileName);
        });

        app.MapPost("/api/export/reveal", (JsonElement? body, StoragePathProvider storage) =>
        {
            string targetPath = storage.ExportsDirectory;
            if (body.HasValue && body.Value.TryGetProperty("file_path", out var pathProp) && !string.IsNullOrWhiteSpace(pathProp.GetString()))
            {
                var cand = Path.GetFullPath(pathProp.GetString()!);
                if (cand.StartsWith(Path.GetFullPath(storage.ExportsDirectory), StringComparison.OrdinalIgnoreCase) && File.Exists(cand))
                {
                    targetPath = cand;
                }
            }

            try
            {
                if (File.Exists(targetPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{targetPath}\"") { UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{storage.ExportsDirectory}\"") { UseShellExecute = true });
                }
                return Results.Ok(new { revealed = true, path = targetPath });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        // --- Database Backup & Safe Purge (TASK-066) ---
        app.MapPost("/api/data/backup", async (IDatabaseMaintenanceService maintenance, CancellationToken ct) =>
        {
            try
            {
                var backupPath = await maintenance.CreateBackupAsync(ct);
                return Results.Ok(new
                {
                    status = "SUCCESS",
                    backup_path = backupPath,
                    file_name = Path.GetFileName(backupPath)
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });

        app.MapPost("/api/data/purge", async (IDatabaseMaintenanceService maintenance, JsonElement? body, CancellationToken ct) =>
        {
            try
            {
                var purgeAll = body.HasValue && body.Value.TryGetProperty("purge_all", out var pProp) && pProp.GetBoolean();
                if (purgeAll)
                {
                    await maintenance.PurgeAllDataAsync(ct);
                    return Results.Ok(new { status = "SUCCESS", purged_all = true });
                }

                var days = 30;
                if (body.HasValue && body.Value.TryGetProperty("older_than_days", out var dProp) && dProp.TryGetInt32(out var dVal))
                {
                    days = Math.Max(1, dVal);
                }

                var deleted = await maintenance.PurgeOldRecordsAsync(days, ct);
                return Results.Ok(new { status = "SUCCESS", purged_all = false, older_than_days = days, deleted_rows = deleted });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 500);
            }
        });
    }

    private static string ResolveSpaDistDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "LocalDashboard", "dist"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "LocalDashboard", "dist")
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "index.html")))
            {
                return Path.GetFullPath(dir);
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "src", "LocalDashboard", "dist");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
