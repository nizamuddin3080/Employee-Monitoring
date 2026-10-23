using System.Runtime.InteropServices;

namespace LocalActivityTracker.Core.Win32.SystemTray;

/// <summary>
/// Pure Win32 Shell_NotifyIcon implementation for system tray presence in the interactive user desktop session.
/// Does not depend on Windows Forms or WPF runtimes.
/// </summary>
public sealed class Win32TrayIconManager : ITrayIconManager
{
    private const uint TRAY_ICON_ID = 1001;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private readonly object _lock = new();
    private readonly Win32Native.WndProc _wndProcDelegate;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hwnd = IntPtr.Zero;
    private string _className = string.Empty;
    private IntPtr _hInstance = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;

    private bool _isInitialized;
    private bool _isDisposed;
    private string _currentTooltip = "Local Activity Tracker";

    public event EventHandler? OpenDashboardRequested;
    public event EventHandler? ExitRequested;

    public void TriggerExit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    public Win32TrayIconManager()
    {
        _wndProcDelegate = TrayWindowProcedure;
    }

    public void Initialize(string initialTooltip)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(Win32TrayIconManager));
            }

            if (_isInitialized)
            {
                return;
            }

            _currentTooltip = initialTooltip;

            var startedSignal = new ManualResetEventSlim(false);
            _thread = new Thread(() => RunTrayThread(startedSignal))
            {
                IsBackground = true,
                Name = "LAT-TrayIcon-Thread"
            };

            _thread.Start();
            startedSignal.Wait(TimeSpan.FromSeconds(5));
            _isInitialized = true;
        }
    }

    public void UpdateTooltip(string tooltip)
    {
        lock (_lock)
        {
            if (!_isInitialized || _hwnd == IntPtr.Zero)
            {
                return;
            }

            _currentTooltip = tooltip;

            var nid = new Win32Native.NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<Win32Native.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TRAY_ICON_ID,
                uFlags = Win32Native.NIF_TIP,
                szTip = TruncateTooltip(tooltip)
            };

            Win32Native.Shell_NotifyIcon(Win32Native.NIM_MODIFY, ref nid);
        }
    }

    public void Remove()
    {
        lock (_lock)
        {
            if (!_isInitialized)
            {
                return;
            }

            _isInitialized = false;

            if (_hwnd != IntPtr.Zero)
            {
                var nid = new Win32Native.NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<Win32Native.NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = TRAY_ICON_ID
                };

                Win32Native.Shell_NotifyIcon(Win32Native.NIM_DELETE, ref nid);
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

    private void RunTrayThread(ManualResetEventSlim startedSignal)
    {
        _threadId = Win32Native.GetCurrentThreadId();
        _hInstance = Win32Native.GetModuleHandle(null);
        _className = "LAT_TrayWindowClass_" + Guid.NewGuid().ToString("N");

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
            startedSignal.Set();
            return;
        }

        _hwnd = Win32Native.CreateWindowEx(
            0,
            _className,
            "LocalActivityTracker_TrayWindow",
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
            _hIcon = Win32Native.LoadIcon(IntPtr.Zero, Win32Native.IDI_APPLICATION);

            var nid = new Win32Native.NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<Win32Native.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = TRAY_ICON_ID,
                uFlags = Win32Native.NIF_MESSAGE | Win32Native.NIF_ICON | Win32Native.NIF_TIP,
                uCallbackMessage = Win32Native.WM_TRAYICON,
                hIcon = _hIcon,
                szTip = TruncateTooltip(_currentTooltip)
            };

            Win32Native.Shell_NotifyIcon(Win32Native.NIM_ADD, ref nid);
        }

        startedSignal.Set();

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

    private IntPtr TrayWindowProcedure(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32Native.WM_TRAYICON)
        {
            var eventCode = (uint)lParam.ToInt64();
            if (eventCode == WM_LBUTTONDBLCLK)
            {
                OpenDashboardRequested?.Invoke(this, EventArgs.Empty);
            }
            return IntPtr.Zero;
        }

        if (msg == Win32Native.WM_DESTROY)
        {
            Win32Native.PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return Win32Native.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static string TruncateTooltip(string tooltip)
    {
        if (string.IsNullOrEmpty(tooltip))
        {
            return "Local Activity Tracker";
        }

        return tooltip.Length > 127 ? tooltip[..127] : tooltip;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Remove();
    }
}
