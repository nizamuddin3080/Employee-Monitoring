using System.Runtime.InteropServices;

namespace LocalActivityTracker.Core.Win32;

/// <summary>
/// Listens for Windows session lock/unlock (WTSRegisterSessionNotification) and system power events (WM_POWERBROADCAST)
/// via a hidden native top-level window in the interactive desktop user session.
/// </summary>
public sealed class SessionNotificationListener : ISessionNotificationListener
{
    private readonly object _lock = new();
    private readonly Win32Native.WndProc _wndProcDelegate;
    private readonly ManualResetEventSlim _startedSignal = new(false);

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd = IntPtr.Zero;
    private string _className = string.Empty;
    private IntPtr _hInstance = IntPtr.Zero;

    private bool _isListening;
    private bool _isDisposed;

    public event EventHandler? SessionLocked;
    public event EventHandler? SessionUnlocked;
    public event EventHandler? Suspended;
    public event EventHandler? Resumed;

    public bool IsListening => _isListening;

    public SessionNotificationListener()
    {
        // Retain delegate instance to prevent garbage collection
        _wndProcDelegate = WindowProcedure;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(SessionNotificationListener));
            }

            if (_isListening)
            {
                return;
            }

            _startedSignal.Reset();
            _thread = new Thread(RunWindowThread)
            {
                IsBackground = true,
                Name = "LAT-SessionListener-Thread"
            };

            _thread.Start();

            if (!_startedSignal.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out waiting for session notification window to initialize.");
            }

            _isListening = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isListening)
            {
                return;
            }

            _isListening = false;

            if (_hwnd != IntPtr.Zero)
            {
                Win32Native.WTSUnRegisterSessionNotification(_hwnd);
                Win32Native.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }

            if (!string.IsNullOrEmpty(_className) && _hInstance != IntPtr.Zero)
            {
                Win32Native.UnregisterClass(_className, _hInstance);
            }

            if (_threadId != 0)
            {
                Win32Native.PostThreadMessage(_threadId, Win32Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }

            if (_thread != null && _thread.IsAlive)
            {
                _thread.Join(2000);
                _thread = null;
            }

            _threadId = 0;
        }
    }

    private void RunWindowThread()
    {
        _threadId = Win32Native.GetCurrentThreadId();
        _hInstance = Win32Native.GetModuleHandle(null);
        _className = "LAT_SessionListenerClass_" + Guid.NewGuid().ToString("N");

        var wndClass = new Win32Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32Native.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _wndProcDelegate,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = _hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = _className,
            hIconSm = IntPtr.Zero
        };

        var regResult = Win32Native.RegisterClassEx(ref wndClass);
        if (regResult == 0)
        {
            _startedSignal.Set();
            return;
        }

        // Top-level popup window (hidden) is required by Windows to receive broadcast session/power messages
        _hwnd = Win32Native.CreateWindowEx(
            0,
            _className,
            "LocalActivityTracker_SessionListenerWindow",
            Win32Native.WS_POPUP,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            _hInstance,
            IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            Win32Native.WTSRegisterSessionNotification(_hwnd, Win32Native.NOTIFY_FOR_THIS_SESSION);
        }

        _startedSignal.Set();

        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        while (Win32Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            Win32Native.TranslateMessage(ref msg);
            Win32Native.DispatchMessage(ref msg);
        }
    }

    private IntPtr WindowProcedure(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Win32Native.WM_WTSSESSION_CHANGE:
                HandleSessionChange((uint)wParam.ToInt64());
                return IntPtr.Zero;

            case Win32Native.WM_POWERBROADCAST:
                HandlePowerBroadcast((uint)wParam.ToInt64());
                return new IntPtr(1);

            case Win32Native.WM_DESTROY:
                Win32Native.PostQuitMessage(0);
                return IntPtr.Zero;

            default:
                return Win32Native.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }

    private void HandleSessionChange(uint notificationCode)
    {
        switch (notificationCode)
        {
            case Win32Native.WTS_SESSION_LOCK:
                SessionLocked?.Invoke(this, EventArgs.Empty);
                break;

            case Win32Native.WTS_SESSION_UNLOCK:
                SessionUnlocked?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void HandlePowerBroadcast(uint eventCode)
    {
        switch (eventCode)
        {
            case Win32Native.PBT_APMSUSPEND:
                Suspended?.Invoke(this, EventArgs.Empty);
                break;

            case Win32Native.PBT_APMRESUMESUSPEND:
            case Win32Native.PBT_APMRESUMEAUTOMATIC:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
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
