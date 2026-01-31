using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System.Threading.Tasks;
using WallpaperDockWinUI.Services;
using WallpaperDockWinUI.ViewModels;
using Windows.UI;

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
        private ProgressRing? _loadingRing;
        private GridView? _wallpaperList;
        private TextBlock? _emptyText;

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

        public MainPage()
        {
            this.InitializeComponent();
            ViewModel = (MainViewModel)(App.Current.Services.GetService(typeof(MainViewModel)) ?? throw new InvalidOperationException("ViewModel cannot be null"));
            Loaded += MainPage_Loaded;
            BuildUI();
        }

        private void BuildUI()
        {
            MainGrid.Children.Clear();
            MainGrid.RowDefinitions.Clear();

            // 定义三行布局
            // 行 0: 分类筛选栏 (全部/收藏/分组)
            // 行 1: 搜索栏 (新位置)
            // 行 2: 壁纸列表 (填充剩余空间)
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // CategoryBar
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // SearchBar
            MainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List

            // Background overlay to ensure semi-transparent background even when window is inactive
            var backgroundOverlay = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 24, 24, 24)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            MainGrid.Children.Add(backgroundOverlay);

            // --- 1. 分类筛选栏 ---
            _categoryBar = BuildCategoryBar();
            Grid.SetRow(_categoryBar, 0);
            MainGrid.Children.Add(_categoryBar);

            // --- 2. 搜索栏 (美化后的 UI) ---
            var searchContainer = BuildModernSearchBar();
            Grid.SetRow(searchContainer, 1);
            MainGrid.Children.Add(searchContainer);

            // --- 3. 壁纸列表 ---
            // Create loading ring
            _loadingRing = new ProgressRing
            {
                Width = 40,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Visible
            };
            Grid.SetRow(_loadingRing, 2);
            MainGrid.Children.Add(_loadingRing);

            // Create wallpaper grid using GridView to display wallpaper items in a grid
            var gridView = new GridView
            {
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.None,
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            // 隐藏滚动条，但保留滚轮和触摸滚动
            gridView.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            gridView.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            gridView.CanDragItems = false; // 我们自定义按住拖动滚动行为

            // 绑定 Loaded 以便找到内部 ScrollViewer，并绑定按住拖动相关事件
            gridView.Loaded += GridView_Loaded;
            gridView.PointerPressed += GridView_PointerPressed;
            gridView.PointerMoved += GridView_PointerMoved;
            gridView.PointerReleased += GridView_PointerReleased;
            gridView.PointerCanceled += GridView_PointerCanceled;
            gridView.PointerCaptureLost += GridView_PointerCaptureLost;

            // 保证能捕获到子元素上的指针事件（handledEventsToo = true）
            gridView.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(GridView_PointerPressed), true);
            gridView.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(GridView_PointerMoved), true);
            gridView.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(GridView_PointerReleased), true);

            // 设置 ItemContainerStyle 使项靠上布局并有最小高度，避免被裁切
            var itemStyle = new Microsoft.UI.Xaml.Style(typeof(GridViewItem));
            itemStyle.Setters.Add(new Microsoft.UI.Xaml.Setter(FrameworkElement.MinHeightProperty, 140.0));
            itemStyle.Setters.Add(new Microsoft.UI.Xaml.Setter(Control.PaddingProperty, new Thickness(0)));
            itemStyle.Setters.Add(new Microsoft.UI.Xaml.Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            itemStyle.Setters.Add(new Microsoft.UI.Xaml.Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));
            gridView.ItemContainerStyle = itemStyle;

            _wallpaperList = gridView;
            _wallpaperList.ItemClick += WallpaperList_ItemClick;
            Grid.SetRow(_wallpaperList, 2);
            MainGrid.Children.Add(_wallpaperList);

            // Create empty text
            _emptyText = new TextBlock
            {
                Text = "No wallpapers found",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkGray),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_emptyText, 2);
            MainGrid.Children.Add(_emptyText);
        }

        private Grid BuildCategoryBar()
        {
            var categoryBar = new Grid
            {
                Height = 40,
                Padding = new Thickness(16, 0, 16, 0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(160, 30, 30, 30)) // 半透明分类栏
            };

            // Use a Grid with two columns: left for filters, right for the R18 toggle so it stays visible
            var categoryContentGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            categoryContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            categoryContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left stack for filter controls (keeps them grouped)
            var leftStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Group filter 下拉（减小默认宽度并限制最大宽度）
            _groupComboBox = new ComboBox
            {
                Width = 150,
                MaxWidth = 220,
                MinWidth = 80,
                Height = 32,
                Margin = new Thickness(0, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Left
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

            leftStack.Children.Add(_groupComboBox);

            Grid.SetColumn(leftStack, 0);
            categoryContentGrid.Children.Add(leftStack);

            // R18 模式选择器
            var r18ComboBox = new ComboBox
            {
                Width = 90,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };
            
            // 添加三种模式选项
            r18ComboBox.Items.Add(new ComboBoxItem { Content = "全部", Tag = ViewModels.MainViewModel.R18FilterMode.All });
            r18ComboBox.Items.Add(new ComboBoxItem { Content = "隐藏R18", Tag = ViewModels.MainViewModel.R18FilterMode.Hide });
            r18ComboBox.Items.Add(new ComboBoxItem { Content = "仅R18", Tag = ViewModels.MainViewModel.R18FilterMode.Only });
            
            // 设置默认选中项
            r18ComboBox.SelectedIndex = 0;
            
            // 处理选择变化
            r18ComboBox.SelectionChanged += (s, e) =>
            {
                if (r18ComboBox.SelectedItem is ComboBoxItem item && item.Tag is ViewModels.MainViewModel.R18FilterMode mode)
                {
                    ViewModel.R18Mode = mode;
                    UpdateUI();
                }
            };

            Grid.SetColumn(r18ComboBox, 1);
            categoryContentGrid.Children.Add(r18ComboBox);

            // Subscribe to ViewModel changes so we can refresh the group list UI
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            categoryBar.Children.Add(categoryContentGrid);

            return categoryBar;
        }

        private FrameworkElement BuildModernSearchBar()
        {
            // 外部容器，增加边距
            Grid container = new Grid { Padding = new Thickness(16, 8, 16, 12) };

            // 使用 TextBox 并进行深度美化
            _searchBox = new TextBox
            {
                PlaceholderText = "Search...",
                Height = 40,
                CornerRadius = new CornerRadius(8),
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new Thickness(1),
                // 使用半透明白色背景（白色毛玻璃感）
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 0, 0, 0))
            };

            // 搜索框左侧图标
            _searchBox.Header = null; // 确保没有头部标签
            
            // 设置文本改变事件
            _searchBox.TextChanged += SearchBox_TextChanged;

            // 技巧：在 WinUI 3 中通过设置 Placeholder 的交互感来增强视觉
            // 如果想要更高级的"搜索图标在框内"，可以使用 AutoSuggestBox
            var searchIcon = new FontIcon
            {
                Glyph = "\uE11A",
                FontSize = 16,
                Opacity = 0.6,
                Margin = new Thickness(14, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };

            // 为了让图标看起来在框内，我们把图标叠在 TextBox 上
            Grid searchWrapper = new Grid();
            searchWrapper.Children.Add(_searchBox);
            searchWrapper.Children.Add(searchIcon);

            // 调整文字缩进，给图标留位，并确保文本垂直居中
            _searchBox.Padding = new Thickness(40, 8, 12, 8);

            container.Children.Add(searchWrapper);
            return container;
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 在 UI 构建完成后把自定义顶栏注册为窗口标题栏（隐藏系统 caption buttons）
            SetupTitleBar();

            await ViewModel.LoadWallpapersAsync();
            UpdateUI();
        }

        private void SetupTitleBar()
        {
            try
            {
                var app = App.Current as App;
                if (app?.MainWindow != null && _topBar != null)
                {
                    // 不设置标题栏，禁用窗口拖动功能
                    // app.MainWindow.SetTitleBar(_topBar);
                    
                    // 使用半透明背景以配合系统 Mica/Acrylic
                    _topBar.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 20, 20, 20));
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
            
            // Update wallpaper list
            if (!ViewModel.IsLoading)
            {
                if (_wallpaperList != null)
                {
                    // 重要改动：不要直接设置 ItemsSource (会只显示 Title 等)，而是逐项创建 WallpaperItem 控件
                    _wallpaperList.Items.Clear();
                    foreach (var wp in ViewModel.Wallpapers)
                    {
                        var item = new WallpaperItem { Wallpaper = wp };
                        // 适当增加每个项的间距以使布局更舒适（左右留白缩小，垂直间距保留）
                        item.Margin = new Thickness(6, 8, 6, 8);
                        // 左键点击项也要能触发切换壁纸：订阅 Tap 事件
                        item.Tapped += WallpaperItem_Tapped;
                        _wallpaperList.Items.Add(item);
                    }
                    _wallpaperList.Visibility = ViewModel.Wallpapers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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

        // 处理直接点击 WallpaperItem（内层控件可能会吞掉 GridView 的 ItemClick）
        private void WallpaperItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
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
                    _isGridPointerPressed = true;
                    _pointerStartY = pt.Position.Y;
                    if (_wallpaperScrollViewer != null)
                        _initialVerticalOffset = _wallpaperScrollViewer.VerticalOffset;

                    // 清空历史记录并记录起点
                    _pointerHistory.Clear();
                    _pointerHistory.Add((pt.Position.Y, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

                    _wallpaperList.CapturePointer(e.Pointer);
                }
            }
            catch { }
        }

        private void GridView_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
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
    }
}
