using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace WallpaperDockWinUI.Views
{
    public sealed partial class ToastNotificationWindow : Window
    {
        // 静态变量跟踪当前显示的toast窗口
        private static ToastNotificationWindow _currentToast = null;
        
        private AppWindow _appWindow;
        private DispatcherTimer _closeTimer;

        // P/Invoke: 用于实现真·透明窗口的关键 API
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        // P/Invoke: 用于显示窗口但不获取焦点
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOWNOACTIVATE = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int cxLeftWidth; public int cxRightWidth; public int cyTopHeight; public int cyBottomHeight; }

        public ToastNotificationWindow(string title, string message, bool showSpeechLinks = false)
        {
            this.InitializeComponent();

            TitleText.Text = title;
            // Remove extra blank lines from message
            MessageText.Text = System.Text.RegularExpressions.Regex.Replace(message, @"\n{2,}", "\n");
            MessageText.Text = MessageText.Text.Trim();

            // 控制语音设置和麦克风设置按钮的显示
            ActionPanel.Visibility = showSpeechLinks ? Visibility.Visible : Visibility.Collapsed;

            // 关闭当前正在显示的toast窗口
            if (_currentToast != null)
            {
                try
                {
                    _currentToast.CloseWithAnimation();
                }
                catch { }
            }
            
            // 设置当前toast窗口为自己
            _currentToast = this;

            // 1. 获取 AppWindow 引用
            _appWindow = this.AppWindow;

            // 2. 初始化样式（去边框、透明化）- 必须尽早执行
            InitializeWindowStyle();

            // 3. 【关键修复】在显示前，先强制设置一个近似的小尺寸和位置
            // 这样即使窗口闪现，也是在右下角闪现一个小块，而不是屏幕中间的大白框
            PreInitializeWindowPosition();
            
            // 4. 等 UI 加载完，再根据文字多少计算精确的高度
            RootCard.Loaded += RootCard_Loaded;
        }

        private void InitializeWindowStyle()
        {
            // 1. 设置不显示在 Alt+Tab 和任务栏中
            _appWindow.IsShownInSwitchers = false;

            // 2. 设置 Presenter: 去掉标题栏和边框
            var presenter = OverlappedPresenter.Create();
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            _appWindow.SetPresenter(presenter);

            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            MARGINS margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }

        // 新增方法：在窗口 Activate 之前就先移到角落
        private void PreInitializeWindowPosition()
        {
            // 获取主显示器区域
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            // 预估尺寸：宽度固定 768，高度扩大到1.32倍 (稍后 Loaded 会修正)
            int estimatedWidth = 788; // 比 Grid 略大一点以防阴影被切
            int estimatedHeight = 264; // 高度扩大到1.32倍 (240 * 1.1)
            int margin = 24;

            // 计算右下角位置
            int x = workArea.X + workArea.Width - estimatedWidth - margin;
            int y = workArea.Y + workArea.Height - estimatedHeight - margin;

            // 立即应用位置和尺寸！
            // 这样当外部调用 .Activate() 时，窗口直接出现在这里，而不是屏幕中间
            _appWindow.MoveAndResize(new RectInt32(x, y, estimatedWidth, estimatedHeight));
        }

        private void RootCard_Loaded(object sender, RoutedEventArgs e)
        {
            // 5. 此时字体渲染完毕，计算【精确】高度
            AdjustWindowSizeToFitContent();
            
            // 6. 播放进场动画
            StartEntranceAnimation();

            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _closeTimer.Tick += (s, args) => CloseWithAnimation();
            _closeTimer.Start();
        }

        private void AdjustWindowSizeToFitContent()
        {
            // 强制测量实际内容
            RootCard.Measure(new Windows.Foundation.Size(768, double.PositiveInfinity));
            
            // 获取精确尺寸 (+20 是为了阴影预留空间)，高度扩大到1.32倍 (1.2 * 1.1)
            int finalWidth = (int)RootCard.DesiredSize.Width + 20; 
            int finalHeight = (int)(RootCard.DesiredSize.Height * 1.32) + 20;

            // 重新计算位置，保持右下角对齐 (因为高度变了，Y坐标需要微调)
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            int margin = 24;

            int x = workArea.X + workArea.Width - finalWidth - margin;
            int y = workArea.Y + workArea.Height - finalHeight - margin;

            _appWindow.MoveAndResize(new RectInt32(x, y, finalWidth, finalHeight));
        }

        private void StartEntranceAnimation()
        {
            var storyboard = new Storyboard();

            // 向上位移
            var translateAnim = new DoubleAnimation
            {
                From = 20, To = 0, Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(translateAnim, EntranceTransform);
            Storyboard.SetTargetProperty(translateAnim, "Y");

            // 透明度渐变 (从 0 到 1)
            var fadeAnim = new DoubleAnimation
            {
                From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(300)
            };
            Storyboard.SetTarget(fadeAnim, RootCard);
            Storyboard.SetTargetProperty(fadeAnim, "Opacity");

            storyboard.Children.Add(translateAnim);
            storyboard.Children.Add(fadeAnim);
            storyboard.Begin();
        }

        private void CloseWithAnimation()
        {
            _closeTimer.Stop();
            var storyboard = new Storyboard();
            var fadeAnim = new DoubleAnimation
            {
                To = 0, Duration = TimeSpan.FromMilliseconds(200)
            };
            Storyboard.SetTarget(fadeAnim, RootCard);
            Storyboard.SetTargetProperty(fadeAnim, "Opacity");

            storyboard.Completed += (s, e) => 
            {
                // 如果当前关闭的是正在跟踪的toast窗口，将其设置为null
                if (_currentToast == this)
                {
                    _currentToast = null;
                }
                this.Close();
            };
            storyboard.Children.Add(fadeAnim);
            storyboard.Begin();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseWithAnimation();

        private void SpeechPrivacyLink_Click(object sender, RoutedEventArgs e)
        {
            // 打开系统面板的"隐私和安全性"->"语音"界面
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-speechtyping") { UseShellExecute = true });
            CloseWithAnimation();
        }

        private void MicrophonePrivacyLink_Click(object sender, RoutedEventArgs e)
        {
            // 打开系统面板的"隐私和安全性"->"麦克风"界面
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:privacy-microphone") { UseShellExecute = true });
            CloseWithAnimation();
        }

        // 公共方法：显示窗口但不获取焦点
        public void ShowWithoutActivation()
        {
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        }
    }
}