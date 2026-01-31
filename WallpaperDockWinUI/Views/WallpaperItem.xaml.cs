using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            var groups = fav?.GetGroups() ?? new System.Collections.Generic.List<string>();
            foreach (var g in groups)
            {
                var gi = new MenuFlyoutItem { Text = g };
                gi.Click += (s, ev) =>
                {
                    Wallpaper.Group = g;
                    fav?.SetGroup(Wallpaper.ProjectJsonPath, g);

                    // 刷新 groups 列表和过滤
                    var vm = App.Current.Services.GetService(typeof(ViewModels.MainViewModel)) as ViewModels.MainViewModel;
                    vm?.RefreshGroupsAndFilter();
                };
                groupSub.Items.Add(gi);
            }
            groupSub.Items.Add(new MenuFlyoutSeparator());
            var newGroupItem = new MenuFlyoutItem { Text = "新增分组" };
            newGroupItem.Click += async (s, ev) => await ShowNewGroupDialogAsync(fav);
            groupSub.Items.Add(newGroupItem);
            menu.Items.Add(groupSub);

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
            var dlg = new InputDialog { TitleText = "新增分组", Input = string.Empty };
            dlg.RequireNonEmpty = true;
            var existingGroups = fav?.GetGroups() ?? new System.Collections.Generic.List<string>();
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
                System.Diagnostics.Debug.WriteLine($"ShowNewGroupDialogAsync failed: {ex}");
                return;
            }

            if (ok && Wallpaper != null && !string.IsNullOrEmpty(Wallpaper.ProjectJsonPath))
            {
                var groupRaw = dlg.Input;
                var group = NormalizeGroupName(groupRaw);
                if (!string.IsNullOrWhiteSpace(group))
                {
                    Wallpaper.Group = group;
                    fav?.SetGroup(Wallpaper.ProjectJsonPath, group);
                }
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

        private void WallpaperItem_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
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

                // 让顶部文字为白色以便在各种缩略图上可读
                item.TitleText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

                // R18 badge
                if (item.R18Badge != null)
                    item.R18Badge.Visibility = wallpaper.IsR18 ? Visibility.Visible : Visibility.Collapsed;

                // Load preview image
                item.LoadPreviewImageAsync(wallpaper);
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