using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace KovanTerm.Interop;

/// <summary>
/// Windows notifications through the notification area (Shell_NotifyIcon): Windows 10 and 11 show a balloon as a
/// toast, so no WinRT projection or notification package is needed. The icon is added with the first notification
/// and removed with <see cref="Hide"/>; clicks come back through a message-only window.
/// </summary>
public sealed class TrayNotifier : IDisposable
{
    private const int WM_APP = 0x8000, CallbackMessage = WM_APP + 1;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const int NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const int NIIF_INFO = 1, NIIF_WARNING = 2, NIIF_RESPECT_QUIET_TIME = 0x80;
    private const int NOTIFYICON_VERSION_4 = 4;
    private const int NIN_SELECT = 0x400, NIN_KEYSELECT = 0x401, NIN_BALLOONUSERCLICK = 0x405;
    private const int WM_LBUTTONUP = 0x202;
    private const int IDI_APPLICATION = 32512;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    private readonly HwndSource _window;
    private bool _added;

    /// <summary>The notification was clicked.</summary>
    public event Action? Clicked;

    public TrayNotifier()
    {
        // HWND_MESSAGE: receives messages, never shown
        _window = new HwndSource(new HwndSourceParameters("Kovan notifications") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
    }

    private NOTIFYICONDATAW Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = LoadIcon(IntPtr.Zero, new IntPtr(IDI_APPLICATION)),
        szTip = "Kovan Term",
        szInfo = "",
        szInfoTitle = "",
    };

    /// <summary>Shows a notification; a newer one replaces the one still on screen. <c>false</c> if Windows refused.</summary>
    public bool Show(string title, string text, bool warning)
    {
        if (!_added)
        {
            var add = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
            if (!Shell_NotifyIconW(NIM_ADD, ref add)) return false;
            add.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, ref add);
            _added = true;
        }
        var info = Data(NIF_INFO);
        info.szInfoTitle = Truncate(title, 63);
        info.szInfo = Truncate(text, 255);
        // Focus assist / quiet hours hold it back like any other app's
        info.dwInfoFlags = (warning ? NIIF_WARNING : NIIF_INFO) | NIIF_RESPECT_QUIET_TIME;
        return Shell_NotifyIconW(NIM_MODIFY, ref info);
    }

    /// <summary>Removes the icon (and with it the notifications still in the notification center).</summary>
    public void Hide()
    {
        if (!_added) return;
        var data = Data(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
    }

    /// <summary>Simulates a click (for testing with window messages only).</summary>
    public IntPtr Handle => _window.Handle;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != CallbackMessage) return IntPtr.Zero;
        // Version 4: the event is in the low word of lParam
        var e = (int)(lParam.ToInt64() & 0xFFFF);
        if (e is NIN_BALLOONUSERCLICK or NIN_SELECT or NIN_KEYSELECT or WM_LBUTTONUP)
        {
            handled = true;
            Clicked?.Invoke();
        }
        return IntPtr.Zero;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public void Dispose()
    {
        Hide();
        _window.Dispose();
    }
}
