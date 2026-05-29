using System;
using System.Runtime.InteropServices;

namespace PulseDesk.Services;

/// <summary>
/// Manages a Windows notification-area (tray) icon using Win32 Shell_NotifyIcon.
/// Fires <see cref="Clicked"/> when the user left-clicks or double-clicks the icon.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;

    private IntPtr _messageWindow;
    private IntPtr _iconHandle;
    private bool _iconAdded;
    private bool _disposed;

    // Must be stored as a field to prevent the delegate from being garbage-collected
    // while the message window is still alive.
    private readonly WndProcDelegate _wndProcDelegate;

    public event Action? Clicked;

    #region P/Invoke

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public int style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(int dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(
        int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, int type, int cx, int cy, int fuLoad);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, int nIconIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    #endregion

    public TrayIconService()
    {
        _wndProcDelegate = WndProc;
        CreateMessageWindow();
        LoadIcon();
        AddTrayIcon();
    }

    private void CreateMessageWindow()
    {
        var hInstance = GetModuleHandleW(null);
        var className = "PulseDesk_TrayMsg_" + Environment.ProcessId;

        var wc = new WNDCLASSEXW
        {
            cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = _wndProcDelegate,
            hInstance = hInstance,
            lpszClassName = className
        };

        RegisterClassExW(ref wc);

        // HWND_MESSAGE (-3) creates a message-only window (invisible, no taskbar entry).
        _messageWindow = CreateWindowExW(
            0, className, "PulseDesk Tray", 0,
            0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, hInstance, IntPtr.Zero);
    }

    private void LoadIcon()
    {
        // Try extracting the icon embedded in the current executable.
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            _iconHandle = ExtractIconW(GetModuleHandleW(null), exePath, 0);
        }

        // Try loading an explicit tray.ico from the Assets folder.
        if (_iconHandle == IntPtr.Zero)
        {
            var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "tray.ico");
            if (System.IO.File.Exists(icoPath))
            {
                _iconHandle = LoadImageW(IntPtr.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
            }
        }

        // Fallback to the default Windows application icon (IDI_APPLICATION = 32512).
        if (_iconHandle == IntPtr.Zero)
        {
            _iconHandle = LoadIconW(IntPtr.Zero, new IntPtr(32512));
        }
    }

    private void AddTrayIcon()
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageWindow,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = "PulseDesk"
        };

        _iconAdded = Shell_NotifyIconW(NIM_ADD, ref nid);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var mouseMsg = (int)lParam;
            if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
            {
                Clicked?.Invoke();
            }
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_iconAdded)
        {
            var nid = new NOTIFYICONDATAW
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _messageWindow,
                uID = 1
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
        }

        if (_messageWindow != IntPtr.Zero)
        {
            DestroyWindow(_messageWindow);
        }

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }
}
