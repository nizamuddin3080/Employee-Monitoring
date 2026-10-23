using System.Collections.Concurrent;
using System.Text.Json;
using LocalActivityTracker.Core.Ipc;
using LocalActivityTracker.Core.Models;
using LocalActivityTracker.Core.Privacy;
using LocalActivityTracker.Core.Repositories;
using LocalActivityTracker.Core.Win32;

namespace LocalActivityTracker.Core.Arbitration;

/// <summary>
/// Core duration arbitration engine.
/// Accrues active URL time strictly during the intersection of Chrome OS desktop focus and tab selection.
/// Accrues non-Chrome desktop application duration (strictly zero window titles) when external apps are foregrounded.
/// </summary>
public sealed class ActivityArbiter : IActivityArbiter
{
    private sealed record TabState(
        string ProfileId,
        int WindowId,
        int TabId,
        string RawUrl,
        string SanitizedUrl,
        string Domain,
        string PageTitle,
        DateTime TimestampUtc
    );

    private readonly IActivityRepository _activityRepository;
    private readonly IUrlSanitizer _urlSanitizer;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TabState> _activeTabsByProfile = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _heartbeatTimer;

    private bool _isChromeForeground;
    private bool _isTrackingPaused;
    private bool _isDisposed;
    private string? _currentChromeProfileId;

    private UrlActivitySession? _currentUrlSession;
    private BrowserSession? _currentBrowserSession;
    private ApplicationSession? _currentAppSession;

    public bool IsChromeForeground => _isChromeForeground;
    public bool IsTrackingPaused => _isTrackingPaused;
    public string? CurrentChromeProfileId => _currentChromeProfileId;
    public UrlActivitySession? CurrentUrlSession => _currentUrlSession;
    public BrowserSession? CurrentBrowserSession => _currentBrowserSession;
    public ApplicationSession? CurrentAppSession => _currentAppSession;

    public ActivityArbiter(
        IActivityRepository activityRepository,
        IUrlSanitizer? urlSanitizer = null,
        int heartbeatIntervalSeconds = 15)
    {
        _activityRepository = activityRepository ?? throw new ArgumentNullException(nameof(activityRepository));
        _urlSanitizer = urlSanitizer ?? new UrlSanitizer();

        _heartbeatTimer = new Timer(
            OnHeartbeatTick,
            null,
            TimeSpan.FromSeconds(heartbeatIntervalSeconds),
            TimeSpan.FromSeconds(heartbeatIntervalSeconds));
    }

    public async Task OnForegroundWindowChangedAsync(ForegroundChangedEventArgs e)
    {
        await _stateLock.WaitAsync();
        try
        {
            if (_isTrackingPaused)
            {
                return;
            }

            var isChrome = e.IsChrome || string.Equals(e.ProcessName, "chrome.exe", StringComparison.OrdinalIgnoreCase);

            if (isChrome)
            {
                // Chrome is now foregrounded on the Windows desktop
                _isChromeForeground = true;

                // 1. Close any non-Chrome application session that was running
                if (_currentAppSession != null)
                {
                    await CloseCurrentAppSessionAsync(e.TimestampUtc);
                }

                // 2. Open or resume browser session (Chrome overall foreground)
                if (_currentBrowserSession == null && !string.IsNullOrEmpty(_currentChromeProfileId))
                {
                    _currentBrowserSession = new BrowserSession
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        ProfileId = _currentChromeProfileId,
                        StartTimeUtc = e.TimestampUtc,
                        DurationSeconds = 0,
                        IsCompleted = false,
                        SessionStatus = "ACTIVE"
                    };
                    await _activityRepository.OpenBrowserSessionAsync(_currentBrowserSession);
                }

                // 3. Open or resume URL activity session for the active Chrome tab
                await TryOpenActiveUrlSessionAsync(e.TimestampUtc);
            }
            else
            {
                // Another Windows application is foregrounded (e.g. Code.exe, EXCEL.EXE)
                _isChromeForeground = false;

                // 1. Close active URL activity session immediately
                if (_currentUrlSession != null)
                {
                    await CloseCurrentUrlSessionAsync(e.TimestampUtc);
                }

                // 2. Close active Chrome browser session
                if (_currentBrowserSession != null)
                {
                    await CloseCurrentBrowserSessionAsync(e.TimestampUtc);
                }

                // 3. Close previous non-Chrome application session if the executable changed
                if (_currentAppSession != null && !_currentAppSession.ProcessName.Equals(e.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    await CloseCurrentAppSessionAsync(e.TimestampUtc);
                }

                // 4. Open new application session (strictly NO window title, strictly EXCLUDE chrome.exe)
                if (_currentAppSession == null &&
                    !string.IsNullOrWhiteSpace(e.ProcessName) &&
                    !e.ProcessName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase) &&
                    !e.ProcessName.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                {
                    var newAppSession = new ApplicationSession
                    {
                        SessionId = Guid.NewGuid().ToString("N"),
                        ApplicationDisplayName = e.ApplicationDisplayName,
                        ProcessName = e.ProcessName,
                        StartTimeUtc = e.TimestampUtc,
                        LastHeartbeatUtc = e.TimestampUtc,
                        DurationSeconds = 0,
                        IsCompleted = false,
                        SessionStatus = "ACTIVE"
                    };

                    await _activityRepository.OpenApplicationSessionAsync(newAppSession);
                    _currentAppSession = newAppSession;
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task OnIpcMessageReceivedAsync(string profileId, IpcMessage message)
    {
        await _stateLock.WaitAsync();
        try
        {
            switch (message.MessageType)
            {
                case IpcMessage.EventTabNavigated:
                    await HandleTabNavigatedAsync(profileId, message);
                    break;

                case IpcMessage.EventTabActivated:
                    await HandleTabActivatedAsync(profileId, message);
                    break;

                case IpcMessage.EventTabClosed:
                    await HandleTabClosedAsync(profileId, message);
                    break;

                case IpcMessage.EventWindowFocused:
                    await HandleWindowFocusedAsync(profileId, message);
                    break;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task HandleTabNavigatedAsync(string profileId, IpcMessage message)
    {
        if (message.Payload == null) return;

        var root = message.Payload.Value;
        var tabId = root.TryGetProperty("tab_id", out var tProp) ? tProp.GetInt32() : 0;
        var windowId = root.TryGetProperty("window_id", out var wProp) ? wProp.GetInt32() : 0;
        var rawUrl = root.TryGetProperty("raw_url", out var uProp) ? uProp.GetString() ?? string.Empty : string.Empty;
        var pageTitle = root.TryGetProperty("page_title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;

        var sanitized = _urlSanitizer.Sanitize(rawUrl);

        var tabState = new TabState(
            profileId,
            windowId,
            tabId,
            rawUrl,
            sanitized.SanitizedUrl,
            sanitized.Domain,
            pageTitle,
            message.TimestampUtc);

        _activeTabsByProfile[profileId] = tabState;
        _currentChromeProfileId = profileId;
        _isChromeForeground = true;

        // Close any non-Chrome application session since user is active in Chrome
        if (_currentAppSession != null)
        {
            await CloseCurrentAppSessionAsync(message.TimestampUtc);
        }

        if (_currentBrowserSession == null)
        {
            _currentBrowserSession = new BrowserSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ProfileId = profileId,
                StartTimeUtc = message.TimestampUtc,
                DurationSeconds = 0,
                IsCompleted = false,
                SessionStatus = "ACTIVE"
            };
            await _activityRepository.OpenBrowserSessionAsync(_currentBrowserSession);
        }

        // Record TabEvent audit
        await _activityRepository.InsertTabEventAsync(new TabEvent
        {
            ProfileId = profileId,
            SessionId = _currentUrlSession?.SessionId,
            ChromeTabId = tabId,
            EventType = "TAB_NAVIGATED",
            EventPayloadJson = JsonSerializer.Serialize(new { raw_url = rawUrl, sanitized_url = sanitized.SanitizedUrl }),
            EventTimeUtc = message.TimestampUtc
        });

        // If not paused, transition URL session
        if (!_isTrackingPaused)
        {
            if (_currentUrlSession != null)
            {
                if (_currentUrlSession.ChromeTabId == tabId &&
                    _currentUrlSession.SanitizedUrl.Equals(sanitized.SanitizedUrl, StringComparison.OrdinalIgnoreCase))
                {
                    // Same tab and URL already active; do nothing
                    return;
                }

                await CloseCurrentUrlSessionAsync(message.TimestampUtc);
            }

            await OpenUrlSessionInternalAsync(tabState, message.TimestampUtc);
        }
    }

    private async Task HandleTabActivatedAsync(string profileId, IpcMessage message)
    {
        if (message.Payload == null) return;

        var root = message.Payload.Value;
        var tabId = root.TryGetProperty("tab_id", out var tProp) ? tProp.GetInt32() : 0;
        var windowId = root.TryGetProperty("window_id", out var wProp) ? wProp.GetInt32() : 0;
        var rawUrl = root.TryGetProperty("raw_url", out var uProp) ? uProp.GetString() ?? string.Empty : string.Empty;
        var pageTitle = root.TryGetProperty("page_title", out var titleProp) ? titleProp.GetString() ?? string.Empty : string.Empty;

        var sanitized = _urlSanitizer.Sanitize(rawUrl);

        var tabState = new TabState(
            profileId,
            windowId,
            tabId,
            rawUrl,
            sanitized.SanitizedUrl,
            sanitized.Domain,
            pageTitle,
            message.TimestampUtc);

        _activeTabsByProfile[profileId] = tabState;
        _currentChromeProfileId = profileId;
        _isChromeForeground = true;

        // Close any non-Chrome application session since user is active in Chrome
        if (_currentAppSession != null)
        {
            await CloseCurrentAppSessionAsync(message.TimestampUtc);
        }

        if (_currentBrowserSession == null)
        {
            _currentBrowserSession = new BrowserSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ProfileId = profileId,
                StartTimeUtc = message.TimestampUtc,
                DurationSeconds = 0,
                IsCompleted = false,
                SessionStatus = "ACTIVE"
            };
            await _activityRepository.OpenBrowserSessionAsync(_currentBrowserSession);
        }

        // Record TabEvent audit
        await _activityRepository.InsertTabEventAsync(new TabEvent
        {
            ProfileId = profileId,
            SessionId = _currentUrlSession?.SessionId,
            ChromeTabId = tabId,
            EventType = "TAB_ACTIVATED",
            EventPayloadJson = JsonSerializer.Serialize(new { tab_id = tabId, window_id = windowId }),
            EventTimeUtc = message.TimestampUtc
        });

        if (!_isTrackingPaused)
        {
            if (_currentUrlSession != null)
            {
                if (_currentUrlSession.ChromeTabId == tabId &&
                    _currentUrlSession.SanitizedUrl.Equals(sanitized.SanitizedUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await CloseCurrentUrlSessionAsync(message.TimestampUtc);
            }

            await OpenUrlSessionInternalAsync(tabState, message.TimestampUtc);
        }
    }

    private async Task HandleTabClosedAsync(string profileId, IpcMessage message)
    {
        if (message.Payload == null) return;

        var root = message.Payload.Value;
        var tabId = root.TryGetProperty("tab_id", out var tProp) ? tProp.GetInt32() : 0;

        await _activityRepository.InsertTabEventAsync(new TabEvent
        {
            ProfileId = profileId,
            SessionId = _currentUrlSession?.SessionId,
            ChromeTabId = tabId,
            EventType = "TAB_CLOSED",
            EventPayloadJson = null,
            EventTimeUtc = message.TimestampUtc
        });

        if (_currentUrlSession != null && _currentUrlSession.ChromeTabId == tabId && _currentUrlSession.ProfileId == profileId)
        {
            await CloseCurrentUrlSessionAsync(message.TimestampUtc);
        }

        if (_activeTabsByProfile.TryGetValue(profileId, out var state) && state.TabId == tabId)
        {
            _activeTabsByProfile.TryRemove(profileId, out _);
        }
    }

    private async Task HandleWindowFocusedAsync(string profileId, IpcMessage message)
    {
        var windowId = 0;
        if (message.Payload.HasValue && message.Payload.Value.TryGetProperty("window_id", out var wProp))
        {
            windowId = wProp.GetInt32();
        }

        if (windowId == -1)
        {
            // Chrome lost focus
            _isChromeForeground = false;
            if (_currentUrlSession != null)
            {
                await CloseCurrentUrlSessionAsync(message.TimestampUtc);
            }
            if (_currentBrowserSession != null)
            {
                await CloseCurrentBrowserSessionAsync(message.TimestampUtc);
            }
        }
        else
        {
            // Chrome gained focus
            _isChromeForeground = true;
            _currentChromeProfileId = profileId;

            if (_currentAppSession != null)
            {
                await CloseCurrentAppSessionAsync(message.TimestampUtc);
            }

            if (_currentBrowserSession == null)
            {
                _currentBrowserSession = new BrowserSession
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    ProfileId = profileId,
                    StartTimeUtc = message.TimestampUtc,
                    DurationSeconds = 0,
                    IsCompleted = false,
                    SessionStatus = "ACTIVE"
                };
                await _activityRepository.OpenBrowserSessionAsync(_currentBrowserSession);
            }

            if (!_isTrackingPaused)
            {
                await TryOpenActiveUrlSessionAsync(message.TimestampUtc);
            }
        }
    }

    private async Task TryOpenActiveUrlSessionAsync(DateTime timestampUtc)
    {
        if (_currentUrlSession != null)
        {
            return;
        }

        // Look for the active tab of the current profile (or any active tab)
        TabState? targetState = null;
        if (_currentChromeProfileId != null && _activeTabsByProfile.TryGetValue(_currentChromeProfileId, out var active))
        {
            targetState = active;
        }
        else if (!_activeTabsByProfile.IsEmpty)
        {
            targetState = _activeTabsByProfile.Values.FirstOrDefault();
        }

        if (targetState != null)
        {
            await OpenUrlSessionInternalAsync(targetState, timestampUtc);
        }
    }

    private async Task OpenUrlSessionInternalAsync(TabState state, DateTime timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(state.SanitizedUrl))
        {
            return;
        }

        var newSession = new UrlActivitySession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            ProfileId = state.ProfileId,
            ChromeTabId = state.TabId,
            Url = state.RawUrl,
            SanitizedUrl = state.SanitizedUrl,
            Domain = state.Domain,
            PageTitle = state.PageTitle,
            StartTimeUtc = timestampUtc,
            LastHeartbeatUtc = timestampUtc,
            ActiveDurationSeconds = 0,
            IsCompleted = false,
            SessionStatus = "ACTIVE"
        };

        await _activityRepository.OpenUrlSessionAsync(newSession);
        _currentUrlSession = newSession;

        if (_currentBrowserSession == null)
        {
            _currentBrowserSession = new BrowserSession
            {
                SessionId = Guid.NewGuid().ToString("N"),
                ProfileId = state.ProfileId,
                StartTimeUtc = timestampUtc,
                DurationSeconds = 0,
                IsCompleted = false,
                SessionStatus = "ACTIVE"
            };
            await _activityRepository.OpenBrowserSessionAsync(_currentBrowserSession);
        }
    }

    private async Task CloseCurrentUrlSessionAsync(DateTime endTimeUtc)
    {
        if (_currentUrlSession == null)
        {
            return;
        }

        var duration = Math.Max(0, (int)(endTimeUtc - _currentUrlSession.StartTimeUtc).TotalSeconds);
        await _activityRepository.CloseUrlSessionAsync(_currentUrlSession.SessionId, endTimeUtc, duration);
        _currentUrlSession = null;
    }

    private async Task CloseCurrentBrowserSessionAsync(DateTime endTimeUtc)
    {
        if (_currentBrowserSession == null)
        {
            return;
        }

        var duration = Math.Max(0, (int)(endTimeUtc - _currentBrowserSession.StartTimeUtc).TotalSeconds);
        await _activityRepository.CloseBrowserSessionAsync(_currentBrowserSession.SessionId, endTimeUtc, duration);
        _currentBrowserSession = null;
    }

    private async Task CloseCurrentAppSessionAsync(DateTime endTimeUtc)
    {
        if (_currentAppSession == null)
        {
            return;
        }

        var duration = Math.Max(0, (int)(endTimeUtc - _currentAppSession.StartTimeUtc).TotalSeconds);
        await _activityRepository.CloseApplicationSessionAsync(_currentAppSession.SessionId, endTimeUtc, duration);
        _currentAppSession = null;
    }

    public async Task OnSessionLockedAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _isTrackingPaused = true;
            var now = DateTime.UtcNow;
            await CloseCurrentUrlSessionAsync(now);
            await CloseCurrentBrowserSessionAsync(now);
            await CloseCurrentAppSessionAsync(now);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task OnSessionUnlockedAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            _isTrackingPaused = false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    public async Task OnSuspendedAsync()
    {
        await OnSessionLockedAsync();
    }

    public async Task OnResumedAsync()
    {
        await OnSessionUnlockedAsync();
    }

    private void OnHeartbeatTick(object? state)
    {
        _ = Task.Run(async () =>
        {
            if (_isTrackingPaused || _isDisposed)
            {
                return;
            }

            await _stateLock.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;

                if (_isChromeForeground && _currentUrlSession != null)
                {
                    await _activityRepository.UpdateUrlSessionHeartbeatAsync(_currentUrlSession.SessionId, now);
                }
                else if (!_isChromeForeground && _currentAppSession != null)
                {
                    await _activityRepository.UpdateApplicationSessionHeartbeatAsync(_currentAppSession.SessionId, now);
                }
            }
            catch
            {
                // Suppress heartbeat tick errors
            }
            finally
            {
                _stateLock.Release();
            }
        });
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _heartbeatTimer.Dispose();

        _stateLock.Wait();
        try
        {
            var now = DateTime.UtcNow;
            if (_currentUrlSession != null)
            {
                CloseCurrentUrlSessionAsync(now).GetAwaiter().GetResult();
            }
            if (_currentBrowserSession != null)
            {
                CloseCurrentBrowserSessionAsync(now).GetAwaiter().GetResult();
            }
            if (_currentAppSession != null)
            {
                CloseCurrentAppSessionAsync(now).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Suppress disposal errors
        }
        finally
        {
            _stateLock.Release();
            _stateLock.Dispose();
        }
    }
}
