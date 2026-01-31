using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinRT.Interop;

namespace WallpaperDockWinUI.Views
{
    public sealed partial class InputDialog : Window
    {
        private TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

        // If true, empty input is invalid
        public bool RequireNonEmpty { get; set; } = false;

        // Optional validator: return null for OK, or an error message to display
        public Func<string, string?>? Validator { get; set; }

        public string TitleText
        {
            get => DialogTitle.Text;
            set => DialogTitle.Text = value;
        }

        public string Input
        {
            get => InputBox.Text;
            set => InputBox.Text = value;
        }

        public InputDialog()
        {
            this.InitializeComponent();

            // window size will be applied when centering (AppWindow.MoveAndResize)
            // sizes are defined in XAML (Grid 420x160)

            OkButton.Click += OkButton_Click;
            CancelButton.Click += CancelButton_Click;
            InputBox.TextChanged += InputBox_TextChanged;

            // Titlebar extension removed to avoid conflicts with rounded corners and transparency.
            // Previously used ExtendsContentIntoTitleBar / SetTitleBar(RootGrid) which could interfere with Border rounding.

            this.Closed += InputDialog_Closed;
        }

        private void InputBox_TextChanged(object? sender, TextChangedEventArgs e)
        {
            ValidateInput();
        }

        private void ValidateInput()
        {
            string text = InputBox.Text?.Trim() ?? string.Empty;
            string? err = null;
            if (RequireNonEmpty && string.IsNullOrEmpty(text))
            {
                err = "名称不能为空";
            }
            else if (Validator != null)
            {
                err = Validator(text);
            }

            if (!string.IsNullOrEmpty(err))
            {
                ErrorText.Text = err;
                ErrorText.Visibility = Visibility.Visible;
                OkButton.IsEnabled = false;
            }
            else
            {
                ErrorText.Text = string.Empty;
                ErrorText.Visibility = Visibility.Collapsed;
                OkButton.IsEnabled = true;
            }
        }

        // P/Invoke for removing window chrome
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_BORDER = 0x00800000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_EX_LAYERED = 0x00080000;
        
        private const uint LWA_COLORKEY = 0x00000001;
        private const uint LWA_ALPHA = 0x00000002;

        // DWM constants for window corner preference
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        private void RemoveWindowChrome()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);

                // Remove standard window styles (frame, caption, system menu, resize, min/max)
                int currentStyle = GetWindowLong(hwnd, GWL_STYLE);
                int newStyle = currentStyle & ~(WS_BORDER | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
                SetWindowLong(hwnd, GWL_STYLE, newStyle);

                // Ensure layered window style so we can have transparent background and proper rounded corners
                
                try
                {
                    int currentEx = GetWindowLong(hwnd, GWL_EXSTYLE);
                    int newEx = currentEx | WS_EX_LAYERED;
                    SetWindowLong(hwnd, GWL_EXSTYLE, newEx);

                    // Set layered window attributes to enable transparency support (fully opaque content but layered)
                    SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

                    // Ask DWM to prefer rounded corners for this window (helps eliminate hard rectangular frame)
                    try
                    {
                        int corner = DWMWCP_ROUND;
                        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"DwmSetWindowAttribute failed: {ex}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Set layered style failed: {ex}");
                }

                // Refresh window styles
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RemoveWindowChrome failed: {ex}");
            }
        }

        private void InputDialog_Closed(object sender, WindowEventArgs args)
        {
            // Ensure task completes if window closed by other means
            if (!_tcs.Task.IsCompleted)
                _tcs.TrySetResult(false);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // final validation
            ValidateInput();
            if (!OkButton.IsEnabled)
                return;

            _tcs.TrySetResult(true);
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _tcs.TrySetResult(false);
            this.Close();
        }

        public async Task<bool> ShowCenteredAsync()
        {
            try
            {
                // Activate window so AppWindow is available
                this.Activate();

                // Remove chrome/buttons
                RemoveWindowChrome();

                // Center the window on the display containing the main window
                try
                {
                    var da = DisplayArea.GetFromWindowId(App.Current.MainWindow.AppWindow.Id, DisplayAreaFallback.Nearest);
                    if (da != null)
                    {
                        // Force a layout pass so we can use DesiredSize (more reliable than ActualWidth immediately after Activate)
                        try
                        {
                            RootGrid.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                            // Arrange with the measured desired size to get stable DesiredSize/ActualHeight
                            RootGrid.Arrange(new Windows.Foundation.Rect(0, 0, RootGrid.DesiredSize.Width, RootGrid.DesiredSize.Height));
                            RootGrid.UpdateLayout();
                        }
                        catch { }

                        var desiredSize = RootGrid.DesiredSize;
                        double desiredWidthDips = (desiredSize.Width > 0) ? desiredSize.Width : (RootGrid.MinWidth > 0 ? RootGrid.MinWidth : 480);
                        double desiredHeightDips = (desiredSize.Height > 0) ? desiredSize.Height : (RootGrid.MinHeight > 0 ? RootGrid.MinHeight : 220);

                        // clamp width to a reasonable dialog range so it doesn't feel too wide
                        desiredWidthDips = Math.Clamp(desiredWidthDips, 400, 600);

                        // get the rasterization scale (DIP -> physical pixels)
                        double scale = 1.0;
                        try
                        {
                            scale = this.Content.XamlRoot?.RasterizationScale ?? 1.0;
                        }
                        catch
                        {
                            scale = 1.0;
                        }

                        // convert to physical pixels for AppWindow APIs and add a small safety buffer
                        int w = (int)Math.Ceiling(desiredWidthDips * scale) + 2;
                        int h = (int)Math.Ceiling(desiredHeightDips * scale) + 6;

                        // clamp to display work area so we don't overflow the screen
                        w = (int)Math.Min(w, da.WorkArea.Width);
                        h = (int)Math.Min(h, da.WorkArea.Height);

                        // attempt to set presenter style to remove titlebar and borders so system round corners apply
                        try
                        {
                            if (this.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter op)
                            {
                                op.IsResizable = false;
                                op.IsMinimizable = false;
                                op.IsMaximizable = false;
                                try { op.SetBorderAndTitleBar(false, false); } catch { }
                            }
                        }
                        catch { }

                        // set accessibility title
                        try { this.Title = TitleText; this.AppWindow.Title = TitleText; } catch { }

                        // compute centered physical position (DisplayArea.WorkArea is in raw pixels)
                        var centerX = (int)(da.WorkArea.X + (da.WorkArea.Width - w) / 2);
                        var centerY = (int)(da.WorkArea.Y + (da.WorkArea.Height - h) / 2);

                        // Move and resize using physical pixels
                        this.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(centerX, centerY, w, h));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Centering InputDialog failed: {ex}");
                }

                // default focus, select all text to allow easy overwrite, and validate initial input
                this.DispatcherQueue.TryEnqueue(() =>
                {
                    InputBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
                    try
                    {
                        // Select all text so user can overwrite quickly
                        InputBox.SelectAll();
                    }
                    catch { }
                    ValidateInput();
                });

                return await _tcs.Task;
            }
            finally
            {
                // reset tcs for potential reuse
                _tcs = new TaskCompletionSource<bool>();
            }
        }
    }
}