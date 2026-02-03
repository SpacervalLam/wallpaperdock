using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using WallpaperDockWinUI.Services;

namespace WallpaperDockWinUI.Views
{
    public sealed partial class WallpaperItem : UserControl
    {
        public static readonly DependencyProperty WallpaperProperty = 
            DependencyProperty.Register(nameof(Wallpaper), typeof(WallpaperInfo), typeof(WallpaperItem), 
                new PropertyMetadata(null, OnWallpaperChanged));

        public static readonly DependencyProperty IsBatchModeProperty = 
            DependencyProperty.Register(nameof(IsBatchMode), typeof(bool), typeof(WallpaperItem), 
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsSelectedProperty = 
            DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(WallpaperItem), 
                new PropertyMetadata(false, OnIsSelectedChanged));

        public bool IsSelected
        {
            get => (bool)GetValue(IsSelectedProperty);
            set => SetValue(IsSelectedProperty, value);
        }

        public bool IsBatchMode
        {
            get => (bool)GetValue(IsBatchModeProperty);
            set => SetValue(IsBatchModeProperty, value);
        }

        private IImageCacheService? _imageCacheService;

        public WallpaperInfo Wallpaper
        {
            get => (WallpaperInfo)GetValue(WallpaperProperty);
            set => SetValue(WallpaperProperty, value);
        }

        private Microsoft.UI.Composition.Compositor? _compositor;
        private Microsoft.UI.Composition.SpriteVisual? _shadowVisual;
        private Microsoft.UI.Composition.Visual? _rootVisual;
        private bool _compositionInitialized = false;

        public WallpaperItem()
        {
            this.InitializeComponent();
            _imageCacheService = App.Current.Services.GetService(typeof(IImageCacheService)) as IImageCacheService;

            // 初始化 composition 动画
            this.Loaded += WallpaperItem_Loaded;
            this.PointerEntered += WallpaperItem_PointerEntered;
            this.PointerExited += WallpaperItem_PointerExited;
            this.SizeChanged += WallpaperItem_SizeChanged;

            // 右键菜单
            this.RightTapped += WallpaperItem_RightTapped;
        }

        private async void WallpaperItem_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (Wallpaper == null || string.IsNullOrEmpty(Wallpaper.ProjectJsonPath))
                return;

            var fav = App.Current.Services.GetService(typeof(IFavoritesService)) as IFavoritesService;

            var menu = new MenuFlyout();

            var silentPlayItem = new MenuFlyoutItem { Text = "静音播放" };
            silentPlayItem.Click += (s, ev) =>
            {
                if (Wallpaper != null && !string.IsNullOrEmpty(Wallpaper.ProjectJsonPath))
                {
                    var service = App.Current.Services.GetService(typeof(IWallpaperService)) as IWallpaperService;
                    service?.SwitchWallpaper(Wallpaper.ProjectJsonPath, -1, true);
                }
            };
            menu.Items.Add(silentPlayItem);

            var r18Item = new Microsoft.UI.Xaml.Controls.ToggleMenuFlyoutItem { Text = "标记为R18", IsChecked = Wallpaper.IsR18 };
            r18Item.Click += (s, ev) =>
            {
                bool newVal = r18Item.IsChecked;
                Wallpaper.IsR18 = newVal;
                fav?.SetR18(Wallpaper.ProjectJsonPath, newVal);
                if (R18Badge != null)
                    R18Badge.Visibility = newVal ? Visibility.Visible : Visibility.Collapsed;

                // 通知 ViewModel 重新应用过滤（如果用户当前隐藏 R18，则需要刷新列表）
                var vm = App.Current.Services.GetService(typeof(ViewModels.MainViewModel)) as ViewModels.MainViewModel;
                vm?.RefreshFilters();
            };
            menu.Items.Add(r18Item);

            var renameItem = new MenuFlyoutItem { Text = "重命名" };
            renameItem.Click += async (s, ev) => await ShowRenameDialogAsync(fav);
            menu.Items.Add(renameItem);

            var groupSub = new MenuFlyoutSubItem { Text = "加入分组" };
            // 设置最大高度，确保分组数量过多时显示滚动条
            groupSub.MaxHeight = 400;
            
            var groups = fav?.GetAllGroups() ?? new System.Collections.Generic.List<string>();
            var wallpaperGroups = Wallpaper.Groups ?? new System.Collections.Generic.List<string>();
            foreach (var g in groups)
            {
                var gi = new ToggleMenuFlyoutItem { Text = g, IsChecked = wallpaperGroups.Contains(g) };
                gi.Click += (s, ev) =>
                {
                    if (s is ToggleMenuFlyoutItem item)
                    {
                        if (item.IsChecked)
                        {
                            // 添加分组
                            if (!wallpaperGroups.Contains(g))
                            {
                                wallpaperGroups.Add(g);
                                Wallpaper.Groups = wallpaperGroups;
                                fav?.AddGroupToWallpaper(Wallpaper.ProjectJsonPath, g);
                            }
                        }
                        else
                        {
                            // 移除分组
                            if (wallpaperGroups.Contains(g))
                            {
                                wallpaperGroups.Remove(g);
                                if (wallpaperGroups.Count == 0)
                                {
                                    Wallpaper.Groups = null;
                                }
                                else
                                {
                                    Wallpaper.Groups = wallpaperGroups;
                                }
                                fav?.RemoveGroupFromWallpaper(Wallpaper.ProjectJsonPath, g);
                            }
                        }

                        // 刷新 groups 列表和过滤
                        var vm = App.Current.Services.GetService(typeof(ViewModels.MainViewModel)) as ViewModels.MainViewModel;
                        vm?.RefreshGroupsAndFilter();
                    }
                };
                groupSub.Items.Add(gi);
            }
            groupSub.Items.Add(new MenuFlyoutSeparator());
            var newGroupItem = new MenuFlyoutItem { Text = "新增分组" };
            newGroupItem.Click += async (s, ev) => await ShowNewGroupDialogAsync(fav);
            groupSub.Items.Add(newGroupItem);
            menu.Items.Add(groupSub);

            // 添加删除壁纸按钮
            menu.Items.Add(new MenuFlyoutSeparator());
            var deleteItem = new MenuFlyoutItem { Text = "删除壁纸" };
            deleteItem.Click += async (s, ev) => await DeleteWallpaperAsync();
            menu.Items.Add(deleteItem);

            menu.ShowAt(this, e.GetPosition(this));
        }

        private async System.Threading.Tasks.Task ShowRenameDialogAsync(IFavoritesService? fav)
        {
            var dlg = new InputDialog { TitleText = "重命名", Input = Wallpaper?.Alias ?? Wallpaper?.Title ?? string.Empty };
            dlg.RequireNonEmpty = true;

            bool ok = false;
            try
            {
                ok = await dlg.ShowCenteredAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowRenameDialogAsync failed: {ex}");
                return;
            }

            if (ok && Wallpaper != null && !string.IsNullOrEmpty(Wallpaper.ProjectJsonPath))
            {
                string alias = dlg.Input?.Trim();
                Wallpaper.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias;
                fav?.SetAlias(Wallpaper.ProjectJsonPath, Wallpaper.Alias);

                var display = Wallpaper.Alias ?? Wallpaper.Title ?? "Unknown Title";
                TitleText.Text = display;
            }
        }

        private string NormalizeGroupName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;
            // Trim and collapse internal whitespace to single spaces, then apply compatibility normalization (FormKC) to normalize full/half width chars
            var t = System.Text.RegularExpressions.Regex.Replace(s.Trim(), "\\s+", " ");
            try { t = t.Normalize(System.Text.NormalizationForm.FormKC); } catch { }
            return t;
        }

        private async System.Threading.Tasks.Task ShowNewGroupDialogAsync(IFavoritesService? fav)
        {
            try
            {
                var dlg = new InputDialog { TitleText = "新增分组", Input = string.Empty };
                dlg.RequireNonEmpty = true;
                var existingGroups = fav?.GetAllGroups() ?? new System.Collections.Generic.List<string>();
                var normalizedSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.InvariantCultureIgnoreCase);
                foreach (var g in existingGroups)
                {
                    var n = NormalizeGroupName(g);
                    if (!string.IsNullOrWhiteSpace(n))
                        normalizedSet.Add(n);
                }

                dlg.Validator = (text) =>
                {
                    var n = NormalizeGroupName(text);
                    if (string.IsNullOrWhiteSpace(n))
                        return "名称不能为空";
                    if (normalizedSet.Contains(n))
                        return "分组已存在";
                    return null;
                };

                bool ok = false;
                try
                {
                    ok = await dlg.ShowCenteredAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ShowNewGroupDialogAsync failed during dialog show: {ex}");
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "CrashLog_NewGroup_ShowDialog.txt"), ex.ToString() + Environment.NewLine);
                    return;
                }

                if (ok && Wallpaper != null && !string.IsNullOrEmpty(Wallpaper.ProjectJsonPath))
                {
                    var groupRaw = dlg.Input;
                    var group = NormalizeGroupName(groupRaw);
                    if (!string.IsNullOrWhiteSpace(group))
                    {
                        // 获取MainViewModel实例
                        var vm = App.Current.Services.GetService(typeof(ViewModels.MainViewModel)) as ViewModels.MainViewModel;
                        if (vm == null)
                        {
                            System.Diagnostics.Debug.WriteLine("MainViewModel not available when adding group");
                            System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "CrashLog_NewGroup.txt"), "MainViewModel is null\n");
                        }
                        else
                        {
                            // 通过MainViewModel添加分组，确保分组列表自动刷新
                            try
                            {
                                vm.AddGroup(group);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"AddGroup failed: {ex}");
                                System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "CrashLog_NewGroup_AddGroup.txt"), ex.ToString() + Environment.NewLine);
                            }
                        }

                        // 添加分组到壁纸
                        var wallpaperGroups = Wallpaper.Groups ?? new System.Collections.Generic.List<string>();
                        if (!wallpaperGroups.Contains(group))
                        {
                            wallpaperGroups.Add(group);
                            Wallpaper.Groups = wallpaperGroups;
                            try
                            {
                                fav?.AddGroupToWallpaper(Wallpaper.ProjectJsonPath, group);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"AddGroupToWallpaper failed: {ex}");
                                System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "CrashLog_NewGroup_AddGroupToWallpaper.txt"), ex.ToString() + Environment.NewLine);
                            }
                        }

                        // 刷新过滤
                        try { vm?.RefreshFilters(); } catch (Exception ex) { System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "CrashLog_NewGroup_RefreshFilters.txt"), ex.ToString() + Environment.NewLine); }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception in ShowNewGroupDialogAsync: {ex}");
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "CrashLog_NewGroup_Unhandled.txt"), ex.ToString() + Environment.NewLine);
            }
        }

        private void WallpaperItem_Loaded(object? sender, RoutedEventArgs e)
        {
            if (_compositionInitialized)
                return;

            _rootVisual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(RootGrid);
            if (_rootVisual == null)
                return;

            _compositor = _rootVisual.Compositor;

            // 创建阴影
            var dropShadow = _compositor.CreateDropShadow();
            // DropShadow.Color 在当前平台上使用 Windows.UI.Color
            dropShadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0); // 黑色
            dropShadow.BlurRadius = 30f;
            dropShadow.Opacity = 0.35f;
            dropShadow.Offset = new System.Numerics.Vector3(0, 8, 0);

            _shadowVisual = _compositor.CreateSpriteVisual();
            _shadowVisual.Shadow = dropShadow;

            UpdateShadowSize();

            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(RootGrid, _shadowVisual);

            // 不使用 composition 的白色发光（之前使文字泛白难以阅读），保留基础白色偏移层以实现简单描边效果

            // 默认缩放为 1
            _rootVisual.Scale = new System.Numerics.Vector3(1f, 1f, 1f);

            _compositionInitialized = true;
        }

        private void WallpaperItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateShadowSize();
        }

        private void UpdateShadowSize()
        {
            if (_shadowVisual == null || RootGrid == null)
                return;

            _shadowVisual.Size = new System.Numerics.Vector2((float)RootGrid.ActualWidth, (float)RootGrid.ActualHeight + 10f);
            // 将阴影放在元素后方一点
            _shadowVisual.Offset = new System.Numerics.Vector3(0f, 4f, 0f);
        }

        private void WallpaperItem_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // 在批量模式下，不执行悬浮效果
            if (IsBatchMode)
                return;

            if (_compositor == null || _rootVisual == null)
                return;

            // 缩放动画
            var scaleAnim = _compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1.04f, 1.04f, 1f));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(220);
            scaleAnim.Target = "Scale";
            _rootVisual.StartAnimation("Scale", scaleAnim);

            // 上浮动画（调整 Offset 的 Y）
            var offsetAnim = _compositor.CreateVector3KeyFrameAnimation();
            var current = _rootVisual.Offset;
            offsetAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(current.X, current.Y - 8f, 0f));
            offsetAnim.Duration = TimeSpan.FromMilliseconds(220);
            offsetAnim.Target = "Offset";
            _rootVisual.StartAnimation("Offset", offsetAnim);

            // 加强阴影
            if (_shadowVisual?.Shadow is Microsoft.UI.Composition.DropShadow ds)
            {
                var blurAnim = _compositor.CreateScalarKeyFrameAnimation();
                blurAnim.InsertKeyFrame(1f, 42f);
                blurAnim.Duration = TimeSpan.FromMilliseconds(220);
                ds.StartAnimation("BlurRadius", blurAnim);

                var opacityAnim = _compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(1f, 0.5f);
                opacityAnim.Duration = TimeSpan.FromMilliseconds(220);
                ds.StartAnimation("Opacity", opacityAnim);

                var dropOffsetAnim = _compositor.CreateVector3KeyFrameAnimation();
                dropOffsetAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(0f, 12f, 0f));
                dropOffsetAnim.Duration = TimeSpan.FromMilliseconds(220);
                ds.StartAnimation("Offset", dropOffsetAnim);
            }
        }

        private async Task DeleteWallpaperAsync()
        {
            if (Wallpaper == null || string.IsNullOrEmpty(Wallpaper.ProjectJsonPath))
                return;

            // 显示删除确认提示框
            var dialog = new ContentDialog
            {
                Title = "删除壁纸",
                Content = "确定要删除此壁纸吗？此操作不可恢复，将删除整个壁纸文件夹及其所有内容。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            dialog.XamlRoot = this.XamlRoot;
            var result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
                return;

            try
            {
                // 获取壁纸文件夹路径（从ProjectJsonPath中提取）
                string wallpaperFolderPath = Path.GetDirectoryName(Wallpaper.ProjectJsonPath);
                if (string.IsNullOrEmpty(wallpaperFolderPath))
                {
                    ShowToast("错误", "无法确定壁纸文件夹路径");
                    return;
                }

                // 检查文件夹是否存在
                if (!Directory.Exists(wallpaperFolderPath))
                {
                    ShowToast("错误", "壁纸文件夹不存在");
                    return;
                }

                // 删除整个文件夹及其所有内容
                Directory.Delete(wallpaperFolderPath, true);

                // 刷新UI界面
                var vm = App.Current.Services.GetService(typeof(ViewModels.MainViewModel)) as ViewModels.MainViewModel;
                if (vm != null)
                {
                    await vm.RefreshWallpapers();
                }

                // 显示删除成功的Toast消息
                ShowToast("成功", "壁纸删除成功");
            }
            catch (Exception ex)
            {
                // 处理异常情况
                string errorMessage = ex.Message;
                if (ex is UnauthorizedAccessException)
                {
                    errorMessage = "权限不足，无法删除壁纸文件夹";
                }
                else if (ex is IOException)
                {
                    errorMessage = "文件被占用，无法删除壁纸文件夹";
                }
                ShowToast("错误", errorMessage);
            }
        }

        private void ShowToast(string title, string message)
        {
            var toast = new ToastNotificationWindow(title, message);
            toast.Activate();
            toast.ShowWithoutActivation();
        }

        private void WallpaperItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // 在批量模式下，不执行悬浮效果
            if (IsBatchMode)
                return;

            if (_compositor == null || _rootVisual == null)
                return;

            // 回退缩放
            var scaleAnim = _compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(220);
            scaleAnim.Target = "Scale";
            _rootVisual.StartAnimation("Scale", scaleAnim);

            // 回退位置
            var offsetAnim = _compositor.CreateVector3KeyFrameAnimation();
            var current = _rootVisual.Offset;
            offsetAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(current.X, 0f, 0f));
            offsetAnim.Duration = TimeSpan.FromMilliseconds(220);
            offsetAnim.Target = "Offset";
            _rootVisual.StartAnimation("Offset", offsetAnim);

            // 恢复阴影
            if (_shadowVisual?.Shadow is Microsoft.UI.Composition.DropShadow ds)
            {
                var blurAnim = _compositor.CreateScalarKeyFrameAnimation();
                blurAnim.InsertKeyFrame(1f, 30f);
                blurAnim.Duration = TimeSpan.FromMilliseconds(220);
                ds.StartAnimation("BlurRadius", blurAnim);

                var opacityAnim = _compositor.CreateScalarKeyFrameAnimation();
                opacityAnim.InsertKeyFrame(1f, 0.35f);
                opacityAnim.Duration = TimeSpan.FromMilliseconds(220);
                ds.StartAnimation("Opacity", opacityAnim);

                var dropOffsetAnim = _compositor.CreateVector3KeyFrameAnimation();
                dropOffsetAnim.InsertKeyFrame(1f, new System.Numerics.Vector3(0f, 6f, 0f));
                dropOffsetAnim.Duration = TimeSpan.FromMilliseconds(220);
                ds.StartAnimation("Offset", dropOffsetAnim);
            }
        }

        private static void OnWallpaperChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WallpaperItem item && e.NewValue is WallpaperInfo wallpaper)
            {
                var display = wallpaper.Alias ?? wallpaper.Title ?? "Unknown Title";

                // Update main title
                item.TitleText.Text = display;

                // R18 badge
                if (item.R18Badge != null)
                    item.R18Badge.Visibility = wallpaper.IsR18 ? Visibility.Visible : Visibility.Collapsed;

                // Load preview image
                item.LoadPreviewImageAsync(wallpaper);
            }
        }

        private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WallpaperItem item && e.NewValue is bool isSelected)
            {
                if (item.RootGrid != null)
                {
                    if (isSelected)
                    {
                        // 选中状态：蓝色边框
                        item.RootGrid.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                        item.RootGrid.BorderThickness = new Thickness(3);
                    }
                    else
                    {
                        // 未选中状态：恢复默认样式
                        try
                        {
                            item.RootGrid.BorderBrush = (SolidColorBrush)App.Current.Resources["BorderBrush"];
                        }
                        catch
                        {
                            // 如果获取默认边框刷失败，使用灰色
                            item.RootGrid.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                        }
                        item.RootGrid.BorderThickness = new Thickness(1);
                    }
                }
            }
        }

        private async void LoadPreviewImageAsync(WallpaperInfo wallpaper)
        {
            if (wallpaper == null || string.IsNullOrEmpty(wallpaper.PreviewPath) || _imageCacheService == null)
            {
                // 隐藏 loading 指示并设置占位
                if (this.LoadingIndicator != null)
                    this.LoadingIndicator.Visibility = Visibility.Collapsed;
                if (this.PreviewImage != null)
                    this.PreviewImage.Source = null;
                return;
            }

            try
            {
                if (this.LoadingIndicator != null)
                    this.LoadingIndicator.Visibility = Visibility.Visible;

                BitmapImage? image = null;
                try
                {
                    image = await _imageCacheService.LoadImageAsync(wallpaper.PreviewPath, 180, 120);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Image cache load failed: {ex.Message}");
                }

                // 在 UI 线程设置 Source
                var dq = this.DispatcherQueue;
                if (dq != null)
                {
                    dq.TryEnqueue(() =>
                    {
                        if (this.PreviewImage != null)
                            this.PreviewImage.Source = image;
                    });
                }
                else
                {
                    if (this.PreviewImage != null)
                        this.PreviewImage.Source = image;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading preview image: {ex.Message}");
            }
            finally
            {
                var dq = this.DispatcherQueue;
                if (dq != null)
                {
                    dq.TryEnqueue(() =>
                    {
                        if (this.LoadingIndicator != null)
                            this.LoadingIndicator.Visibility = Visibility.Collapsed;
                    });
                }
                else
                {
                    if (this.LoadingIndicator != null)
                        this.LoadingIndicator.Visibility = Visibility.Collapsed;
                }
            }
        }
    }
}