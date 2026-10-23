using System.Globalization;
using System.Text;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Storage;

namespace LocalActivityTracker.Core.Export;

/// <summary>
/// Generates a single, consolidated RFC-4180 CSV export adhering to master prompt requirements:
/// 1. UTF-8 Byte Order Mark (BOM) for native Excel compatibility.
/// 2. Formula injection defense prepending single quotes (') to =, +, -, @, \t, \r.
/// 3. Mathematical date boundary clipping without timestamp fabrication.
/// 4. 5 structured sections with clear profile separation.
/// 5. Strictly ZERO window titles in Section 5 (Application Usage).
/// </summary>
public sealed class CsvExportService : ICsvExportService
{
    private readonly StoragePathProvider _storage;
    private readonly IActivityRepository _activityRepository;
    private readonly IProfileRepository _profileRepository;

    public CsvExportService(
        StoragePathProvider storage,
        IActivityRepository activityRepository,
        IProfileRepository profileRepository)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _activityRepository = activityRepository ?? throw new ArgumentNullException(nameof(activityRepository));
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
    }

    public async Task<CsvExportResult> ExportToCsvAsync(CsvExportRequest request, CancellationToken cancellationToken = default)
    {
        _storage.EnsureDirectoriesExist();

        // 1. Parse and validate date range
        var (startUtc, endUtc, dateLabelStart, dateLabelEnd) = ParseAndValidateRange(request.StartDate, request.EndDate);

        // 2. Resolve target profiles
        var allProfiles = await _profileRepository.GetAllProfilesAsync(cancellationToken);
        var targetProfiles = ResolveTargetProfiles(allProfiles, request.ProfileIds);

        // 3. Prepare target file path with UTF-8 BOM
        var fileName = $"activity_report_{dateLabelStart}_to_{dateLabelEnd}.csv";
        var filePath = Path.Combine(_storage.ExportsDirectory, fileName);

        var counts = new CsvRecordCounts
        {
            ProfilesIncluded = targetProfiles.Count
        };

        // Open stream with explicit UTF-8 BOM (0xEF, 0xBB, 0xBF)
        using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            // SECTION 1: REPORT_METADATA
            if (request.IncludeReportMetadata)
            {
                await WriteSection1MetadataAsync(writer, dateLabelStart, dateLabelEnd, targetProfiles.Count);
            }

            // SECTION 2: PROFILE_SUMMARY
            if (request.IncludeProfileSummary)
            {
                await WriteSection2ProfileSummaryAsync(writer, targetProfiles, startUtc, endUtc, cancellationToken);
            }

            // SECTION 3: CHROME_PROFILE (for each enrolled profile)
            if (request.IncludeUrlActivity)
            {
                foreach (var profile in targetProfiles)
                {
                    var exportedCount = await WriteSection3ProfileActivityAsync(writer, profile, startUtc, endUtc, cancellationToken);
                    counts.UrlSessionsExported += exportedCount;
                }
            }

            // SECTION 4: SCREENSHOT_METADATA
            if (request.IncludeScreenshotMetadata)
            {
                var exportedCount = await WriteSection4ScreenshotsAsync(writer, targetProfiles, allProfiles, startUtc, endUtc, cancellationToken);
                counts.ScreenshotRecordsExported += exportedCount;
            }

            // SECTION 5: NON_CHROME_APPLICATION_USAGE (Strictly NO window_title)
            if (request.IncludeNonChromeApps)
            {
                var exportedCount = await WriteSection5AppUsageAsync(writer, startUtc, endUtc, cancellationToken);
                counts.ApplicationSessionsExported += exportedCount;
            }

            await writer.FlushAsync(cancellationToken);
        }

        return new CsvExportResult
        {
            Status = "SUCCESS",
            FileName = fileName,
            FilePath = filePath,
            RecordCounts = counts
        };
    }

    private static (DateTime startUtc, DateTime endUtc, string labelStart, string labelEnd) ParseAndValidateRange(string? startStr, string? endStr)
    {
        DateTime localStart;
        DateTime localEnd;

        if (string.IsNullOrWhiteSpace(startStr))
        {
            localStart = DateTime.Today.AddDays(-7);
        }
        else if (DateTime.TryParse(startStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart))
        {
            localStart = parsedStart.Date == parsedStart ? parsedStart.Date : parsedStart;
        }
        else
        {
            localStart = DateTime.Today.AddDays(-7);
        }

        if (string.IsNullOrWhiteSpace(endStr))
        {
            localEnd = DateTime.Today.AddDays(1).AddTicks(-1);
        }
        else if (DateTime.TryParse(endStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
        {
            localEnd = parsedEnd.Date == parsedEnd ? parsedEnd.Date.AddDays(1).AddTicks(-1) : parsedEnd;
        }
        else
        {
            localEnd = DateTime.Today.AddDays(1).AddTicks(-1);
        }

        var startUtc = localStart.ToUniversalTime();
        var endUtc = localEnd.ToUniversalTime();

        if (startUtc > endUtc)
        {
            throw new ArgumentException("Invalid date range: start_date must be before or equal to end_date.");
        }

        var labelStart = localStart.ToString("yyyy-MM-dd");
        var labelEnd = localEnd.ToString("yyyy-MM-dd");

        return (startUtc, endUtc, labelStart, labelEnd);
    }

    private static List<ChromeProfile> ResolveTargetProfiles(IReadOnlyList<ChromeProfile> allProfiles, List<string>? requestedIds)
    {
        if (allProfiles.Count == 0)
        {
            return new List<ChromeProfile>();
        }

        if (requestedIds == null || requestedIds.Count == 0 || requestedIds.Any(id => id.Equals("ALL", StringComparison.OrdinalIgnoreCase)))
        {
            // Default to enrolled profiles; if none enrolled, all profiles
            var enrolled = allProfiles.Where(p => p.IsEnrolled).ToList();
            return enrolled.Count > 0 ? enrolled : allProfiles.ToList();
        }

        var set = new HashSet<string>(requestedIds, StringComparer.OrdinalIgnoreCase);
        return allProfiles.Where(p => set.Contains(p.ProfileId)).ToList();
    }

    private static async Task WriteSection1MetadataAsync(StreamWriter writer, string labelStart, string labelEnd, int totalProfiles)
    {
        var tz = TimeZoneInfo.Local;
        var offset = tz.GetUtcOffset(DateTime.Now);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var tzDisplay = $"UTC{sign}{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2} ({tz.StandardName})";

        await writer.WriteLineAsync("# SECTION: REPORT_METADATA");
        await writer.WriteLineAsync("Key,Value");
        await writer.WriteLineAsync($"Report Generated,{CsvFormatter.EscapeAndSanitize(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}");
        await writer.WriteLineAsync($"Date Range Selected,{CsvFormatter.EscapeAndSanitize($"{labelStart} to {labelEnd}")}");
        await writer.WriteLineAsync($"Timezone,{CsvFormatter.EscapeAndSanitize(tzDisplay)}");
        await writer.WriteLineAsync($"Total Profiles Enrolled,{totalProfiles}");
        await writer.WriteLineAsync(); // Blank row separator
    }

    private async Task WriteSection2ProfileSummaryAsync(
        StreamWriter writer,
        List<ChromeProfile> targetProfiles,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync("# SECTION: PROFILE_SUMMARY");
        await writer.WriteLineAsync("Profile ID,Profile Name,Total Active URL Time (s),Total Chrome Foreground Time (s),URL Visits,Successful Screenshots,Failed Screenshots");

        foreach (var profile in targetProfiles)
        {
            var urlSessions = await _activityRepository.GetUrlSessionsInRangeAsync(profile.ProfileId, startUtc, endUtc, cancellationToken);
            var browserSessions = await _activityRepository.GetBrowserSessionsInRangeAsync(profile.ProfileId, startUtc, endUtc, cancellationToken);
            var screenshots = await _activityRepository.GetScreenshotsInRangeAsync(profile.ProfileId, startUtc, endUtc, cancellationToken);

            int totalActiveUrlSeconds = 0;
            foreach (var session in urlSessions)
            {
                var sEnd = session.EndTimeUtc ?? session.LastHeartbeatUtc;
                if (session.StartTimeUtc <= endUtc && sEnd >= startUtc)
                {
                    var t1 = session.StartTimeUtc > startUtc ? session.StartTimeUtc : startUtc;
                    var t2 = sEnd < endUtc ? sEnd : endUtc;
                    if (t2 > t1)
                    {
                        totalActiveUrlSeconds += (int)Math.Round((t2 - t1).TotalSeconds);
                    }
                }
            }

            int totalChromeForegroundSeconds = 0;
            foreach (var bSession in browserSessions)
            {
                var bEnd = bSession.EndTimeUtc ?? endUtc;
                if (bSession.StartTimeUtc <= endUtc && bEnd >= startUtc)
                {
                    var t1 = bSession.StartTimeUtc > startUtc ? bSession.StartTimeUtc : startUtc;
                    var t2 = bEnd < endUtc ? bEnd : endUtc;
                    if (t2 > t1)
                    {
                        totalChromeForegroundSeconds += (int)Math.Round((t2 - t1).TotalSeconds);
                    }
                }
            }

            // Fallback to active URL seconds if browser sessions were not populated
            if (totalChromeForegroundSeconds < totalActiveUrlSeconds)
            {
                totalChromeForegroundSeconds = totalActiveUrlSeconds;
            }

            var visits = urlSessions.Count;
            var successScreenshots = screenshots.Count(s => s.CaptureStatus == "SUCCESS");
            var failedScreenshots = screenshots.Count(s => s.CaptureStatus != "SUCCESS");

            await writer.WriteLineAsync(string.Join(",",
                CsvFormatter.EscapeAndSanitize(profile.ProfileId),
                CsvFormatter.EscapeAndSanitize(profile.ProfileName),
                totalActiveUrlSeconds,
                totalChromeForegroundSeconds,
                visits,
                successScreenshots,
                failedScreenshots
            ));
        }

        await writer.WriteLineAsync(); // Blank row separator
    }

    private async Task<int> WriteSection3ProfileActivityAsync(
        StreamWriter writer,
        ChromeProfile profile,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync($"# SECTION: CHROME_PROFILE: {profile.ProfileId} ({profile.ProfileName})");
        await writer.WriteLineAsync("Profile ID,Profile Name,Date,Start Time,End Time,Duration Seconds,Page Title,URL,Domain,Session Status");

        var sessions = await _activityRepository.GetUrlSessionsInRangeAsync(profile.ProfileId, startUtc, endUtc, cancellationToken);
        int count = 0;

        foreach (var s in sessions)
        {
            var sessionEnd = s.EndTimeUtc ?? s.LastHeartbeatUtc;

            // Mathematical boundary clipping
            if (s.StartTimeUtc > endUtc || sessionEnd < startUtc)
            {
                continue;
            }

            var t1 = s.StartTimeUtc > startUtc ? s.StartTimeUtc : startUtc;
            var t2 = sessionEnd < endUtc ? sessionEnd : endUtc;

            if (t2 < t1)
            {
                continue;
            }

            var clippedDuration = Math.Max(0, (int)Math.Round((t2 - t1).TotalSeconds));
            var isClipped = s.StartTimeUtc < startUtc || sessionEnd > endUtc;
            var reportedStatus = isClipped ? "CLIPPED_BOUNDARY" : (string.IsNullOrWhiteSpace(s.SessionStatus) ? "COMPLETED" : s.SessionStatus);

            var localT1 = t1.ToLocalTime();
            var localT2 = t2.ToLocalTime();

            await writer.WriteLineAsync(string.Join(",",
                CsvFormatter.EscapeAndSanitize(profile.ProfileId),
                CsvFormatter.EscapeAndSanitize(profile.ProfileName),
                CsvFormatter.EscapeAndSanitize(localT1.ToString("yyyy-MM-dd")),
                CsvFormatter.EscapeAndSanitize(localT1.ToString("HH:mm:ss")),
                CsvFormatter.EscapeAndSanitize(localT2.ToString("HH:mm:ss")),
                clippedDuration,
                CsvFormatter.EscapeAndSanitize(s.PageTitle),
                CsvFormatter.EscapeAndSanitize(s.SanitizedUrl),
                CsvFormatter.EscapeAndSanitize(s.Domain),
                CsvFormatter.EscapeAndSanitize(reportedStatus)
            ));

            count++;
        }

        await writer.WriteLineAsync(); // Blank row separator
        return count;
    }

    private async Task<int> WriteSection4ScreenshotsAsync(
        StreamWriter writer,
        List<ChromeProfile> targetProfiles,
        IReadOnlyList<ChromeProfile> allProfiles,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync("# SECTION: SCREENSHOT_METADATA");
        await writer.WriteLineAsync("Profile ID,Profile Name,Capture Timestamp,Associated URL,Local Screenshot Path,Capture Status,Failure Reason");

        var profileMap = allProfiles.ToDictionary(p => p.ProfileId, p => p.ProfileName, StringComparer.OrdinalIgnoreCase);
        var targetSet = new HashSet<string>(targetProfiles.Select(p => p.ProfileId), StringComparer.OrdinalIgnoreCase);

        var screenshots = await _activityRepository.GetScreenshotsInRangeAsync(null, startUtc, endUtc, cancellationToken);
        int count = 0;

        foreach (var sc in screenshots.OrderBy(s => s.CaptureTimeUtc))
        {
            if (!targetSet.Contains(sc.ProfileId))
            {
                continue;
            }

            var profileName = profileMap.TryGetValue(sc.ProfileId, out var name) ? name : "Unknown";
            var localTime = sc.CaptureTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

            await writer.WriteLineAsync(string.Join(",",
                CsvFormatter.EscapeAndSanitize(sc.ProfileId),
                CsvFormatter.EscapeAndSanitize(profileName),
                CsvFormatter.EscapeAndSanitize(localTime),
                CsvFormatter.EscapeAndSanitize(sc.Url),
                CsvFormatter.EscapeAndSanitize(sc.FilePath),
                CsvFormatter.EscapeAndSanitize(sc.CaptureStatus),
                CsvFormatter.EscapeAndSanitize(sc.FailureReason ?? "")
            ));

            count++;
        }

        await writer.WriteLineAsync(); // Blank row separator
        return count;
    }

    private async Task<int> WriteSection5AppUsageAsync(
        StreamWriter writer,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        // STRICT PRIVACY GUARANTEE: Invariant mandates zero window titles in schema and export.
        await writer.WriteLineAsync("# SECTION: NON_CHROME_APPLICATION_USAGE");
        await writer.WriteLineAsync("Application Name,Executable,Date,Start Time,End Time,Duration Seconds,Session Status");

        var appSessions = await _activityRepository.GetApplicationSessionsInRangeAsync(startUtc, endUtc, cancellationToken);
        int count = 0;

        foreach (var app in appSessions.OrderBy(a => a.StartTimeUtc))
        {
            var sessionEnd = app.EndTimeUtc ?? app.LastHeartbeatUtc;

            // Mathematical boundary clipping
            if (app.StartTimeUtc > endUtc || sessionEnd < startUtc)
            {
                continue;
            }

            var t1 = app.StartTimeUtc > startUtc ? app.StartTimeUtc : startUtc;
            var t2 = sessionEnd < endUtc ? sessionEnd : endUtc;

            if (t2 < t1)
            {
                continue;
            }

            var clippedDuration = Math.Max(0, (int)Math.Round((t2 - t1).TotalSeconds));
            var isClipped = app.StartTimeUtc < startUtc || sessionEnd > endUtc;
            var reportedStatus = isClipped ? "CLIPPED_BOUNDARY" : (string.IsNullOrWhiteSpace(app.SessionStatus) ? "COMPLETED" : app.SessionStatus);

            var localT1 = t1.ToLocalTime();
            var localT2 = t2.ToLocalTime();

            await writer.WriteLineAsync(string.Join(",",
                CsvFormatter.EscapeAndSanitize(app.ApplicationDisplayName),
                CsvFormatter.EscapeAndSanitize(app.ProcessName),
                CsvFormatter.EscapeAndSanitize(localT1.ToString("yyyy-MM-dd")),
                CsvFormatter.EscapeAndSanitize(localT1.ToString("HH:mm:ss")),
                CsvFormatter.EscapeAndSanitize(localT2.ToString("HH:mm:ss")),
                clippedDuration,
                CsvFormatter.EscapeAndSanitize(reportedStatus)
            ));

            count++;
        }

        await writer.WriteLineAsync(); // Blank row separator
        return count;
    }
}
