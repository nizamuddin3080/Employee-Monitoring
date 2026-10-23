using System.Diagnostics;

namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Event-driven monitor for Windows desktop foreground window activations.
/// Executes SetWinEventHook(EVENT_SYSTEM_FOREGROUND) on a dedicated message loop thread in the interactive desktop session (Session 1+).
/// </summary>
public sealed class FocusMonitor : IFocusMonitor
{
    private readonly IProcessInfoResolver _resolver;
    private readonly Win32Native.WinEventDelegate _winEventProc;
    private readonly object _lock = new();

    private IntPtr _hookHandle = IntPtr.Zero;
    private Thread? _messageLoopThread;
    private uint _messageLoopThreadId;
    private readonly ManualResetEventSlim _startedSignal = new(false);
    private bool _isDisposed;
    private bool _isRunning;

    private IntPtr _lastReportedHwnd = IntPtr.Zero;
    private long _lastReportedTick;

    public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

    public bool IsRunning => _isRunning;

    public FocusMonitor(IProcessInfoResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        // Keep delegate reference alive to prevent GC reclamation while native hook is active
        _winEventProc = OnWinEvent;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(FocusMonitor));
            }

            if (_isRunning)
            {
                return;
            }

            _startedSignal.Reset();
            _messageLoopThread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "LAT-FocusMonitor-Thread"
            };

            _messageLoopThread.Start();

            if (!_startedSignal.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out waiting for Win32 focus hook message loop to initialize.");
            }

            _isRunning = true;
        }

        // Fire initial focus state immediately on start
        var current = GetCurrentForeground();
        if (current != null)
        {
            _lastReportedHwnd = current.WindowHandle;
            _lastReportedTick = Environment.TickCount64;
            ForegroundChanged?.Invoke(this, current);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            if (_hookHandle != IntPtr.Zero)
            {
                Win32Native.UnhookWinEvent(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }

            if (_messageLoopThreadId != 0)
            {
                Win32Native.PostThreadMessage(_messageLoopThreadId, Win32Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            if (_messageLoopThread != null && _messageLoopThread.IsAlive)
            {
                _messageLoopThread.Join(2000);
                _messageLoopThread = null;
            }

            _messageLoopThreadId = 0;
        }
    }

    public ForegroundChangedEventArgs? GetCurrentForeground()
    {
        var hWnd = Win32Native.GetForegroundWindow();
        if (hWnd == IntPtr.Zero)
        {
            return null;
        }

        var info = _resolver.Resolve(hWnd);
        var processName = info?.ProcessName ?? "unknown";
        var displayName = info?.ApplicationDisplayName ?? "Unknown Application";
        var isChrome = _resolver.IsChromeProcess(processName);

        return new ForegroundChangedEventArgs(
            hWnd,
            info?.ProcessId ?? 0,
            processName,
            displayName,
            isChrome,
            DateTime.UtcNow);
    }

    private void RunMessageLoop()
    {
        _messageLoopThreadId = Win32Native.GetCurrentThreadId();

        // Install out-of-context hook for EVENT_SYSTEM_FOREGROUND across all desktop processes
        _hookHandle = Win32Native.SetWinEventHook(
            Win32Native.EVENT_SYSTEM_FOREGROUND,
            Win32Native.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            0,
            0,
            Win32Native.WINEVENT_OUTOFCONTEXT | Win32Native.WINEVENT_SKIPOWNPROCESS);

        _startedSignal.Set();

        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        // Standard Win32 message pump required for out-of-context hooks
        while (Win32Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32Native.TranslateMessage(ref msg);
            Win32Native.DispatchMessage(ref msg);
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType != Win32Native.EVENT_SYSTEM_FOREGROUND || hWnd == IntPtr.Zero)
        {
            return;
        }

        var currentTick = Environment.TickCount64;
        if (_lastReportedHwnd == hWnd && (currentTick - _lastReportedTick) < 50)
        {
            // Debounce rapid duplicate events for identical window handle
            return;
        }

        _lastReportedHwnd = hWnd;
        _lastReportedTick = currentTick;

        try
        {
            var info = _resolver.Resolve(hWnd);
            var processName = info?.ProcessName ?? "unknown";
            var displayName = info?.ApplicationDisplayName ?? "Unknown Application";
            var isChrome = _resolver.IsChromeProcess(processName);

            var args = new ForegroundChangedEventArgs(
                hWnd,
                info?.ProcessId ?? 0,
                processName,
                displayName,
                isChrome,
                DateTime.UtcNow);

            ForegroundChanged?.Invoke(this, args);
        }
        catch
        {
            // Suppress callback exceptions to keep the native hook and message pump healthy
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
        _startedSignal.Dispose();
    }
}
