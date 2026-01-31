using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace WallpaperDockWinUI.Services
{
    public interface ITrayIconService
    {
        void Initialize(Window window);
        void Show();
        void Hide();
        void UpdateStatus(TrayIconStatus status);
        void Dispose();
    }

    public enum TrayIconStatus
    {
        Normal,
        Background,
        Notification
    }

    public class TrayIconService : ITrayIconService, IDisposable
    {
        private Window _window;
        private IntPtr _windowHandle;
        private uint _taskbarCreatedMessage;
        private bool _visible;
        private bool _disposed;
        private NOTIFYICONDATA _notifyIconData;
        private IntPtr _originalWndProc;
        private WndProcDelegate _wndProcDelegate;

        public void Initialize(Window window)
        {
            _window = window;
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            InitializeNotifyIcon();
            SubclassWindow();
        }

        private void InitializeNotifyIcon()
        {
            _notifyIconData = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE | NIF_SHOWTIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = GetAppIcon(),
                szTip = "Wallpaper Dock\0"
            };
        }

        private void SubclassWindow()
        {
            _wndProcDelegate = new WndProcDelegate(WndProc);
            _originalWndProc = SetWindowLongPtr(_windowHandle, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        }

        private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
        {
            if (uMsg == WM_TRAYICON)
            {
                HandleTrayIconMessage(lParam);
            }
            else if (uMsg == _taskbarCreatedMessage)
            {
                // Recreate tray icon when taskbar is restarted
                if (_visible)
                {
                    Shell_NotifyIcon(NIM_ADD, ref _notifyIconData);
                }
            }

            return CallWindowProc(_originalWndProc, hWnd, uMsg, wParam, lParam);
        }

        private void HandleTrayIconMessage(IntPtr lParam)
        {
            uint message = (uint)lParam;
            switch (message)
            {
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                    // Both left and right click to show context menu
                    ShowContextMenu();
                    break;
                case WM_LBUTTONDBLCLK:
                    // Double click to show/hide window
                    ToggleWindowVisibility();
                    break;
            }
        }

        private void ToggleWindowVisibility()
        {
            if (_window != null)
            {
                // For WinUI 3, we can check if window is active
                // For simplicity, we'll just activate it
                _window.Activate();
            }
        }

        private void ShowContextMenu()
        {
            // Get cursor position
            POINT pt;
            GetCursorPos(out pt);

            // Create popup menu
            IntPtr hMenu = CreatePopupMenu();

            // Add menu items
            uint idSettings = 1;
            uint idAbout = 2;
            uint idExit = 3;

            AppendMenu(hMenu, MF_STRING, idSettings, "设置");
            AppendMenu(hMenu, MF_STRING, idAbout, "关于");
            AppendMenu(hMenu, MF_SEPARATOR, 0, null);
            AppendMenu(hMenu, MF_STRING, idExit, "退出");

            // Set foreground window to ensure menu appears
            SetForegroundWindow(_windowHandle);

            // Show menu
            uint result = TrackPopupMenu(hMenu, TPM_RETURNCMD | TPM_NONOTIFY, pt.x, pt.y, 0, _windowHandle, IntPtr.Zero);

            // Process menu result
            System.Diagnostics.Debug.WriteLine($"Menu result: {result}");
            if (result == idSettings)
            {
                ShowSettings();
            }
            else if (result == idAbout)
            {
                ShowAbout();
            }
            else if (result == idExit)
            {
                ExitApplication();
            }

            // Destroy menu
            DestroyMenu(hMenu);
        }

        private void ShowSettings()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ShowSettings called");
                if (_window == null)
                {
                    System.Diagnostics.Debug.WriteLine("Window is null");
                    return;
                }

                // 在 UI 线程执行
                bool enqueued = _window.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // 使用简单的 Win32 MessageBox 作为临时解决方案
                        MessageBox(_windowHandle, "我还没想到有啥好设置的", "Wallpaper Dock", MB_OK | MB_ICONINFORMATION);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error showing settings dialog: {ex.Message}");
                    }
                });

                System.Diagnostics.Debug.WriteLine($"DispatcherQueue.TryEnqueue returned: {enqueued}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ShowSettings: {ex.Message}");
            }
        }

        private void ShowAbout()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("ShowAbout called");
                if (_window == null)
                {
                    System.Diagnostics.Debug.WriteLine("Window is null");
                    return;
                }

                // 在 UI 线程执行
                bool enqueued = _window.DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        // 使用简单的 Win32 MessageBox 作为临时解决方案
                        MessageBox(_windowHandle, "使用前请确保已经安装Wallpaper Engine\n\nWallpaper Dock v1.0\n\n© 2026 github.com/SpacervalLam\n\n更多问题咨询Email: spacervallam@gmail.com", "关于 Wallpaper Dock", MB_OK | MB_ICONINFORMATION);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error showing about dialog: {ex.Message}");
                    }
                });

                System.Diagnostics.Debug.WriteLine($"DispatcherQueue.TryEnqueue returned: {enqueued}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ShowAbout: {ex.Message}");
            }
        }

        private void ExitApplication()
        {
            try
            {
                if (_window != null)
                {
                    // Ensure we're on the UI thread
                    _window.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            var app = App.Current as App;
                            app?.Cleanup();
                            Application.Current.Exit();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error exiting application: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ExitApplication: {ex.Message}");
            }
        }

        private IntPtr GetAppIcon()
        {
            try
            {
                // Use the specified icon file
                var assemblyLocation = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                var basePath = System.IO.Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
                var iconPath = System.IO.Path.Combine(basePath, "Assets", "Icons", "badge_mountain.ico");
                
                if (System.IO.File.Exists(iconPath))
                {
                    // Use LoadImage instead of ExtractIcon for better ICO file support
                    return LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                }
                
                // Fallback to executable icon if specified icon not found
                var exePath = assemblyLocation;
                if (!string.IsNullOrEmpty(exePath))
                {
                    return ExtractIcon(exePath, 0, 1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading icon: {ex.Message}");
            }
            return IntPtr.Zero;
        }

        public void Show()
        {
            if (!_visible)
            {
                Shell_NotifyIcon(NIM_ADD, ref _notifyIconData);
                _visible = true;
            }
        }

        public void Hide()
        {
            if (_visible)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _notifyIconData);
                _visible = false;
            }
        }

        public void UpdateStatus(TrayIconStatus status)
        {
            if (!_visible) return;

            // Update icon based on status
            switch (status)
            {
                case TrayIconStatus.Normal:
                    _notifyIconData.hIcon = GetAppIcon();
                    break;
                case TrayIconStatus.Background:
                    // TODO: Add background status icon
                    break;
                case TrayIconStatus.Notification:
                    // TODO: Add notification status icon
                    break;
            }

            Shell_NotifyIcon(NIM_MODIFY, ref _notifyIconData);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Hide();
                    if (_notifyIconData.hIcon != IntPtr.Zero)
                    {
                        DestroyIcon(_notifyIconData.hIcon);
                    }
                    RestoreWindowProc();
                }

                _disposed = true;
            }
        }

        private void RestoreWindowProc()
        {
            if (_originalWndProc != IntPtr.Zero)
            {
                SetWindowLongPtr(_windowHandle, GWL_WNDPROC, _originalWndProc);
                _originalWndProc = IntPtr.Zero;
            }
        }

        ~TrayIconService()
        {
            Dispose(false);
        }

        // Windows API constants
        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_SHOWTIP = 0x00000080;
        private const uint WM_TRAYICON = 0x8000;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_LBUTTONDBLCLK = 0x0203;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_RBUTTONDBLCLK = 0x0206;
        private const int GWL_WNDPROC = -4;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint TPM_RETURNCMD = 0x00000100;
        private const uint TPM_NONOTIFY = 0x00000080;
        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x0010;
        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONINFORMATION = 0x00000040;

        // Delegate for window procedure
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        // Windows API functions
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA pnid);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr ExtractIcon(string lpszFile, int nIconIndex, int cxIcon);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr LoadImage(IntPtr hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

        // Structures
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        // NOTIFYICONDATA structure
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }
    }
}
