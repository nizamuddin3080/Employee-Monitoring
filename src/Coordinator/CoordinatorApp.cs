using System.Diagnostics;
using LocalActivityTracker.Coordinator.Api;
using LocalActivityTracker.Core.Arbitration;
using LocalActivityTracker.Core.Database;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Screenshots;
using LocalActivityTracker.Core.Storage;
using LocalActivityTracker.Core.Win32;
using LocalActivityTracker.Core.Win32.SystemTray;

namespace LocalActivityTracker.Coordinator;

/// <summary>
/// Main application host for the Interactive User-Session Coordinator.
/// Executes in the active Windows interactive user session (Session 1+).
/// Orchestrates database initialization, Named Pipe IPC server, Win32 focus monitoring, session lock/power listeners, duration arbitration, and system tray presence.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class CoordinatorApp : IDisposable
{
    private readonly StoragePathProvider _storage;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly MigrationRunner _migrationRunner;
    private readonly IProfileRepository _profileRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IActivityRepository _activityRepository;
    private readonly IProcessInfoResolver _processInfoResolver;
    private readonly IFocusMonitor _focusMonitor;
    private readonly ISessionNotificationListener _sessionListener;
    private readonly ITrayIconManager _trayIconManager;
    private readonly INamedPipeIpcServer _ipcServer;
    private readonly IActivityArbiter _arbiter;
    private readonly IScreenshotScheduler _screenshotScheduler;
    private readonly CoordinatorApiHost _apiHost;
    private readonly bool _enableTray;

    private readonly object _lock = new();
    private string _logFilePath = string.Empty;
    private bool _isRunning;
    private bool _isTrackingPaused;
    private bool _isDisposed;

    public bool IsRunning => _isRunning;
    public bool IsTrackingPaused => _isTrackingPaused;
    public int SessionId { get; }
    public INamedPipeIpcServer IpcServer => _ipcServer;
    public IActivityArbiter Arbiter => _arbiter;
    public IScreenshotScheduler ScreenshotScheduler => _screenshotScheduler;
    public CoordinatorApiHost ApiHost => _apiHost;

    public CoordinatorApp(
        StoragePathProvider? storage = null,
        ISqliteConnectionFactory? connectionFactory = null,
        IProcessInfoResolver? processInfoResolver = null,
        IFocusMonitor? focusMonitor = null,
        ISessionNotificationListener? sessionListener = null,
        ITrayIconManager? trayIconManager = null,
        INamedPipeIpcServer? ipcServer = null,
        IActivityArbiter? arbiter = null,
        IScreenshotScheduler? screenshotScheduler = null,
        CoordinatorApiHost? apiHost = null,
        bool enableTray = true)
    {
        SessionId = Process.GetCurrentProcess().SessionId;
        _storage = storage ?? new StoragePathProvider();
        _connectionFactory = connectionFactory ?? new SqliteConnectionFactory(_storage);
        _migrationRunner = new MigrationRunner(_connectionFactory);
        _profileRepository = new ProfileRepository(_connectionFactory);
        _settingsRepository = new SettingsRepository(_connectionFactory);
        _activityRepository = new ActivityRepository(_connectionFactory);
        _processInfoResolver = processInfoResolver ?? new ProcessInfoResolver();
        _focusMonitor = focusMonitor ?? new FocusMonitor(_processInfoResolver);
        _sessionListener = sessionListener ?? new SessionNotificationListener();
        _trayIconManager = trayIconManager ?? new Win32TrayIconManager();
        _ipcServer = ipcServer ?? new NamedPipeIpcServer(sessionId: SessionId);
        _arbiter = arbiter ?? new ActivityArbiter(_activityRepository, new UrlSanitizer());
        _screenshotScheduler = screenshotScheduler ?? new ScreenshotScheduler(
            _settingsRepository,
            _arbiter,
            _ipcServer,
            _activityRepository,
            _storage,
            (level, msg) => WriteLog(level, msg));
        _apiHost = apiHost ?? new CoordinatorApiHost(
            _storage,
            _connectionFactory,
            _activityRepository,
            _profileRepository,
            _settingsRepository,
            _arbiter,
            _screenshotScheduler);
        _enableTray = enableTray;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(CoordinatorApp));
            }

            if (_isRunning)
            {
                return;
            }

            // ADR-001 Check: Coordinator must never execute in Session 0
            if (SessionId == 0)
            {
                var errorMsg = "CRITICAL: LocalActivityTracker Coordinator detected execution in Session 0. " +
                               "The Coordinator must execute within the logged-in user's interactive desktop session (Session 1+) " +
                               "to access Win32 foreground desktop hooks and session notifications.";
                LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            _isRunning = true;
        }

        // 0. Ensure all storage directories exist
        _storage.EnsureDirectoriesExist();

        // Initialize log file
        _logFilePath = Path.Combine(_storage.LogsDirectory, $"coordinator_{DateTime.UtcNow:yyyy-MM-dd}.log");
        LogInfo($"Starting Interactive Coordinator in Windows Session {SessionId} (PID: {Environment.ProcessId})...");

        // 1. Run database migrations
        LogInfo("Applying SQLite database migrations...");
        await _migrationRunner.ApplyMigrationsAsync(cancellationToken);
        LogInfo("Database schema verified and up to date.");

        // 2. Perform crash recovery for unclosed dangling sessions from prior runs
        LogInfo("Checking for unclosed sessions from previous unexpected shutdown...");
        var recoveredSessions = await _activityRepository.RecoverDanglingSessionsAsync(cancellationToken);
        if (recoveredSessions > 0)
        {
            LogInfo($"Crash recovery completed: reconciled {recoveredSessions} unclosed session(s) marked RECOVERED_AFTER_CRASH.");
        }
        else
        {
            LogInfo("Crash recovery clean: zero dangling sessions found.");
        }

        // 3. Attach session and power notification listeners
        _sessionListener.SessionLocked += OnSessionLocked;
        _sessionListener.SessionUnlocked += OnSessionUnlocked;
        _sessionListener.Suspended += OnSystemSuspended;
        _sessionListener.Resumed += OnSystemResumed;

        try
        {
            _sessionListener.Start();
            LogInfo("Session notification listener started successfully (WTS_SESSION_LOCK / UNLOCK).");
        }
        catch (Exception ex)
        {
            LogWarning($"Session notification listener failed to initialize: {ex.Message}");
        }

        // 4. Attach Win32 focus monitor
        _focusMonitor.ForegroundChanged += OnForegroundChanged;

        try
        {
            _focusMonitor.Start();
            LogInfo("Win32 desktop focus monitor started successfully (SetWinEventHook EVENT_SYSTEM_FOREGROUND).");

            // Capture initial foreground window immediately at startup
            var initialForeground = _focusMonitor.GetCurrentForeground();
            if (initialForeground != null)
            {
                OnForegroundChanged(this, initialForeground);
            }
        }
        catch (Exception ex)
        {
            LogError($"Win32 desktop focus monitor failed to initialize: {ex.Message}");
            throw;
        }

        // 5. Start Named Pipe IPC Server
        _ipcServer.ClientConnected += OnClientConnected;
        _ipcServer.ClientDisconnected += OnClientDisconnected;
        _ipcServer.MessageReceived += OnIpcMessageReceived;

        try
        {
            await _ipcServer.StartAsync(cancellationToken);
            LogInfo($"Named Pipe IPC server started on \\\\.\\pipe\\{_ipcServer.PipeName}");
        }
        catch (Exception ex)
        {
            LogError($"Named Pipe IPC server failed to initialize: {ex.Message}");
            throw;
        }

        // 6. Start Screenshot Scheduler
        try
        {
            _screenshotScheduler.Start();
            LogInfo("Targeted screenshot scheduler started.");
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to start screenshot scheduler: {ex.Message}");
        }

        // 7. Start Hardened Localhost REST API & Dashboard Server
        try
        {
            await _apiHost.StartAsync(cancellationToken);
            LogInfo($"Hardened localhost REST API and Dashboard hosted at {_apiHost.ServerUrl}");
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to start Localhost REST API: {ex.Message}");
        }

        // 8. Initialize System Tray if enabled
        if (_enableTray)
        {
            try
            {
                _trayIconManager.Initialize($"Local Activity Tracker - Active (Session {SessionId})");
                _trayIconManager.OpenDashboardRequested += OnOpenDashboardRequested;
                LogInfo("System tray icon initialized.");
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to initialize system tray icon: {ex.Message}");
            }
        }

        LogInfo("Interactive Coordinator is actively running.");
    }

    public async Task StopAsync()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
        }

        LogInfo("Shutting down Interactive Coordinator...");

        try
        {
            _focusMonitor.ForegroundChanged -= OnForegroundChanged;
            _focusMonitor.Stop();
            LogInfo("Focus monitor stopped.");
        }
        catch (Exception ex)
        {
            LogWarning($"Error stopping focus monitor: {ex.Message}");
        }

        try
        {
            _sessionListener.SessionLocked -= OnSessionLocked;
            _sessionListener.SessionUnlocked -= OnSessionUnlocked;
            _sessionListener.Suspended -= OnSystemSuspended;
            _sessionListener.Resumed -= OnSystemResumed;
            _sessionListener.Stop();
            LogInfo("Session listener stopped.");
        }
        catch (Exception ex)
        {
            LogWarning($"Error stopping session listener: {ex.Message}");
        }

        try
        {
            _ipcServer.ClientConnected -= OnClientConnected;
            _ipcServer.ClientDisconnected -= OnClientDisconnected;
            _ipcServer.MessageReceived -= OnIpcMessageReceived;
            await _ipcServer.StopAsync();
            LogInfo("Named Pipe IPC server stopped.");
        }
        catch (Exception ex)
        {
            LogWarning($"Error stopping Named Pipe IPC server: {ex.Message}");
        }

        try
        {
            _screenshotScheduler.Stop();
            LogInfo("Screenshot scheduler stopped.");
        }
        catch (Exception ex)
        {
            LogWarning($"Error stopping screenshot scheduler: {ex.Message}");
        }

        try
        {
            await _apiHost.StopAsync(CancellationToken.None);
            LogInfo("Localhost REST API server stopped.");
        }
        catch (Exception ex)
        {
            LogWarning($"Error stopping Localhost REST API: {ex.Message}");
        }

        if (_enableTray)
        {
            try
            {
                _trayIconManager.OpenDashboardRequested -= OnOpenDashboardRequested;
                _trayIconManager.Remove();
                LogInfo("System tray icon removed.");
            }
            catch (Exception ex)
            {
                LogWarning($"Error removing tray icon: {ex.Message}");
            }
        }

        LogInfo("Interactive Coordinator shutdown complete.");
    }

    private void OnForegroundChanged(object? sender, ForegroundChangedEventArgs e)
    {
        if (_isTrackingPaused)
        {
            return;
        }

        var appType = e.IsChrome ? "Chrome Browser" : "Desktop Application";
        LogInfo($"[FOCUS] HWND: 0x{e.WindowHandle.ToInt64():X8} | App: {e.ApplicationDisplayName} ({e.ProcessName}) | PID: {e.ProcessId} | Type: {appType}");
        _ = _arbiter.OnForegroundWindowChangedAsync(e);
    }

    private void OnSessionLocked(object? sender, EventArgs e)
    {
        _isTrackingPaused = true;
        LogInfo("[SESSION] Windows session locked (WTS_SESSION_LOCK). Pausing active tracking.");
        _trayIconManager.UpdateTooltip($"Local Activity Tracker - Paused / Locked (Session {SessionId})");
        _ = _arbiter.OnSessionLockedAsync();
    }

    private void OnSessionUnlocked(object? sender, EventArgs e)
    {
        _isTrackingPaused = false;
        LogInfo("[SESSION] Windows session unlocked (WTS_SESSION_UNLOCK). Resuming active tracking.");
        _trayIconManager.UpdateTooltip($"Local Activity Tracker - Active (Session {SessionId})");
        _ = _arbiter.OnSessionUnlockedAsync();

        // Re-evaluate current foreground window immediately after unlock
        var current = _focusMonitor.GetCurrentForeground();
        if (current != null)
        {
            OnForegroundChanged(this, current);
        }
    }

    private void OnSystemSuspended(object? sender, EventArgs e)
    {
        _isTrackingPaused = true;
        LogInfo("[POWER] System entering sleep/suspend. Pausing active tracking.");
        _ = _arbiter.OnSuspendedAsync();
    }

    private void OnSystemResumed(object? sender, EventArgs e)
    {
        _isTrackingPaused = false;
        LogInfo("[POWER] System resumed from sleep/suspend. Resuming active tracking.");
        _ = _arbiter.OnResumedAsync();

        var current = _focusMonitor.GetCurrentForeground();
        if (current != null)
        {
            OnForegroundChanged(this, current);
        }
    }

    private void OnOpenDashboardRequested(object? sender, EventArgs e)
    {
        LogInfo("User requested dashboard from system tray. Opening browser...");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "http://127.0.0.1:48120",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            LogWarning($"Failed to open dashboard URL: {ex.Message}");
        }
    }

    private void OnClientConnected(object? sender, ClientConnectedEventArgs e)
    {
        LogInfo($"[IPC] Profile '{e.ProfileId}' connected from Native Host PID {e.ClientPid} (Name: {e.Handshake.ProfileName}, Ext: v{e.Handshake.ExtensionVersion})");
        _ = Task.Run(async () =>
        {
            try
            {
                await _profileRepository.UpsertProfileAsync(new ChromeProfile
                {
                    ProfileId = e.ProfileId,
                    ProfileName = e.Handshake.ProfileName,
                    IsEnrolled = true,
                    ConnectionStatus = "CONNECTED",
                    LastSeenUtc = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to record connected profile '{e.ProfileId}': {ex.Message}");
            }
        });
    }

    private void OnClientDisconnected(object? sender, ClientDisconnectedEventArgs e)
    {
        LogInfo($"[IPC] Profile '{e.ProfileId}' disconnected.");
        _ = Task.Run(async () =>
        {
            try
            {
                var existing = await _profileRepository.GetProfileByIdAsync(e.ProfileId);
                if (existing != null)
                {
                    existing.ConnectionStatus = "DISCONNECTED";
                    existing.LastSeenUtc = DateTime.UtcNow;
                    await _profileRepository.UpsertProfileAsync(existing);
                }
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to update disconnected profile '{e.ProfileId}': {ex.Message}");
            }
        });
    }

    private void OnIpcMessageReceived(object? sender, IpcMessageReceivedEventArgs e)
    {
        LogInfo($"[IPC] Received {e.Message.MessageType} from Profile '{e.ProfileId}' (CorrId: {e.Message.CorrelationId})");
        _ = _arbiter.OnIpcMessageReceivedAsync(e.ProfileId, e.Message);
    }

    private void LogInfo(string message) => WriteLog("INFO", message);
    private void LogWarning(string message) => WriteLog("WARN", message);
    private void LogError(string message) => WriteLog("ERROR", message);

    private void WriteLog(string level, string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fffZ}] [{level}] {message}";
        Console.WriteLine(line);

        if (!string.IsNullOrEmpty(_logFilePath))
        {
            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Suppress log file write errors
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopAsync().GetAwaiter().GetResult();
        _focusMonitor.Dispose();
        _sessionListener.Dispose();
        _trayIconManager.Dispose();
        _ipcServer.Dispose();
        _arbiter.Dispose();
        _screenshotScheduler.Dispose();
        _apiHost.DisposeAsync().GetAwaiter().GetResult();
    }
}
