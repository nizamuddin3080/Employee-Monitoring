using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using LocalActivityTracker.Coordinator.Api;
using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Screenshots;
using LocalActivityTracker.Core.Storage;
using Xunit;

namespace LocalActivityTracker.Tests;

/// <summary>
/// Milestone 9 Verification Suite:
/// Hardened Localhost Kestrel API Gateway, Security Middleware, Capability Token, CSRF Defense, and Endpoints.
/// </summary>
public class ApiGatewayAndSecurityTests
{
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

    private static async Task<(CoordinatorApiHost Host, StoragePathProvider Storage, ActivityRepository ActRepo, SettingsRepository SetRepo, ProfileRepository ProfRepo, LocalCapabilityTokenProvider TokenProvider, string BaseUrl, string TempDir)> CreateTestServerAsync(int port)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "LAT_ApiTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var storage = new StoragePathProvider(tempDir);
        storage.EnsureDirectoriesExist();

        var factory = new SqliteConnectionFactory(storage);
        var runner = new MigrationRunner(factory);
        await runner.ApplyMigrationsAsync();

        var actRepo = new ActivityRepository(factory);
        var setRepo = new SettingsRepository(factory);
        var profRepo = new ProfileRepository(factory);
        var arbiter = new ActivityArbiter(actRepo, new UrlSanitizer());
        var tokenProvider = new LocalCapabilityTokenProvider(storage);
        var scheduler = new ScreenshotScheduler(setRepo, arbiter, new NoopIpcServer(), actRepo, storage);

        var host = new CoordinatorApiHost(
            storage,
            factory,
            actRepo,
            profRepo,
            setRepo,
            arbiter,
            scheduler,
            tokenProvider,
            port);

        await host.StartAsync();

        return (host, storage, actRepo, setRepo, profRepo, tokenProvider, host.ServerUrl, tempDir);
    }

    [Fact]
    public async Task TASK049_HostHeaderCheck_RejectsForgedHostHeaders()
    {
        var (host, _, _, _, _, _, baseUrl, tempDir) = await CreateTestServerAsync(48121);
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/overview");
            request.Headers.Host = "attacker-domain.com";

            var response = await client.SendAsync(request);

            // Assert: Must be rejected with 400 Bad Request
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Host header rejected", content);
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK049_OriginHeaderCheck_RejectsUntrustedOrigins()
    {
        var (host, _, _, _, _, _, baseUrl, tempDir) = await CreateTestServerAsync(48122);
        try
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/overview");
            request.Headers.Host = "127.0.0.1:48122";
            request.Headers.Add("Origin", "http://malicious-site.com");

            var response = await client.SendAsync(request);

            // Assert: Must be rejected with 403 Forbidden
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("Origin rejected", content);
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK050_CsrfHeaderCheck_RejectsMutationWithoutCustomHeader()
    {
        var (host, _, _, _, _, tokenProv, baseUrl, tempDir) = await CreateTestServerAsync(48123);
        try
        {
            using var client = new HttpClient();
            var validToken = tokenProv.GetOrCreateToken();

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/settings");
            request.Headers.Host = "127.0.0.1:48123";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", validToken);
            request.Content = new StringContent("{\"retention_days\":\"60\"}", System.Text.Encoding.UTF8, "application/json");

            // Intentionally omit X-LAT-Requested-With header
            var response = await client.SendAsync(request);

            // Assert: Must be rejected with 403 Forbidden
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains("X-LAT-Requested-With", content);
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK050_CapabilityTokenCheck_EnforcesBearerTokenOnProtectedEndpoints()
    {
        var (host, _, _, _, _, _, baseUrl, tempDir) = await CreateTestServerAsync(48124);
        try
        {
            using var client = new HttpClient();

            // 1. Without Token -> 401 Unauthorized
            var unauthRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/overview");
            unauthRequest.Headers.Host = "127.0.0.1:48124";

            var unauthResponse = await client.SendAsync(unauthRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

            // 2. With Forged / Invalid Token -> 401 Unauthorized
            var forgedRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/overview");
            forgedRequest.Headers.Host = "127.0.0.1:48124";
            forgedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token-12345");

            var forgedResponse = await client.SendAsync(forgedRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);

            // 3. Local bootstrap endpoint /api/auth/token allows local token retrieval
            var bootstrapRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/auth/token");
            bootstrapRequest.Headers.Host = "127.0.0.1:48124";

            var bootstrapResponse = await client.SendAsync(bootstrapRequest);
            Assert.Equal(HttpStatusCode.OK, bootstrapResponse.StatusCode);

            var bootstrapJson = await bootstrapResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(bootstrapJson);
            var retrievedToken = doc.RootElement.GetProperty("token").GetString();

            Assert.NotNull(retrievedToken);
            Assert.Equal(64, retrievedToken.Length);

            // 4. Authenticated request with retrieved token -> 200 OK
            var authRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/overview");
            authRequest.Headers.Host = "127.0.0.1:48124";
            authRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", retrievedToken);

            var authResponse = await client.SendAsync(authRequest);
            Assert.Equal(HttpStatusCode.OK, authResponse.StatusCode);
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK051_TASK054_OverviewAndApplicationsEndpoints_ReturnValidDataWithoutWindowTitles()
    {
        var (host, _, actRepo, _, _, tokenProv, baseUrl, tempDir) = await CreateTestServerAsync(48125);
        try
        {
            using var client = new HttpClient();
            var token = tokenProv.GetOrCreateToken();

            // Insert non-Chrome application usage record
            var t0 = DateTime.UtcNow.AddMinutes(-30);
            await actRepo.OpenApplicationSessionAsync(new ApplicationSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ProcessName = "Code.exe",
                ApplicationDisplayName = "Visual Studio Code",
                StartTimeUtc = t0,
                EndTimeUtc = t0.AddMinutes(15),
                DurationSeconds = 900,
                IsCompleted = true,
                SessionStatus = "COMPLETED"
            });

            // 1. Test Overview Endpoint
            var overviewReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/overview");
            overviewReq.Headers.Host = "127.0.0.1:48125";
            overviewReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var overviewRes = await client.SendAsync(overviewReq);
            Assert.Equal(HttpStatusCode.OK, overviewRes.StatusCode);

            var overviewJson = await overviewRes.Content.ReadAsStringAsync();
            using var overviewDoc = JsonDocument.Parse(overviewJson);
            var nonChromeSeconds = overviewDoc.RootElement.GetProperty("non_chrome_app_seconds").GetInt32();
            Assert.Equal(900, nonChromeSeconds);

            // 2. Test Applications Endpoint
            var appReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/applications");
            appReq.Headers.Host = "127.0.0.1:48125";
            appReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var appRes = await client.SendAsync(appReq);
            Assert.Equal(HttpStatusCode.OK, appRes.StatusCode);

            var appJson = await appRes.Content.ReadAsStringAsync();

            // Strict privacy audit on HTTP JSON payload: ZERO window titles anywhere in response
            Assert.Contains("Code.exe", appJson);
            Assert.Contains("Visual Studio Code", appJson);
            Assert.DoesNotContain("window_title", appJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("caption", appJson, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task TASK056_SettingsEndpoint_UpdatesAndPersistsConfig()
    {
        var (host, _, _, setRepo, _, tokenProv, baseUrl, tempDir) = await CreateTestServerAsync(48126);
        try
        {
            using var client = new HttpClient();
            var token = tokenProv.GetOrCreateToken();

            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/settings");
            req.Headers.Host = "127.0.0.1:48126";
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("X-LAT-Requested-With", "LocalActivityTracker");
            req.Content = new StringContent("{\"screenshot_interval_seconds\":\"300\",\"retention_days\":\"180\"}", System.Text.Encoding.UTF8, "application/json");

            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            // Assert persistence in SQLite
            var interval = await setRepo.GetSettingAsync("screenshot_interval_seconds");
            var retention = await setRepo.GetSettingAsync("retention_days");

            Assert.Equal("300", interval);
            Assert.Equal("180", retention);
        }
        finally
        {
            await host.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
