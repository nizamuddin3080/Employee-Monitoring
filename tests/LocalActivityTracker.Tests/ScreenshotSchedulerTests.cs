using System.Reflection;
using System.Text.Json;
using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Screenshots;
using LocalActivityTracker.Core.Storage;
using LocalActivityTracker.Core.Win32;
using Xunit;

namespace LocalActivityTracker.Tests;

/// <summary>
/// Milestone 8 Verification Suite:
/// Local Screenshot Capture & Targeted Routing Pipeline.
/// </summary>
public class ScreenshotSchedulerTests
{
    private const string ValidPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    private sealed class FakeIpcServer : INamedPipeIpcServer
    {
        public event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<ClientConnectedEventArgs>? ClientConnected { add { } remove { } }
        public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected { add { } remove { } }

        public bool IsRunning { get; set; } = true;
        public string PipeName => "FakePipe";
        public List<string> ConnectedProfiles { get; } = new();
        public IReadOnlyList<string> ConnectedProfileIds => ConnectedProfiles;

        public List<(string ProfileId, IpcMessage Message)> SentMessages { get; } = new();

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;

        public Task<bool> SendMessageAsync(string profileId, IpcMessage message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add((profileId, message));
            return Task.FromResult(true);
        }

        public Task BroadcastAsync(IpcMessage message, CancellationToken cancellationToken = default)
        {
            foreach (var pid in ConnectedProfiles)
            {
                SentMessages.Add((pid, message));
            }
            return Task.CompletedTask;
        }

        public void RaiseMessageReceived(string profileId, IpcMessage message)
        {
            MessageReceived?.Invoke(this, new IpcMessageReceivedEventArgs(profileId, message));
        }

        public void Dispose() { }
    }

    private static async Task<(SqliteConnectionFactory Factory, ActivityRepository ActRepo, SettingsRepository SetRepo, ProfileRepository ProfRepo, StoragePathProvider Storage, string TempDir)> CreateTestEnvironmentAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LAT_ScreenshotTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var storage = new StoragePathProvider(tempDir);
        storage.EnsureDirectoriesExist();

        var factory = new SqliteConnectionFactory(storage);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var actRepo = new ActivityRepository(factory);
        var setRepo = new SettingsRepository(factory);
        var profRepo = new ProfileRepository(factory);

        return (factory, actRepo, setRepo, profRepo, storage, tempDir);
    }

    [Fact]
    public async Task TASK043_ScreenshotScheduler_TargetedRoutingToActiveFocusedProfile()
    {
        var (_, actRepo, setRepo, profRepo, storage, tempDir) = await CreateTestEnvironmentAsync();
        try
        {
            var profileId1 = "profile-alpha";
            var profileId2 = "profile-beta";

            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId1, ProfileName = "Alpha" });
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId2, ProfileName = "Beta" });

            using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());
            var fakeIpc = new FakeIpcServer();
            fakeIpc.ConnectedProfiles.Add(profileId1);
            fakeIpc.ConnectedProfiles.Add(profileId2);

            // Chrome holds OS focus
            var t0 = DateTime.UtcNow;
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(100), 1234, "chrome.exe", "Google Chrome", true, t0));

            // Profile 1 navigates to GitHub
            var navMsg = IpcMessage.Create(IpcMessage.EventTabNavigated, profileId1, new
            {
                tab_id = 42,
                window_id = 1,
                raw_url = "https://github.com/udaysharmadev/Gravitas",
                page_title = "GitHub"
            });
            await arbiter.OnIpcMessageReceivedAsync(profileId1, navMsg);

            Assert.Equal(profileId1, arbiter.CurrentUrlSession?.ProfileId);

            using var scheduler = new ScreenshotScheduler(setRepo, arbiter, fakeIpc, actRepo, storage, overrideIntervalSeconds: 60);
            scheduler.Start();

            // Act: trigger capture
            var captureTask = scheduler.TriggerCaptureAsync();

            // Assert: fake IPC server received targeted message strictly for profile-alpha
            Assert.Single(fakeIpc.SentMessages);
            var (targetPid, sentMsg) = fakeIpc.SentMessages[0];
            Assert.Equal(profileId1, targetPid);
            Assert.Equal(IpcMessage.RequestScreenshot, sentMsg.MessageType);

            // Simulate extension response
            var responseMsg = IpcMessage.Create(IpcMessage.ResponseScreenshot, profileId1, new
            {
                capture_id = sentMsg.CorrelationId,
                profile_id = profileId1,
                url = "https://github.com/udaysharmadev/Gravitas",
                image_format = "png",
                image_data_base64 = ValidPngBase64
            }, correlationId: sentMsg.CorrelationId);

            fakeIpc.RaiseMessageReceived(profileId1, responseMsg);

            var result = await captureTask;

            Assert.NotNull(result);
            Assert.Equal("SUCCESS", result.CaptureStatus);
            Assert.Equal(profileId1, result.ProfileId);
            Assert.True(File.Exists(result.FilePath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK044_TASK045_VisibleTabCapture_DecodesPngAndPreventsPathTraversal()
    {
        var (_, actRepo, setRepo, profRepo, storage, tempDir) = await CreateTestEnvironmentAsync();
        try
        {
            var profileId = "profile-traversal-check";
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId, ProfileName = "SafeProfile" });

            using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());
            var fakeIpc = new FakeIpcServer();
            fakeIpc.ConnectedProfiles.Add(profileId);

            var t0 = DateTime.UtcNow;
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(100), 1234, "chrome.exe", "Google Chrome", true, t0));

            var navMsg = IpcMessage.Create(IpcMessage.EventTabNavigated, profileId, new
            {
                tab_id = 99,
                window_id = 1,
                raw_url = "https://example.com",
                page_title = "Example"
            });
            await arbiter.OnIpcMessageReceivedAsync(profileId, navMsg);

            using var scheduler = new ScreenshotScheduler(setRepo, arbiter, fakeIpc, actRepo, storage);

            var maliciousProfileId = @"..\..\Windows\System32\hack";
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = maliciousProfileId, ProfileName = "MaliciousProfile" });
            var captureId = Guid.NewGuid().ToString("N");

            var responseMsg = IpcMessage.Create(IpcMessage.ResponseScreenshot, maliciousProfileId, new
            {
                capture_id = captureId,
                profile_id = maliciousProfileId,
                url = "https://example.com",
                image_format = "png",
                image_data_base64 = ValidPngBase64
            }, correlationId: captureId);

            // Act
            var meta = await scheduler.ProcessScreenshotResponseAsync(maliciousProfileId, responseMsg);

            // Assert: Filename must reside inside storage.ScreenshotsDirectory and traversal characters must be sanitized
            Assert.Equal("SUCCESS", meta.CaptureStatus);
            Assert.True(File.Exists(meta.FilePath));
            Assert.StartsWith(Path.GetFullPath(storage.ScreenshotsDirectory), Path.GetFullPath(meta.FilePath), StringComparison.OrdinalIgnoreCase);

            var fileName = Path.GetFileName(meta.FilePath);
            Assert.DoesNotContain("..", fileName);
            Assert.DoesNotContain(@"\", fileName);
            Assert.DoesNotContain("/", fileName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Theory]
    [InlineData("RESTRICTED_URL", "Cannot capture internal browser page.")]
    [InlineData("WINDOW_OCCLUDED_OR_MINIMIZED", "Browser window is minimized or occluded.")]
    [InlineData("CAPTURE_TIMEOUT", "Extension capture timed out.")]
    public async Task TASK046_TASK047_RestrictedPagesAndHonestFailureRecording(string failureCode, string reason)
    {
        var (_, actRepo, setRepo, profRepo, storage, tempDir) = await CreateTestEnvironmentAsync();
        try
        {
            var profileId = "profile-failures";
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId, ProfileName = "FailureTest" });

            using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());
            var fakeIpc = new FakeIpcServer();

            using var scheduler = new ScreenshotScheduler(setRepo, arbiter, fakeIpc, actRepo, storage);

            var captureId = Guid.NewGuid().ToString("N");
            var failMsg = IpcMessage.Create(IpcMessage.ResponseScreenshot, profileId, new
            {
                capture_id = captureId,
                profile_id = profileId,
                failure_code = failureCode,
                failure_reason = reason
            }, correlationId: captureId);

            // Act
            var meta = await scheduler.ProcessScreenshotResponseAsync(profileId, failMsg);

            // Assert
            Assert.NotNull(meta);
            Assert.Equal("FAILED", meta.CaptureStatus);
            Assert.Equal(failureCode, meta.FailureReason);
            Assert.Empty(meta.FilePath);
            Assert.Equal(0, meta.FileSizeBytes);

            // Assert SQLite persistence
            var dbMeta = await actRepo.GetScreenshotMetadataByIdAsync(captureId);
            Assert.NotNull(dbMeta);
            Assert.Equal("FAILED", dbMeta.CaptureStatus);
            Assert.Equal(failureCode, dbMeta.FailureReason);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK048_AntiCaptureSafeguards_NeverCapturesWhenNonChromeIsFocusedOrSessionLocked()
    {
        var (_, actRepo, setRepo, profRepo, storage, tempDir) = await CreateTestEnvironmentAsync();
        try
        {
            var profileId = "profile-anticapture";
            await profRepo.UpsertProfileAsync(new ChromeProfile { ProfileId = profileId, ProfileName = "AntiCaptureProfile" });

            using var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());
            var fakeIpc = new FakeIpcServer();
            fakeIpc.ConnectedProfiles.Add(profileId);

            using var scheduler = new ScreenshotScheduler(setRepo, arbiter, fakeIpc, actRepo, storage);

            // 1. External App (Visual Studio Code) holds OS desktop focus
            var t0 = DateTime.UtcNow;
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(200), 5678, "Code.exe", "Visual Studio Code", false, t0));

            Assert.False(arbiter.IsChromeForeground);

            // Trigger capture
            var result = await scheduler.TriggerCaptureAsync();

            // Assert: Must be skipped immediately, zero messages sent, zero screenshots taken
            Assert.Null(result);
            Assert.Empty(fakeIpc.SentMessages);

            // 2. Chrome now foregrounded, but session is locked (WTS_SESSION_LOCK)
            await arbiter.OnForegroundWindowChangedAsync(new ForegroundChangedEventArgs(
                new IntPtr(100), 1234, "chrome.exe", "Google Chrome", true, t0.AddSeconds(10)));
            await arbiter.OnSessionLockedAsync();

            Assert.True(arbiter.IsTrackingPaused);

            var lockedResult = await scheduler.TriggerCaptureAsync();

            // Assert: Must be skipped immediately while locked
            Assert.Null(lockedResult);
            Assert.Empty(fakeIpc.SentMessages);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void TASK048_AntiCaptureSafeguards_Win32NativeDeclaresZeroScreenScrapingAPIs()
    {
        // Assert: Ensure no desktop screen capture APIs exist in the codebase
        var methods = typeof(Win32Native).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(m => m.Name).ToList();

        Assert.DoesNotContain("BitBlt", methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateCompatibleDC", methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateCompatibleBitmap", methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("GetDC", methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrintWindow", methods, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Direct3DCreate9", methods, StringComparer.OrdinalIgnoreCase);
    }
}
