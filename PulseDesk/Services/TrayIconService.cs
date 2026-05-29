using System;
using System.Collections.Generic;
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
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const int IMAGE_ICON = 1;
    private const int LR_LOADFROMFILE = 0x00000010;

    private readonly int _iconId;
    private readonly string _label;
    private readonly int _accentColor;
    private readonly Dictionary<int, IntPtr> _iconCache = new();
    private IntPtr _messageWindow;
    private IntPtr _iconHandle;
    private int _lastPercent = -1;
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

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll")]
    private static extern int SetBkMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr hdc, int color);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(
        IntPtr hdc, ref BITMAPINFO pbmi, int usage,
        out IntPtr ppvBits, IntPtr hSection, int offset);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        int bItalic, int bUnderline, int bStrikeOut, int iCharSet,
        int iOutPrecision, int iClipPrecision, int iQuality, int iPitchAndFamily,
        string pszFaceName);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO piconinfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, int format);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int nWidth, int nHeight, int nPlanes, int nBitCount, IntPtr lpBits);

    private const int TRANSPARENT = 1;
    private const int DT_CENTER = 0x01;
    private const int DT_VCENTER = 0x04;
    private const int DT_SINGLELINE = 0x20;
    private const int DT_NOCLIP = 0x100;
    private const int FW_BOLD = 700;
    private const int ANTIALIASED_QUALITY = 4;
    private const int DIB_RGB_COLORS = 0;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int color);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    #endregion

    public TrayIconService(int iconId = 1, string label = "PulseDesk", int accentColor = 0x00FF8800)
    {
        _iconId = iconId;
        _label = label;
        _accentColor = accentColor;
        _wndProcDelegate = WndProc;
        CreateMessageWindow();
        LoadIcon();
        AddTrayIcon();
    }

    private void CreateMessageWindow()
    {
        var hInstance = GetModuleHandleW(null);
        var className = $"PulseDesk_TrayMsg_{_iconId}_{Environment.ProcessId}";

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
            uID = _iconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = _label
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

    /// <summary>
    /// Updates the tray icon to show the given percentage as rendered text with a dynamic fill.
    /// Icons are cached by percent value (0–100) so repeated calls with the same value are free.
    /// </summary>
    public void UpdatePercent(int percent)
    {
        if (_disposed || !_iconAdded) return;

        percent = Math.Clamp(percent, 0, 100);

        // Nothing to do if the value hasn’t changed.
        if (percent == _lastPercent) return;

        if (!_iconCache.TryGetValue(percent, out var cachedIcon))
        {
            cachedIcon = CreateTextIcon(percent.ToString(), percent, _accentColor);
            if (cachedIcon == IntPtr.Zero) return;
            _iconCache[percent] = cachedIcon;
        }

        var nid = new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _messageWindow,
            uID = _iconId,
            uFlags = NIF_ICON | NIF_TIP,
            hIcon = cachedIcon,
            szTip = $"{_label} {percent}%"
        };

        if (Shell_NotifyIconW(NIM_MODIFY, ref nid))
        {
            _lastPercent = percent;
        }
    }

    private static IntPtr CreateTextIcon(string text, int percent, int accentColor)
    {
        const int size = 16;
        const int stripeHeight = 3;

        var hdc = CreateCompatibleDC(IntPtr.Zero);
        if (hdc == IntPtr.Zero) return IntPtr.Zero;

        IntPtr hBitmap = IntPtr.Zero;
        IntPtr hMask = IntPtr.Zero;
        IntPtr hFont = IntPtr.Zero;
        IntPtr icon = IntPtr.Zero;

        try
        {
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = size,
                    biHeight = -size, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                }
            };

            hBitmap = CreateDIBSection(hdc, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero) return IntPtr.Zero;

            var oldBitmap = SelectObject(hdc, hBitmap);

            // Fill the entire icon with a dark background (semi-transparent dark gray).
            var darkBrush = CreateSolidBrush(0x00303030); // BGR dark gray
            var fullRect = new RECT { left = 0, top = 0, right = size, bottom = size };
            FillRect(hdc, ref fullRect, darkBrush);
            DeleteObject(darkBrush);

            // Fill from the bottom up proportionally to the percentage.
            // Color: green (0-65%), yellow (66-85%), red (86-100%).
            int fillColor = percent switch
            {
                <= 65 => 0x0000B050,  // BGR green
                <= 85 => 0x0000C8E0,  // BGR yellow/amber
                _     => 0x000035E0   // BGR red
            };

            var fillHeight = (int)((size - stripeHeight) * Math.Clamp(percent, 0, 100) / 100.0);
            if (fillHeight > 0)
            {
                var fillBrush = CreateSolidBrush(fillColor);
                var fillRect = new RECT
                {
                    left = 0,
                    top = size - fillHeight,
                    right = size,
                    bottom = size
                };
                FillRect(hdc, ref fillRect, fillBrush);
                DeleteObject(fillBrush);
            }

            // Draw a colored accent stripe at the top to identify the metric.
            var accentBrush = CreateSolidBrush(accentColor);
            var accentRect = new RECT { left = 0, top = 0, right = size, bottom = stripeHeight };
            FillRect(hdc, ref accentRect, accentBrush);
            DeleteObject(accentBrush);

            // Set pixel alpha to fully opaque for all pixels.
            if (bits != IntPtr.Zero)
            {
                unsafe
                {
                    var p = (byte*)bits;
                    for (var i = 0; i < size * size; i++)
                    {
                        p[i * 4 + 3] = 0xFF; // alpha channel
                    }
                }
            }

            // Draw white text on top.
            SetBkMode(hdc, TRANSPARENT);
            SetTextColor(hdc, 0x00FFFFFF); // white in BGR

            // Use a compact font; shrink further for 3-digit "100".
            var fontSize = text.Length >= 3 ? -9 : -11;
            hFont = CreateFontW(
                fontSize, 0, 0, 0, FW_BOLD,
                0, 0, 0, 0, 0, 0, ANTIALIASED_QUALITY, 0, "Segoe UI");
            var oldFont = SelectObject(hdc, hFont);

            var rect = new RECT { left = 0, top = 0, right = size, bottom = size };
            DrawTextW(hdc, text, text.Length, ref rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOCLIP);

            SelectObject(hdc, oldFont);
            SelectObject(hdc, oldBitmap);

            // Create a monochrome mask bitmap (all zeros = fully opaque).
            hMask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);

            var iconInfo = new ICONINFO
            {
                fIcon = true,
                hbmMask = hMask,
                hbmColor = hBitmap
            };

            icon = CreateIconIndirect(ref iconInfo);
            return icon;
        }
        finally
        {
            if (hFont != IntPtr.Zero) DeleteObject(hFont);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (hMask != IntPtr.Zero) DeleteObject(hMask);
            DeleteDC(hdc);
        }
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
                uID = _iconId
            };
            Shell_NotifyIconW(NIM_DELETE, ref nid);
        }

        if (_messageWindow != IntPtr.Zero)
        {
            DestroyWindow(_messageWindow);
        }

        foreach (var handle in _iconCache.Values)
        {
            DestroyIcon(handle);
        }
        _iconCache.Clear();

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
    }
}
