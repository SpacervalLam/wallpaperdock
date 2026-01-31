using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using WallpaperDockWinUI.Services;
using WallpaperDockWinUI.ViewModels;

namespace WallpaperDockWinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window window = Window.Current;
        private IAutoHideService? _autoHideService;
        private ITrayIconService? _trayIconService;

        /// <summary>
        /// Gets the current <see cref="App"/> instance in use
        /// </summary>
        public static new App Current => (App)Application.Current;

        /// <summary>
        /// Gets the <see cref="IServiceProvider"/> instance to resolve application services.
        /// </summary>
        public IServiceProvider Services { get; }

        /// <summary>
        /// Gets the main window instance
        /// </summary>
        public Window MainWindow => window;

        // Win32 API imports for hiding window border
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const int WS_BORDER = 0x00800000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
            Services = ConfigureServices();
        }

        /// <summary>
        /// Configures the services for the application.
        /// </summary>
        private static IServiceProvider ConfigureServices() 
        {
            var services = new ServiceCollection();

            // Register services
            services.AddSingleton<ISteamLibraryService, SteamLibraryService>();
            services.AddSingleton<IWallpaperService, WallpaperEngineService>();
            services.AddSingleton<IAutoHideService, AutoHideService>();
            services.AddSingleton<IImageCacheService, ImageCacheService>();
            services.AddSingleton<IMonitorService, MonitorService>();
            services.AddSingleton<IColorService, ColorService>();
            services.AddSingleton<IFavoritesService, FavoritesService>();
            services.AddSingleton<ITrayIconService, TrayIconService>();

            // Register view models
            services.AddTransient<MainViewModel>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            window ??= new Window();

            // Configure window as right-side dock
            ConfigureWindowAsDock(window);

            // Initialize and start auto-hide service
            _autoHideService = Services.GetService<IAutoHideService>();
            _autoHideService.Initialize(window);
            _autoHideService.Start();

            // Initialize and show tray icon
            _trayIconService = Services.GetService<ITrayIconService>();
            _trayIconService.Initialize(window);
            _trayIconService.Show();

            if (window.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                window.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage), e.Arguments);
                window.Activate();
        }

        /// <summary>
        /// Configures the window as a right-side dock
        /// </summary>
        /// <param name="window">The window to configure</param>
        private void ConfigureWindowAsDock(Window window)
        {
            // Get the main display area
            var displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest);
            if (displayArea != null)
            {
                // Set window size: 350px width, full height
                int windowWidth = 350;
                int windowHeight = (int)displayArea.WorkArea.Height;
                int windowX = (int)(displayArea.WorkArea.X + displayArea.WorkArea.Width - windowWidth);
                int windowY = (int)displayArea.WorkArea.Y;

                // 设置窗口位置和大小
                window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(windowX, windowY, windowWidth, windowHeight));

                // 禁用任务栏图标
                window.AppWindow.IsShownInSwitchers = false;

                // 隐藏标题栏
                window.ExtendsContentIntoTitleBar = true;
                
                // 透明化标题栏按钮
                window.AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                window.AppWindow.TitleBar.ButtonForegroundColor = Microsoft.UI.Colors.Transparent;
                window.AppWindow.TitleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Transparent;
                
                // 使用 Win32 API 完全移除窗口边框和标题栏
                HideWindowBorder(window);

                // 尝试启用 Desktop Acrylic（透明玻璃效果），若不可用则回退到 Mica
                try
                {
                    window.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                }
                catch
                {
                    window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                }
            }
        }

        private void HideWindowBorder(Window window)
        {
            try
            {
                // Get the window handle
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

                // Get the current window style
                int currentStyle = GetWindowLong(hwnd, GWL_STYLE);

                // Remove border and title bar styles
                int newStyle = currentStyle & ~(WS_BORDER | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);

                // Set the new window style
                SetWindowLong(hwnd, GWL_STYLE, newStyle);

                // Update the window to reflect the changes
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error hiding window border: {ex.Message}");
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Clean up resources when the application exits
        /// </summary>
        public void Cleanup()
        {
            // Stop auto-hide service
            _autoHideService?.Stop();
            
            // Clean up tray icon service
            _trayIconService?.Dispose();
        }
    }
}
