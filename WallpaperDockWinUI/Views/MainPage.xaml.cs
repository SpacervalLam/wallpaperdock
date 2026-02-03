using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Windowing;
using System.Threading.Tasks;
using WallpaperDockWinUI.Services;
using WallpaperDockWinUI.ViewModels;
using WallpaperDockWinUI.Views;
using Windows.UI;
using Windows.Media.SpeechRecognition;

namespace WallpaperDockWinUI.Views
{
    /// <summary>
    /// A simple page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; } = null!;
        
        // UI elements
        private Grid? _topBar;
        private TextBox? _searchBox;
        private Grid? _categoryBar;
        private Button? _allButton;
        private Button? _favoritesButton;
        private ComboBox? _groupComboBox;
        private ComboBox? _r18ComboBox;
        private Button? _microphoneButton;
        private Button? _openWallpaperEngineButton;
        private Button? _batchOperationsButton;
        private Button? _schedulePlayButton;
        private Grid? _batchToolbarContainer;

        private ProgressRing? _loadingRing;
        private GridView? _wallpaperList;
        private TextBlock? _emptyText;

        // Speech recognition
        private SpeechRecognizer? _speechRecognizer;
        private bool _isListening;

        // ========== 配置项（根据你项目调整） ==========
        // 单项最大宽度阈值（像素）
        private const double MaxItemWidthThreshold = 305.0;

        // 最大列数上限，可避免太多列导致项过小（可选）
        private const int MaxColumnsLimit = 6;

        // 每项的 Margin（左右合计）
        private const double ItemHorizontalMargin = 16.0; // = 8(left) + 8(right)

        // 根容器 padding（左右合计）
        private const double RootHorizontalPadding = 24.0; // = 12 left + 12 right

        // 防抖时间（ms）
        private const int DebounceDelayMs = 80;

        // ========== 内部状态 ==========
        private System.Threading.CancellationTokenSource? _debounceCts;
        private int _lastAppliedColumns = -1;

        // Scroll/drag support for wallpaper list
        private ScrollViewer? _wallpaperScrollViewer;
        private bool _isGridPointerPressed = false;
        private double _pointerStartY = 0;
        private double _initialVerticalOffset = 0;

        // Inertia support
        private readonly System.Collections.Generic.List<(double Y, long Time)> _pointerHistory = new();
        private Microsoft.UI.Xaml.DispatcherTimer? _inertiaTimer;
        private double _inertiaVelocity = 0; // pixels per ms (positive -> scroll down)
        private const double InertiaFrictionPerTick = 0.94; // per 16ms tick multiplier
        private const double InertiaThreshold = 0.1; // px/ms threshold to start inertia

        // Batch operation mode
        private bool _isBatchMode = false;
        private Grid? _batchToolbar;

        public MainPage()
        {
            this.InitializeComponent();
            ViewModel = (MainViewModel)(App.Current.Services.GetService(typeof(MainViewModel)) ?? throw new InvalidOperationException("ViewModel cannot be null"));
            Loaded += MainPage_Loaded;
            BuildUI();
        }

        private void BuildUI()
        {
            // 保存引用以便后续应用主题资源
            _topBar = CategoryBar;

            // Subscribe to theme changes so we can refresh visual brushes (ThemeService will raise ThemeChanged)
            var themeService = App.Current.Services.GetService(typeof(IThemeService)) as IThemeService;
            if (themeService != null)
            {
                // Apply theme resources immediately and when theme changes
                ApplyThemeResources();
                // Subscribe to theme changes if available
                if (themeService is Services.ThemeService ts)
                {
                    ts.ThemeChanged += (s, e) => ApplyThemeResources();
                }
            }

            // Subscribe to size changes for responsive adjustments
            this.SizeChanged += (s, e) => DebounceRecalculate(e.NewSize.Width);
            MainGrid.SizeChanged += (s, e) => DebounceRecalculate(MainGrid.ActualWidth);
            
            // Add double-tap event handler to toggle window mode
            MainGrid.DoubleTapped += (s, e) =>
            {
                try
                {
                    var app = App.Current as App;
                    app?.ToggleWindowMode();
                    // Update buttons visibility after toggling window mode
                    UpdateButtonsVisibility();
                    
                    // 检查是否在批量操作模式下，并且切换到了 dock 模式
                    if (_isBatchMode)
                    {
                        // 检查当前是否为 dock 模式
                        bool isDockMode = app?.MainWindow.AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen;
                        if (isDockMode)
                        {
                            // 自动退出批量操作模式
                            _isBatchMode = false;
                            
                            // 更新批量操作按钮的图标颜色
                            if (_batchOperationsButton != null)
                            {
                                if (_batchOperationsButton.Content is Viewbox viewbox)
                                {
                                    if (viewbox.Child is Canvas canvas)
                                    {
                                        foreach (var child in canvas.Children)
                                        {
                                            if (child is Rectangle rect)
                                            {
                                                rect.Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                                            }
                                            else if (child is Microsoft.UI.Xaml.Shapes.Path path)
                                            {
                                                path.Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                                            }
                                        }
                                    }
                                }
                            }
                            
                            // 隐藏批量工具栏
                            HideBatchToolbar();
                            
                            // 恢复 GridView 的选择模式
                            if (_wallpaperList != null)
                            {
                                _wallpaperList.SelectionMode = ListViewSelectionMode.None;
                                _wallpaperList.IsItemClickEnabled = true;
                                _wallpaperList.CanDragItems = true;
                                _wallpaperList.SelectedItems.Clear();
                                
                                // 更新现有 WallpaperItem 的 IsBatchMode 属性
                                foreach (var item in _wallpaperList.Items)
                                {
                                    if (item is WallpaperItem wallpaperItem)
                                    {
                                        wallpaperItem.IsBatchMode = false;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            };

            // Right-click forwarding removed: layout is now responsive-only and no longer user-toggleable via right-click.

            // 初始化引用
            _groupComboBox = GroupComboBox;
            _r18ComboBox = R18ComboBox;
            _wallpaperList = WallpaperList;
            _loadingRing = LoadingRing;
            _emptyText = EmptyText;
            _searchBox = SearchBox;
            _microphoneButton = MicrophoneButton;
            _openWallpaperEngineButton = OpenWallpaperEngineButton;
            _batchOperationsButton = BatchOperationsButton;
            _schedulePlayButton = SchedulePlayButton;
            _batchToolbarContainer = BatchToolbarContainer;
            
            // 初始化按钮显示状态
            UpdateButtonsVisibility();

            // 为按钮添加悬浮提示
            if (_openWallpaperEngineButton != null)
            {
                ToolTip toolTip = new ToolTip { Content = "打开 Wallpaper Engine" };
                ToolTipService.SetToolTip(_openWallpaperEngineButton, toolTip);
            }

            if (_batchOperationsButton != null)
            {
                ToolTip toolTip = new ToolTip { Content = "批量操作" };
                ToolTipService.SetToolTip(_batchOperationsButton, toolTip);
            }

            if (_schedulePlayButton != null)
            {
                ToolTip toolTip = new ToolTip { Content = "播放列表" };
                ToolTipService.SetToolTip(_schedulePlayButton, toolTip);
            }

            // 设置搜索框事件
            _searchBox.TextChanged += SearchBox_TextChanged;

            // 设置麦克风按钮事件
            _microphoneButton.Click += MicrophoneButton_Click;

            // 设置打开 Wallpaper Engine 按钮事件
            _openWallpaperEngineButton.Click += OpenWallpaperEngineButton_Click;

            // 设置批量操作按钮事件
            _batchOperationsButton.Click += BatchOperationsButton_Click;

            // 设置定时播放设置按钮事件
            _schedulePlayButton.Click += SchedulePlayButton_Click;

            // 设置分组下拉框
            _groupComboBox.ItemsSource = ViewModel.Groups;
            _groupComboBox.SelectedItem = ViewModel.SelectedGroup;
            _groupComboBox.SelectionChanged += (s, e) =>
            {
                if (_groupComboBox.SelectedItem is string g)
                {
                    ViewModel.SelectedGroup = g;
                    UpdateUI();
                }
            };

            // 创建一个简单的右键菜单
            MenuFlyout contextMenu = new MenuFlyout();

            // 添加刷新分组选项
            MenuFlyoutItem refreshItem = new MenuFlyoutItem {
                Text = "刷新分组",
                Icon = new SymbolIcon(Symbol.Refresh)
            };
            refreshItem.Click += (s, e) => {
                ViewModel.LoadGroups();
            };
            contextMenu.Items.Add(refreshItem);

            // 添加分隔符
            contextMenu.Items.Add(new MenuFlyoutSeparator());

            // 添加管理分组选项
            MenuFlyoutItem manageGroupsItem = new MenuFlyoutItem {
                Text = "管理分组",
                Icon = new SymbolIcon(Symbol.Manage)
            };
            manageGroupsItem.Click += async (s, e) => {
                // 显示分组管理对话框或菜单
                MenuFlyout manageMenu = new MenuFlyout();
                
                // 添加新增分组选项
                MenuFlyoutItem addGroupItem = new MenuFlyoutItem {
                    Text = "新增分组",
                    Icon = new SymbolIcon(Symbol.Add)
                };
                addGroupItem.Click += async (s2, e2) => {
                    var dlg = new InputDialog { TitleText = "新增分组", Input = string.Empty };
                    dlg.RequireNonEmpty = true;
                    
                    bool ok = await dlg.ShowCenteredAsync();
                    if (ok)
                    {
                        string groupName = dlg.Input?.Trim() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(groupName))
                        {
                            ViewModel.AddGroup(groupName);
                        }
                    }
                };
                manageMenu.Items.Add(addGroupItem);
                
                // 添加分隔符
                manageMenu.Items.Add(new MenuFlyoutSeparator());
                
                // 添加现有分组及其操作
                bool hasCustomGroups = false;
                foreach (var group in ViewModel.Groups) {
                    if (group == "All" || group == "Favorites")
                        continue;
                    
                    hasCustomGroups = true;
                    
                    MenuFlyoutSubItem groupItem = new MenuFlyoutSubItem {
                        Text = group
                    };
                    
                    // 删除分组选项
                    MenuFlyoutItem deleteItem = new MenuFlyoutItem {
                        Text = "删除",
                        Icon = new SymbolIcon(Symbol.Delete)
                    };
                    deleteItem.Click += (s2, e2) => {
                        ViewModel.DeleteGroup(group);
                    };
                    groupItem.Items.Add(deleteItem);
                    
                    // 重命名分组选项
                    MenuFlyoutItem renameItem = new MenuFlyoutItem {
                        Text = "重命名",
                        Icon = new SymbolIcon(Symbol.Edit)
                    };
                    renameItem.Click += async (s2, e2) => {
                        var dlg = new InputDialog { TitleText = "重命名分组", Input = group };
                        dlg.RequireNonEmpty = true;
                        
                        var existingGroups = ViewModel.Groups.Where(g => g != "All" && g != "Favorites").ToList();
                        dlg.Validator = (text) => {
                            if (string.IsNullOrWhiteSpace(text))
                                return "名称不能为空";
                            if (text != group && existingGroups.Contains(text))
                                return "分组名称已存在";
                            return null;
                        };
                        
                        bool ok = await dlg.ShowCenteredAsync();
                        if (ok)
                        {
                            string newGroupName = dlg.Input?.Trim() ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(newGroupName) && newGroupName != group)
                            {
                                ViewModel.RenameGroup(group, newGroupName);
                            }
                        }
                    };
                    groupItem.Items.Add(renameItem);
                    
                    manageMenu.Items.Add(groupItem);
                }
                
                // 如果没有自定义分组，添加提示
                if (!hasCustomGroups) {
                    MenuFlyoutItem noGroupsItem = new MenuFlyoutItem {
                        Text = "暂无自定义分组",
                        IsEnabled = false
                    };
                    manageMenu.Items.Add(noGroupsItem);
                }
                
                // 显示管理菜单
                manageMenu.ShowAt(_groupComboBox);
            };
            contextMenu.Items.Add(manageGroupsItem);
            
            // 为 ComboBox 添加右键菜单
            _groupComboBox.ContextFlyout = contextMenu;

            // 设置R18筛选
            _r18ComboBox.Items.Add(new ComboBoxItem { Content = "全部", Tag = ViewModels.MainViewModel.R18FilterMode.All });
            _r18ComboBox.Items.Add(new ComboBoxItem { Content = "隐藏R18", Tag = ViewModels.MainViewModel.R18FilterMode.Hide });
            _r18ComboBox.Items.Add(new ComboBoxItem { Content = "仅R18", Tag = ViewModels.MainViewModel.R18FilterMode.Only });
            _r18ComboBox.SelectedIndex = 0;
            _r18ComboBox.SelectionChanged += (s, e) =>
            {
                if (_r18ComboBox.SelectedItem is ComboBoxItem item && item.Tag is ViewModels.MainViewModel.R18FilterMode mode)
                {
                    ViewModel.R18Mode = mode;
                    UpdateUI();
                }
            };

            // 绑定 Loaded 以便找到内部 ScrollViewer，并绑定按住拖动相关事件
            _wallpaperList.Loaded += (s, e) => {
                // 找到内部 ScrollViewer
                _wallpaperScrollViewer = FindScrollViewer(_wallpaperList);
                if (_wallpaperScrollViewer != null)
                {
                    _wallpaperScrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                    _wallpaperScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                }
                
                // 确保在 GridView 加载完成后应用布局
                if (MainGrid != null)
                {
                    MainGrid.UpdateLayout();
                    RecalculateAndApplyColumns(MainGrid.ActualWidth);
                }
            };
            _wallpaperList.PointerPressed += GridView_PointerPressed;
            _wallpaperList.PointerMoved += GridView_PointerMoved;
            _wallpaperList.PointerReleased += GridView_PointerReleased;
            _wallpaperList.PointerCanceled += GridView_PointerCanceled;
            _wallpaperList.PointerCaptureLost += GridView_PointerCaptureLost;

            // 保证能捕获到子元素上的指针事件（handledEventsToo = true）
            _wallpaperList.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(GridView_PointerPressed), true);
            _wallpaperList.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(GridView_PointerMoved), true);
            _wallpaperList.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(GridView_PointerReleased), true);

            // 设置 ItemClick 事件处理程序
            _wallpaperList.ItemClick += WallpaperList_ItemClick;
            // 设置 SelectionChanged 事件处理程序，用于更新选中状态的可视化
            _wallpaperList.SelectionChanged += WallpaperList_SelectionChanged;
        }





        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 在 UI 构建完成后把自定义顶栏注册为窗口标题栏（隐藏系统 caption buttons）
            SetupTitleBar();

            // 应用主题资源
            ApplyThemeResources();

            await ViewModel.LoadWallpapersAsync();
            UpdateUI();
            
            // 初始化布局，确保在数据加载完成后正确应用
            // 使用 DispatcherQueue 确保在 UI 线程执行，并且在数据加载完成后执行
            _ = DispatcherQueue.TryEnqueue(() => {
                if (MainGrid != null)
                {
                    // 强制更新 MainGrid 的布局，确保获取到正确的宽度
                    MainGrid.UpdateLayout();
                    // 重新计算并应用布局
                    RecalculateAndApplyColumns(MainGrid.ActualWidth);
                }
            });
        }

        private void SetupTitleBar()
        {
            try
            {
                var app = App.Current as App;
                if (app?.MainWindow != null && _topBar != null)
                {
                    // Optionally set title bar for drag region if needed
                    // app.MainWindow.SetTitleBar(_topBar);

                    // Apply theme-aware background for title/top bar
                    ApplyThemeResources();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetupTitleBar failed: {ex.Message}");
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ViewModel.SearchText = _searchBox.Text;
            UpdateUI();
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 当 ViewModel 通知 Groups 集合已更改时
            if (e.PropertyName == nameof(MainViewModel.Groups))
            {
                if (_groupComboBox != null)
                {
                    // 重新绑定数据源以强制 UI 刷新下拉列表内容
                    _groupComboBox.ItemsSource = null; // 先清空，防止某些情况下 UI 不重绘
                    _groupComboBox.ItemsSource = ViewModel.Groups;
                    _groupComboBox.SelectedItem = ViewModel.SelectedGroup;
                }
            }
        }

        private void ApplyThemeResources()
        {
            try
            {
                var res = App.Current.Resources;

                // Category bar background
                if (_topBar != null && res.ContainsKey("SurfaceBrush") && res["SurfaceBrush"] is SolidColorBrush surfaceBrush)
                {
                    var s = surfaceBrush.Color;
                    _topBar.Background = new SolidColorBrush(Color.FromArgb(220, s.R, s.G, s.B));
                }

                // Search box background and border
                if (_searchBox != null && res.ContainsKey("SurfaceBrush") && res["SurfaceBrush"] is SolidColorBrush sb)
                {
                    var sc = sb.Color;
                    _searchBox.Background = new SolidColorBrush(Color.FromArgb(180, sc.R, sc.G, sc.B));
                    if (res.ContainsKey("BorderBrush") && res["BorderBrush"] is SolidColorBrush bb)
                        _searchBox.BorderBrush = bb;
                    if (res.ContainsKey("TextPrimaryBrush") && res["TextPrimaryBrush"] is SolidColorBrush searchTpb)
                        _searchBox.Foreground = searchTpb;
                }

                // ComboBox styles
                if (_groupComboBox != null && res.ContainsKey("SurfaceBrush") && res["SurfaceBrush"] is SolidColorBrush cbBrush)
                {
                    var cbc = cbBrush.Color;
                    _groupComboBox.Background = new SolidColorBrush(Color.FromArgb(180, cbc.R, cbc.G, cbc.B));
                    if (res.ContainsKey("BorderBrush") && res["BorderBrush"] is SolidColorBrush cbb)
                        _groupComboBox.BorderBrush = cbb;
                    if (res.ContainsKey("TextPrimaryBrush") && res["TextPrimaryBrush"] is SolidColorBrush cbt)
                        _groupComboBox.Foreground = cbt;
                }

                if (_r18ComboBox != null && res.ContainsKey("SurfaceBrush") && res["SurfaceBrush"] is SolidColorBrush r18Brush)
                {
                    var r18c = r18Brush.Color;
                    _r18ComboBox.Background = new SolidColorBrush(Color.FromArgb(180, r18c.R, r18c.G, r18c.B));
                    if (res.ContainsKey("BorderBrush") && res["BorderBrush"] is SolidColorBrush r18bb)
                        _r18ComboBox.BorderBrush = r18bb;
                    if (res.ContainsKey("TextPrimaryBrush") && res["TextPrimaryBrush"] is SolidColorBrush r18t)
                        _r18ComboBox.Foreground = r18t;
                }

                // Category buttons / text color
                if (res.ContainsKey("TextPrimaryBrush") && res["TextPrimaryBrush"] is SolidColorBrush tpb)
                {
                    if (_allButton != null) _allButton.Foreground = tpb;
                    if (_favoritesButton != null) _favoritesButton.Foreground = tpb;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyThemeResources failed: {ex.Message}");
            }
        }

        private T? FindParentOfType<T>(DependencyObject? start) where T : DependencyObject
        {
            while (start != null)
            {
                if (start is T t) return t;
                start = VisualTreeHelper.GetParent(start);
            }
            return null;
        }

        private void DebounceRecalculate(double newWidth)
        {
            _debounceCts?.Cancel();
            _debounceCts = new System.Threading.CancellationTokenSource();
            var ct = _debounceCts.Token;

            System.Threading.Tasks.Task.Delay(DebounceDelayMs).ContinueWith((t) =>
            {
                if (ct.IsCancellationRequested) return;
                // 在 UI 线程执行
                _ = DispatcherQueue.TryEnqueue(() => RecalculateAndApplyColumns(newWidth));
            }, ct);
        }

        private void RecalculateAndApplyColumns(double containerWidth)
        {
            if (_wallpaperList == null) return;

            // 1) 可用宽度（减去根容器 padding）
            double usableWidth = Math.Max(0, containerWidth - RootHorizontalPadding);

            // 防护：如果 usableWidth 非法，退回到 ActualWidth 的近似
            if (usableWidth <= 0)
            {
                usableWidth = Math.Max(0, this.ActualWidth - RootHorizontalPadding);
            }

            // 2) 计算最少列数使单项宽度 <= MaxItemWidthThreshold
            // 核心算法：当窗口宽度增大导致单列宽度超过阈值时，立即增加列数
            // 公式：columns = ceil(usableWidth / MaxItemWidthThreshold)
            int columns = (int)Math.Ceiling(usableWidth / MaxItemWidthThreshold);
            columns = Math.Max(1, columns);
            if (MaxColumnsLimit > 0) columns = Math.Min(MaxColumnsLimit, columns);

            // 3) 计算每列最终可用宽度：扣除每列的左右 margin 总和
            // total horizontal margin for C columns = (C - 1) * ItemHorizontalMargin
            // 因为第一个项目左边和最后一个项目右边的 margin 会被 GridView 的 Padding 吸收
            double totalMargins = (columns - 1) * ItemHorizontalMargin;

            // 平均分配宽度，确保窗口被占满不要留白
            double itemWidth = Math.Max(0, (usableWidth - totalMargins) / columns);

            // 确保 itemWidth 不超过阈值（额外保险）
            if (itemWidth > MaxItemWidthThreshold)
            {
                // 如果仍然超过，增加列数直到满足（或达到 max limit）
                while (itemWidth > MaxItemWidthThreshold && columns < MaxColumnsLimit)
                {
                    columns++;
                    totalMargins = (columns - 1) * ItemHorizontalMargin;
                    itemWidth = Math.Max(0, (usableWidth - totalMargins) / columns);
                }
            }

            // 最后保护：itemWidth 至少 1
            if (itemWidth < 1) itemWidth = 1;

            // 应用计算结果到 ItemsWrapGrid
            if (_wallpaperList.ItemsPanelRoot is ItemsWrapGrid wrapGrid)
            {
                // 强制设置 ItemWidth，确保布局正确
                wrapGrid.ItemWidth = itemWidth;
                // 强制更新布局
                wrapGrid.UpdateLayout();
            }
            else
            {
                // 如果 ItemsPanelRoot 不是 ItemsWrapGrid，尝试重新设置 ItemsPanel
                var panelXaml = "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' HorizontalAlignment='Stretch' VerticalAlignment='Top' ItemWidth='" + itemWidth + "'/></ItemsPanelTemplate>";
                _wallpaperList.ItemsPanel = Microsoft.UI.Xaml.Markup.XamlReader.Load(panelXaml) as ItemsPanelTemplate;
                // 强制更新布局
                _wallpaperList.UpdateLayout();
            }

            // 可选：在列数切换时记录日志 / 触发动画
            if (_lastAppliedColumns != columns)
            {
                _lastAppliedColumns = columns;
                // 可以在这里添加过渡动画逻辑
            }

            // 强制重新测量布局
            _wallpaperList.InvalidateMeasure();
        }

        private void UpdateLayoutForWidth()
        {
            // 保留旧方法以保持兼容性，但实际使用新的防抖方法
            if (MainGrid != null)
            {
                DebounceRecalculate(MainGrid.ActualWidth);
            }
        }

        // 页面加载事件处理程序
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 页面加载时的处理逻辑
            if (MainGrid != null)
            {
                MainGrid.UpdateLayout();
                RecalculateAndApplyColumns(MainGrid.ActualWidth);
            }
        }

        // 页面大小变化事件处理程序
        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 页面大小变化时的处理逻辑
            DebounceRecalculate(e.NewSize.Width);
        }



        private void AllButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedCategory = "All";
            UpdateUI();
        }

        private void FavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.SelectedCategory = "Favorites";
            UpdateUI();
        }

        private void UpdateUI()
        {
            // Update loading state
            if (_loadingRing != null)
            {
                _loadingRing.Visibility = ViewModel.IsLoading ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Update wallpaper list (responsive + layout-aware)
            if (!ViewModel.IsLoading)
            {
                if (_wallpaperList != null)
                {
                    _wallpaperList.Items.Clear();

                    foreach (var wp in ViewModel.Wallpapers)
                    {
                        var item = new WallpaperItem { Wallpaper = wp, IsBatchMode = _isBatchMode };
                        // Click handling
                        item.Tapped += WallpaperItem_Tapped;
                        _wallpaperList.Items.Add(item);
                    }

                    _wallpaperList.Visibility = ViewModel.Wallpapers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    
                    // 数据更新后重新计算布局
                    if (MainGrid != null)
                    {
                        MainGrid.UpdateLayout();
                        RecalculateAndApplyColumns(MainGrid.ActualWidth);
                    }
                }
                if (_emptyText != null)
                {
                    _emptyText.Visibility = ViewModel.Wallpapers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else
            {
                if (_wallpaperList != null)
                {
                    _wallpaperList.Visibility = Visibility.Collapsed;
                }
                if (_emptyText != null)
                {
                    _emptyText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void WallpaperList_ItemClick(object sender, ItemClickEventArgs e)
        {
            // 如果处于批量模式，直接返回，不执行切换壁纸逻辑
            if (_isBatchMode) return;

            // 注意：现在 ListView 的项类型是 WallpaperItem，而不是 WallpaperInfo
            if (e.ClickedItem is WallpaperItem item && item.Wallpaper != null)
            {
                var wallpaper = item.Wallpaper;
                ViewModel.SelectedWallpaper = wallpaper;

                if (ViewModel.Monitors.Count > 1)
                {
                    ShowMonitorSelectionMenu(wallpaper);
                }
                else
                {
                    ViewModel.SwitchWallpaper();
                }
            }
        }

        private void WallpaperList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 遍历所有壁纸项，更新 IsSelected 属性
            foreach (var item in _wallpaperList.Items)
            {
                if (item is WallpaperItem wallpaperItem)
                {
                    wallpaperItem.IsSelected = _wallpaperList.SelectedItems.Contains(item);
                }
            }
        }

        // 处理直接点击 WallpaperItem（内层控件可能会吞掉 GridView 的 ItemClick）
        private void WallpaperItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            // 如果处于批量模式，直接返回，不执行切换壁纸逻辑
            if (_isBatchMode) return;

            if (sender is WallpaperItem item && item.Wallpaper != null)
            {
                var wallpaper = item.Wallpaper;
                ViewModel.SelectedWallpaper = wallpaper;

                if (ViewModel.Monitors.Count > 1)
                {
                    ShowMonitorSelectionMenu(wallpaper);
                }
                else
                {
                    ViewModel.SwitchWallpaper();
                }
            }
        }

        // Helper: 在元素树中查找第一个 ScrollViewer（用于 GridView 的内部滚动器）
        private ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            if (parent == null)
                return null;

            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer sv)
                    return sv;

                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }

            return null;
        }

        private void GridView_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is GridView gv)
            {
                _wallpaperScrollViewer = FindScrollViewer(gv);
                if (_wallpaperScrollViewer != null)
                {
                    _wallpaperScrollViewer.VerticalScrollMode = ScrollMode.Enabled;
                    _wallpaperScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                }
            }
        }

        private void GridView_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            try
            {
                // 停止惯性（如果正在运行）
                StopInertia();

                var pt = e.GetCurrentPoint(_wallpaperList);
                if (pt.Properties.IsLeftButtonPressed && _wallpaperList != null)
                {
                    // 在批量模式下，不执行拖拽逻辑，避免干扰选择
                    if (!_isBatchMode)
                    {
                        _isGridPointerPressed = true;
                        _pointerStartY = pt.Position.Y;
                        if (_wallpaperScrollViewer != null)
                            _initialVerticalOffset = _wallpaperScrollViewer.VerticalOffset;

                        // 清空历史记录并记录起点
                        _pointerHistory.Clear();
                        _pointerHistory.Add((pt.Position.Y, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

                        _wallpaperList.CapturePointer(e.Pointer);
                    }
                    // 在批量模式下，不设置 e.Handled = true，让 GridView 能够处理选择
                }
            }
            catch { }
        }

        private void GridView_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // 在批量模式下，不执行拖拽逻辑，避免干扰选择
            if (_isBatchMode) return;

            if (!_isGridPointerPressed || _wallpaperScrollViewer == null)
                return;

            var pt = e.GetCurrentPoint(_wallpaperList);
            double delta = pt.Position.Y - _pointerStartY;
            double newOffset = _initialVerticalOffset - delta;

            // 记录位置及时间用于惯性计算（只保留最近 150ms 的点）
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _pointerHistory.Add((pt.Position.Y, now));
            while (_pointerHistory.Count > 2 && now - _pointerHistory[0].Time > 150)
                _pointerHistory.RemoveAt(0);

            // 限制范围
            if (newOffset < 0) newOffset = 0;
            if (_wallpaperScrollViewer.ScrollableHeight > 0 && newOffset > _wallpaperScrollViewer.ScrollableHeight)
                newOffset = _wallpaperScrollViewer.ScrollableHeight;

            _wallpaperScrollViewer.ChangeView(null, newOffset, null, true);
            e.Handled = true;
        }

        private void GridView_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // 在批量模式下，不执行惯性相关的逻辑，避免干扰选择
            if (_isBatchMode)
            {
                _isGridPointerPressed = false;
                try { _wallpaperList?.ReleasePointerCapture(e.Pointer); } catch { }
                _pointerHistory.Clear();
                return;
            }

            if (_isGridPointerPressed && _wallpaperList != null)
            {
                _isGridPointerPressed = false;
                try { _wallpaperList.ReleasePointerCapture(e.Pointer); } catch { }

                // 计算速度（像素/毫秒），使用历史记录的首尾点
                if (_pointerHistory.Count >= 2 && _wallpaperScrollViewer != null)
                {
                    var first = _pointerHistory[0];
                    var last = _pointerHistory[_pointerHistory.Count - 1];
                    double dy = last.Y - first.Y; // 向下为正
                    long dt = last.Time - first.Time; // ms
                    if (dt > 0)
                    {
                        double velocity = dy / dt; // px per ms (positive down)
                        // 转换为滚动方向的速度（offset 增加 = scroll down）
                        _inertiaVelocity = -velocity;

                        if (Math.Abs(velocity) >= InertiaThreshold)
                        {
                            StartInertia();
                        }
                    }
                }

                _pointerHistory.Clear();
            }
        }

        private void GridView_PointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // 中断同等于释放
            _isGridPointerPressed = false;
            try { _wallpaperList?.ReleasePointerCapture(e.Pointer); } catch { }
        }

        private void GridView_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isGridPointerPressed = false;
        }

        private void StartInertia()
        {
            if (_inertiaTimer != null)
                return;

            _inertiaTimer = new Microsoft.UI.Xaml.DispatcherTimer();
            _inertiaTimer.Interval = TimeSpan.FromMilliseconds(16);
            long lastTick = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _inertiaTimer.Tick += (s, e) =>
            {
                if (_wallpaperScrollViewer == null)
                {
                    StopInertia();
                    return;
                }

                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                double dt = Math.Max(1, now - lastTick); // ms
                lastTick = now;

                // 计算位移（offset += velocity * dt）
                double deltaOffset = _inertiaVelocity * dt;
                double newOffset = _wallpaperScrollViewer.VerticalOffset + deltaOffset;

                // 检查边界
                if (newOffset < 0)
                {
                    newOffset = 0;
                    StopInertia();
                }
                else if (_wallpaperScrollViewer.ScrollableHeight > 0 && newOffset > _wallpaperScrollViewer.ScrollableHeight)
                {
                    newOffset = _wallpaperScrollViewer.ScrollableHeight;
                    StopInertia();
                }

                _wallpaperScrollViewer.ChangeView(null, newOffset, null, true);

                // 应用摩擦
                _inertiaVelocity *= Math.Pow(InertiaFrictionPerTick, dt / 16.0);
                if (Math.Abs(_inertiaVelocity) < 0.02)
                {
                    StopInertia();
                }
            };
            _inertiaTimer.Start();
        }

        private void StopInertia()
        {
            try
            {
                if (_inertiaTimer != null)
                {
                    _inertiaTimer.Stop();
                    _inertiaTimer = null;
                }
            }
            catch { }
        }

        private void ShowMonitorSelectionMenu(WallpaperInfo wallpaper)
        {
            // Create a menu flyout for monitor selection
            MenuFlyout menuFlyout = new MenuFlyout();

            // Add "Apply to All Monitors" menu item
            MenuFlyoutItem allMonitorsItem = new MenuFlyoutItem { Text = "Apply to All Monitors" };
            allMonitorsItem.Click += (sender, e) => ViewModel.SwitchWallpaper(-1);
            menuFlyout.Items.Add(allMonitorsItem);

            // Add separator
            menuFlyout.Items.Add(new MenuFlyoutSeparator());

            // Add monitor-specific menu items
            foreach (var monitor in ViewModel.Monitors)
            {
                MenuFlyoutItem monitorItem = new MenuFlyoutItem { Text = $"Apply to Monitor {monitor.Index + 1}" };
                int monitorIndex = monitor.Index;
                monitorItem.Click += (sender, e) => ViewModel.SwitchWallpaper(monitorIndex);
                menuFlyout.Items.Add(monitorItem);
            }

            // Show the menu flyout at the cursor position
            menuFlyout.ShowAt(this);
        }

        private async void MicrophoneButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isListening)
                {
                    // Stop listening
                    await StopSpeechRecognitionAsync();
                }
                else
                {
                    // Start listening
                    await StartSpeechRecognitionAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in MicrophoneButton_Click: {ex.Message}");
                ShowToastNotification("错误", $"麦克风按钮点击失败: {ex.Message}", true);
            }
        }

        private async Task StartSpeechRecognitionAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Starting speech recognition...");
                
                // Initialize speech recognizer with specified language
                if (_speechRecognizer == null)
                {
                    System.Diagnostics.Debug.WriteLine("Initializing SpeechRecognizer...");
                    // Specify Chinese language to avoid language pack issues
                    _speechRecognizer = new SpeechRecognizer(new Windows.Globalization.Language("zh-CN"));
                    _speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
                    _speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;
                    System.Diagnostics.Debug.WriteLine("SpeechRecognizer initialized successfully");
                }

                // Compile grammar
                System.Diagnostics.Debug.WriteLine("Compiling constraints...");
                await _speechRecognizer.CompileConstraintsAsync();
                System.Diagnostics.Debug.WriteLine("Constraints compiled successfully");

                // Start continuous recognition
                System.Diagnostics.Debug.WriteLine("Starting continuous recognition...");
                await _speechRecognizer.ContinuousRecognitionSession.StartAsync(SpeechContinuousRecognitionMode.Default);
                System.Diagnostics.Debug.WriteLine("Continuous recognition started successfully");

                _isListening = true;
                UpdateMicrophoneButtonState(true);
                System.Diagnostics.Debug.WriteLine("Speech recognition started successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting speech recognition: {ex.Message}, HResult: {ex.HResult}");
                
                // Show toast notification with error message
                ShowToastNotification("语音识别错误", $"语音识别启动失败: {ex.Message}\n错误代码: {ex.HResult.ToString("X8")}", true);
            }
        }

        private void ShowToastNotification(string title, string message, bool showSpeechLinks = false)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                // Create and show toast notification window
                var toastWindow = new ToastNotificationWindow(title, message, showSpeechLinks);
                toastWindow.ShowWithoutActivation();
            });
        }

        private async Task StopSpeechRecognitionAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Stopping speech recognition...");
                
                if (_speechRecognizer != null && _isListening)
                {
                    await _speechRecognizer.ContinuousRecognitionSession.StopAsync();
                    _isListening = false;
                    UpdateMicrophoneButtonState(false);
                    System.Diagnostics.Debug.WriteLine("Speech recognition stopped successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Speech recognition is not running");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping speech recognition: {ex.Message}");
                ShowToastNotification("错误", $"停止语音识别失败: {ex.Message}", true);
            }
        }

        private void UpdateMicrophoneButtonState(bool isListening)
        {
            if (_microphoneButton != null)
            {
                // Change button appearance based on listening state
                if (isListening)
                {
                    // Set active state (e.g., change color or icon)
                    _microphoneButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightBlue);
                }
                else
                {
                    // Set inactive state
                    _microphoneButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                }
            }
        }

        private void UpdateButtonsVisibility()
        {
            try
            {
                var app = App.Current as App;
                if (app != null)
                {
                    // Check if window is in dock mode (true) or fullscreen (false)
                    // We can determine this by checking the window's presenter kind
                    bool isDockMode = app.MainWindow.AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen;
                    
                    // Show buttons only in fullscreen mode
                    Visibility buttonsVisibility = isDockMode ? Visibility.Collapsed : Visibility.Visible;
                    
                    if (_openWallpaperEngineButton != null)
                        _openWallpaperEngineButton.Visibility = buttonsVisibility;
                    if (_batchOperationsButton != null)
                        _batchOperationsButton.Visibility = buttonsVisibility;
                    if (_schedulePlayButton != null)
                        _schedulePlayButton.Visibility = buttonsVisibility;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateButtonsVisibility failed: {ex.Message}");
            }
        }

        private void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
        {
            if (args.Result.Status == SpeechRecognitionResultStatus.Success)
            {
                // Update search box with recognized text
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_searchBox != null)
                    {
                        _searchBox.Text = args.Result.Text;
                        // Trigger search
                        ViewModel.SearchText = args.Result.Text;
                        UpdateUI();
                    }
                });
            }
        }

        private void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
        {
            _isListening = false;
            UpdateMicrophoneButtonState(false);
        }

        private void OpenWallpaperEngineButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Console.WriteLine("点击了打开 Wallpaper Engine 按钮");
                
                var wallpaperService = App.Current.Services.GetService(typeof(IWallpaperService)) as IWallpaperService;
                if (wallpaperService != null)
                {
                    Console.WriteLine("成功获取 Wallpaper Engine 服务");
                    wallpaperService.OpenWallpaperEngineMainWindow();
                    Console.WriteLine("已调用 OpenWallpaperEngineMainWindow 方法");
                }
                else
                {
                    Console.WriteLine("错误：无法获取 Wallpaper Engine 服务");
                    ShowToastNotification("错误", "无法获取 Wallpaper Engine 服务");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误：打开 Wallpaper Engine 失败: {ex.Message}");
                ShowToastNotification("错误", $"打开 Wallpaper Engine 失败: {ex.Message}");
            }
        }

        private void BatchOperationsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 切换批量操作模式
                _isBatchMode = !_isBatchMode;

                // 更新批量操作按钮的图标颜色
                if (_batchOperationsButton != null)
                {
                    // 遍历视觉树找到图标元素并修改颜色
                    if (_batchOperationsButton.Content is Viewbox viewbox)
                    {
                        if (viewbox.Child is Canvas canvas)
                        {
                            foreach (var child in canvas.Children)
                            {
                                if (child is Rectangle rect)
                                {
                                    rect.Stroke = new SolidColorBrush(_isBatchMode ? Microsoft.UI.Colors.Blue : Microsoft.UI.Colors.Gray);
                                }
                                else if (child is Microsoft.UI.Xaml.Shapes.Path path)
                                {
                                    path.Stroke = new SolidColorBrush(_isBatchMode ? Microsoft.UI.Colors.Blue : Microsoft.UI.Colors.Gray);
                                }
                            }
                        }
                    }
                }

                // 显示或隐藏批量工具栏
                if (_isBatchMode)
                {
                    ShowBatchToolbar();
                }
                else
                {
                    HideBatchToolbar();
                }

                // 更新 GridView 的 SelectionMode
                if (_wallpaperList != null)
                {
                    if (_isBatchMode)
                    {
                        // 进入多选模式
                        _wallpaperList.SelectionMode = ListViewSelectionMode.Multiple;
                        // 关键：禁用 ItemClick 识别，让点击行为直接作用于 Selection
                        _wallpaperList.IsItemClickEnabled = false;
                        // 防止拖拽逻辑干扰多选选择
                        _wallpaperList.CanDragItems = false;
                    }
                    else
                    {
                        // 退出多选模式
                        _wallpaperList.SelectionMode = ListViewSelectionMode.None;
                        // 恢复单选点击识别
                        _wallpaperList.IsItemClickEnabled = true;
                        // 恢复拖拽功能
                        _wallpaperList.CanDragItems = true;
                        _wallpaperList.SelectedItems.Clear();
                    }

                    // 更新现有 WallpaperItem 的 IsBatchMode 属性
                    foreach (var item in _wallpaperList.Items)
                    {
                        if (item is WallpaperItem wallpaperItem)
                        {
                            wallpaperItem.IsBatchMode = _isBatchMode;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 只在进入批量模式失败时显示错误提示，退出时不显示
                if (_isBatchMode)
                {
                    ShowToastNotification("错误", $"切换批量操作模式失败: {ex.Message}");
                }
            }
        }

        private void ShowBatchToolbar()
        {
            // 创建并显示批量工具栏
            if (_batchToolbar == null && _batchToolbarContainer != null)
            {
                // 创建批量工具栏
                _batchToolbar = new Grid
                {
                    Height = 48,
                    Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    Margin = new Microsoft.UI.Xaml.Thickness(16, 0, 16, 0)
                };

                // 设置列定义
                _batchToolbar.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                _batchToolbar.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                _batchToolbar.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                _batchToolbar.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                _batchToolbar.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
                _batchToolbar.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });

                // 添加按钮
                AddBatchToolbarButton(_batchToolbar, 0, "添加到分组", AddToGroupButton_Click);
                AddBatchToolbarButton(_batchToolbar, 1, "批量删除", BatchDeleteButton_Click);
                AddBatchToolbarButton(_batchToolbar, 2, "添加到播放列表", AddToPlaylistButton_Click);
                AddBatchToolbarButton(_batchToolbar, 3, "全选", SelectAllButton_Click);

                // 将工具栏添加到批量工具栏容器中
                if (_batchToolbarContainer.Children.Count > 0)
                {
                    _batchToolbarContainer.Children.Clear();
                }
                _batchToolbarContainer.Children.Add(_batchToolbar);
                _batchToolbarContainer.Visibility = Visibility.Visible;
            }
            else if (_batchToolbar != null && _batchToolbarContainer != null)
            {
                _batchToolbar.Visibility = Visibility.Visible;
                _batchToolbarContainer.Visibility = Visibility.Visible;
            }
        }

        private void HideBatchToolbar()
        {
            // 隐藏批量工具栏
            if (_batchToolbar != null)
            {
                _batchToolbar.Visibility = Visibility.Collapsed;
            }
            if (_batchToolbarContainer != null)
            {
                _batchToolbarContainer.Visibility = Visibility.Collapsed;
            }
        }

        private void AddBatchToolbarButton(Grid parent, int column, string text, RoutedEventHandler clickHandler)
        {
            Button button = new Button
            {
                Content = text,
                Margin = new Microsoft.UI.Xaml.Thickness(8, 8, 0, 8),
                Padding = new Microsoft.UI.Xaml.Thickness(12, 6, 12, 6),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
                BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4)
            };
            button.Click += clickHandler;
            Grid.SetColumn(button, column);
            parent.Children.Add(button);
        }

        private void AddToGroupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_wallpaperList != null && _wallpaperList.SelectedItems.Count > 0)
                {
                    // 获取所有可用的分组
                    var vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
                    if (vm != null)
                    {
                        // 创建分组选择器
                        MenuFlyout groupMenu = new MenuFlyout();

                        // 添加分组选项
                        foreach (var group in vm.Groups)
                        {
                            MenuFlyoutItem groupItem = new MenuFlyoutItem { Text = group };
                            groupItem.Click += (s, args) =>
                            {
                                // 将选中的壁纸添加到选定的分组
                                AddSelectedWallpapersToGroup(group);
                            };
                            groupMenu.Items.Add(groupItem);
                        }

                        // 添加分隔符
                        groupMenu.Items.Add(new MenuFlyoutSeparator());

                        // 添加新建分组选项
                        MenuFlyoutItem newGroupItem = new MenuFlyoutItem { Text = "新建分组" };
                        newGroupItem.Click += async (s, args) =>
                        {
                            // 弹出新建分组对话框
                            var dlg = new InputDialog { TitleText = "新建分组", Input = string.Empty };
                            dlg.RequireNonEmpty = true;

                            bool ok = await dlg.ShowCenteredAsync();
                            if (ok)
                            {
                                string groupName = dlg.Input?.Trim() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(groupName))
                                {
                                    // 添加新分组
                                    vm.AddGroup(groupName);
                                    // 将选中的壁纸添加到新分组
                                    AddSelectedWallpapersToGroup(groupName);
                                }
                            }
                        };
                        groupMenu.Items.Add(newGroupItem);

                        // 显示分组选择器
                        groupMenu.ShowAt(sender as FrameworkElement);
                    }
                }
                else
                {
                    ShowToastNotification("提示", "请先选择要添加到分组的壁纸");
                }
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"批量添加分组失败: {ex.Message}");
            }
        }

        private void AddSelectedWallpapersToGroup(string groupName)
        {
            try
            {
                if (_wallpaperList != null && _wallpaperList.SelectedItems.Count > 0)
                {
                    var vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
                    if (vm != null)
                    {
                        int count = 0;
                        foreach (var item in _wallpaperList.SelectedItems)
                        {
                            if (item is WallpaperItem wallpaperItem && wallpaperItem.Wallpaper != null)
                            {
                                // 将壁纸添加到分组
                                vm.AddWallpaperToGroup(wallpaperItem.Wallpaper, groupName);
                                count++;
                            }
                        }
                        ShowToastNotification("成功", $"已将 {count} 个壁纸添加到分组 '{groupName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"添加到分组失败: {ex.Message}");
            }
        }

        private void BatchDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_wallpaperList != null && _wallpaperList.SelectedItems.Count > 0)
                {
                    // 弹出确认对话框
                    var confirmDialog = new ContentDialog
                    {
                        Title = "确认删除",
                        Content = $"确定要从列表中移除选定的 {_wallpaperList.SelectedItems.Count} 个壁纸吗？",
                        PrimaryButtonText = "确定",
                        SecondaryButtonText = "取消"
                    };

                    // 设置对话框的 XamlRoot
                    if (this.Content is FrameworkElement rootElement)
                    {
                        confirmDialog.XamlRoot = rootElement.XamlRoot;
                    }

                    // 显示对话框并处理结果
                    confirmDialog.PrimaryButtonClick += async (s, args) =>
                    {
                        // 执行批量删除操作
                        int count = DeleteSelectedWallpapers();
                        // 删除后刷新壁纸列表，体现删除结果
                        UpdateUI();
                        ShowToastNotification("成功", $"已从列表中移除 {count} 个壁纸");
                    };

                    confirmDialog.ShowAsync();
                }
                else
                {
                    ShowToastNotification("提示", "请先选择要删除的壁纸");
                }
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"批量删除失败: {ex.Message}");
            }
        }

        private int DeleteSelectedWallpapers()
        {
            int count = 0;
            try
            {
                if (_wallpaperList != null && _wallpaperList.SelectedItems.Count > 0)
                {
                    var vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
                    if (vm != null)
                    {
                        // 创建要删除的壁纸列表
                        List<WallpaperInfo> wallpapersToDelete = new List<WallpaperInfo>();
                        foreach (var item in _wallpaperList.SelectedItems)
                        {
                            if (item is WallpaperItem wallpaperItem && wallpaperItem.Wallpaper != null)
                            {
                                wallpapersToDelete.Add(wallpaperItem.Wallpaper);
                            }
                        }

                        // 从列表中移除壁纸
                        foreach (var wallpaper in wallpapersToDelete)
                        {
                            vm.RemoveWallpaper(wallpaper);
                            count++;
                        }

                        // 清空选择
                        _wallpaperList.SelectedItems.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"删除壁纸失败: {ex.Message}");
            }
            return count;
        }

        private void AddToPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_wallpaperList != null && _wallpaperList.SelectedItems.Count > 0)
                {
                    // 实现添加到播放列表功能
                    // 这里需要与定时播放服务交互
                    var vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
                    if (vm != null)
                    {
                        // 创建播放列表选择器
                        MenuFlyout playlistMenu = new MenuFlyout();

                        // 添加播放列表选项
                        // 这里假设 MainViewModel 中有 Playlists 属性
                        if (vm.Playlists != null && vm.Playlists.Count > 0)
                        {
                            foreach (var playlist in vm.Playlists)
                            {
                                MenuFlyoutItem playlistItem = new MenuFlyoutItem { Text = playlist };
                                playlistItem.Click += (s, args) =>
                                {
                                    // 将选中的壁纸添加到选定的播放列表
                                    AddSelectedWallpapersToPlaylist(playlist);
                                };
                                playlistMenu.Items.Add(playlistItem);
                            };
                        }
                        else
                        {
                            MenuFlyoutItem noPlaylistItem = new MenuFlyoutItem { Text = "暂无播放列表", IsEnabled = false };
                            playlistMenu.Items.Add(noPlaylistItem);
                        }

                        // 添加分隔符
                        playlistMenu.Items.Add(new MenuFlyoutSeparator());

                        // 添加新建播放列表选项
                        MenuFlyoutItem newPlaylistItem = new MenuFlyoutItem { Text = "新建播放列表" };
                        newPlaylistItem.Click += async (s, args) =>
                        {
                            // 弹出新建播放列表对话框
                            var dlg = new InputDialog { TitleText = "新建播放列表", Input = string.Empty };
                            dlg.RequireNonEmpty = true;

                            bool ok = await dlg.ShowCenteredAsync();
                            if (ok)
                            {
                                string playlistName = dlg.Input?.Trim() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(playlistName))
                                {
                                    // 添加新播放列表
                                    vm.AddPlaylist(playlistName);
                                    // 将选中的壁纸添加到新播放列表
                                    AddSelectedWallpapersToPlaylist(playlistName);
                                }
                            }
                        };
                        playlistMenu.Items.Add(newPlaylistItem);

                        // 显示播放列表选择器
                        playlistMenu.ShowAt(sender as FrameworkElement);
                    }
                }
                else
                {
                    ShowToastNotification("提示", "请先选择要添加到播放列表的壁纸");
                }
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"添加到播放列表失败: {ex.Message}");
            }
        }

        private void AddSelectedWallpapersToPlaylist(string playlistName)
        {
            try
            {
                if (_wallpaperList != null && _wallpaperList.SelectedItems.Count > 0)
                {
                    var vm = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
                    if (vm != null)
                    {
                        int count = 0;
                        foreach (var item in _wallpaperList.SelectedItems)
                        {
                            if (item is WallpaperItem wallpaperItem && wallpaperItem.Wallpaper != null)
                            {
                                // 将壁纸添加到播放列表
                                vm.AddWallpaperToPlaylist(wallpaperItem.Wallpaper, playlistName);
                                count++;
                            }
                        }
                        ShowToastNotification("成功", $"已将 {count} 个壁纸添加到播放列表 '{playlistName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"添加到播放列表失败: {ex.Message}");
            }
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 实现全选功能
                if (_wallpaperList != null)
                {
                    _wallpaperList.SelectAll();
                }
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"全选失败: {ex.Message}");
            }
        }



        private void SchedulePlayButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 打开播放列表编辑器对话框
                var dialog = new PlaylistEditorDialog();
                
                // 设置对话框的 XamlRoot
                if (this.Content is FrameworkElement rootElement)
                {
                    dialog.XamlRoot = rootElement.XamlRoot;
                }
                
                // 显示对话框
                dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                ShowToastNotification("错误", $"打开播放列表编辑器失败: {ex.Message}");
            }
        }
    }
}
