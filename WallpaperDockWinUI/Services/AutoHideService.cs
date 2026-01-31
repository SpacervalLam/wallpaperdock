using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;

namespace WallpaperDockWinUI.Services
{
    public interface IAutoHideService
    {
        void Initialize(Window window);
        void Start();
        void Stop();
    }

    public class AutoHideService : IAutoHideService
    {
        private Window? _window;
        private DisplayArea? _displayArea;
        
        // 状态变量
        private int _currentFullWidth; // 记录窗口当前的完整宽度
        private int _visibleStripWidth = 2; // 隐藏时露出的像素宽度 (边缘触发区域)
        private bool _isHidden = false;
        
        private DispatcherTimer? _mouseCheckTimer; 

        // 时间配置
        private readonly TimeSpan _checkInterval = TimeSpan.FromMilliseconds(50);
        private const int HideDelayMs = 500; 
        private const int EdgeShowDelayMs = 100; // 缩短为 100ms（0.1s）

        // 计时追踪
        private long _lastMouseOverWindowTime = 0;
        private long _edgeHoverStartTime = 0;

        // 动画相关
        private DispatcherTimer? _animTimer;
        private long _animStartTime = 0;
        private int _animDuration = 250; // 动画时长 ms
        
        // 动画只需要改变 X 坐标，不需要改变宽
        private int _animStartX = 0;
        private int _animTargetX = 0;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        public void Initialize(Window window)
        {
            _window = window;
            _displayArea = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest);
            if (_displayArea != null)
            {
                _currentFullWidth = _window.AppWindow.Size.Width;
                _lastMouseOverWindowTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        public void Start()
        {
            if (_window == null) return;
            if (_mouseCheckTimer == null)
            {
                _mouseCheckTimer = new DispatcherTimer();
                _mouseCheckTimer.Interval = _checkInterval;
                _mouseCheckTimer.Tick += OnMouseCheckTimerTick;
                _mouseCheckTimer.Start();
            }
        }

        public void Stop()
        {
            if (_mouseCheckTimer != null)
            {
                _mouseCheckTimer.Stop();
                _mouseCheckTimer = null;
            }
            StopAnimation();
        }

        private void OnMouseCheckTimerTick(object? sender, object e)
        {
            if (_window == null) return;
            
            _displayArea = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Nearest);
            if (_displayArea == null) return;

            if (!GetCursorPos(out POINT mousePos)) return;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            
            // 获取屏幕边界
            int screenRight = (int)(_displayArea.WorkArea.X + _displayArea.WorkArea.Width);
            var winPos = _window.AppWindow.Position;
            var winSize = _window.AppWindow.Size;

            // --- 核心逻辑 ---

            if (_isHidden)
            {
                // [状态：隐藏中] 
                // 此时窗口大部分在屏幕外，只有 _visibleStripWidth 在屏幕内
                // 我们检测鼠标是否在屏幕最右侧的边缘区域
                
                // 判定鼠标是否在"露出来的那个细条"或者"屏幕边缘"
                bool atRightEdge = mousePos.X >= (screenRight - _visibleStripWidth * 2); //稍微放宽一点判定范围
                bool withinY = mousePos.Y >= winPos.Y && mousePos.Y <= winPos.Y + winSize.Height;

                if (atRightEdge && withinY)
                {
                    if (_edgeHoverStartTime == 0) _edgeHoverStartTime = now;
                    
                    if (now - _edgeHoverStartTime >= EdgeShowDelayMs)
                    {
                        StartShowAnimation();
                        _edgeHoverStartTime = 0;
                        _lastMouseOverWindowTime = now;
                    }
                }
                else
                {
                    _edgeHoverStartTime = 0;
                }
            }
            else
            {
                // [状态：显示中]
                // 实时更新宽度记录（以防用户拖拽改变了窗口大小）
                if (_animTimer == null || !_animTimer.IsEnabled) // 只有不在动画时才更新宽度
                {
                    _currentFullWidth = _window.AppWindow.Size.Width;
                }

                // 判断鼠标是否在窗口范围内
                bool overWindow = mousePos.X >= winPos.X && mousePos.X <= winPos.X + winSize.Width && 
                                  mousePos.Y >= winPos.Y && mousePos.Y <= winPos.Y + winSize.Height;

                if (overWindow)
                {
                    _lastMouseOverWindowTime = now;
                }
                else
                {
                    if (now - _lastMouseOverWindowTime >= HideDelayMs)
                    {
                        StartHideAnimation();
                    }
                }
                _edgeHoverStartTime = 0;
            }
        }

        private void StartHideAnimation()
        {
            if (_isHidden || _window == null || _displayArea == null) return;

            _isHidden = true;
            
            int screenRight = (int)(_displayArea.WorkArea.X + _displayArea.WorkArea.Width);
            
            // 起点：当前位置 (应该是完全显示的位置)
            _animStartX = _window.AppWindow.Position.X;
            
            // 终点：屏幕右侧 - 露出的细条宽度
            // 举例：屏幕宽1920，露出4px。终点X = 1916。
            // 此时窗口宽300，所以窗口范围是 1916 ~ 2216。也就是大部分在屏幕外。
            _animTargetX = screenRight - _visibleStripWidth;

            StartAnimation();
        }

        private void StartShowAnimation()
        {
            if (!_isHidden || _window == null || _displayArea == null) return;

            _isHidden = false;

            int screenRight = (int)(_displayArea.WorkArea.X + _displayArea.WorkArea.Width);

            // 起点：当前位置 (隐藏状态的位置)
            _animStartX = _window.AppWindow.Position.X;

            // 终点：屏幕右侧 - 完整宽度
            // 举例：屏幕1920，宽300。终点X = 1620。
            _animTargetX = screenRight - _currentFullWidth;

            StartAnimation();
        }

        private void StartAnimation()
        {
            StopAnimation();

            _animStartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _animTimer = new DispatcherTimer();
            _animTimer.Interval = TimeSpan.FromMilliseconds(16); // 60fps

            long startTimeLocal = _animStartTime;
            int startX = _animStartX;
            int targetX = _animTargetX;
            int duration = _animDuration;

            _animTimer.Tick += (s, e) =>
            {
                if (_window == null)
                {
                    StopAnimation();
                    return;
                }

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                double t = Math.Min(1.0, (now - startTimeLocal) / (double)duration);
                double tEase = 1 - Math.Pow(1 - t, 3); // EaseOutCubic

                // 计算当前 X
                int currentX = (int)(startX + (targetX - startX) * tEase);
                int currentY = _window.AppWindow.Position.Y;
                
                // 关键修改：只使用 Move，不再 Resize
                // 宽度始终保持 _currentFullWidth，只是把多余的部分移到了屏幕外
                _window.AppWindow.Move(new Windows.Graphics.PointInt32(currentX, currentY));
                
                // 确保宽度不被意外改变（可选，强制锁定宽度）
                if (_window.AppWindow.Size.Width != _currentFullWidth)
                {
                    _window.AppWindow.Resize(new Windows.Graphics.SizeInt32(_currentFullWidth, _window.AppWindow.Size.Height));
                }

                if (t >= 1.0)
                {
                    StopAnimation();
                }
            };
            _animTimer.Start();
        }

        private void StopAnimation()
        {
            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer = null;
            }
        }
    }
}
