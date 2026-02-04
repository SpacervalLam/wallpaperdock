using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using WallpaperDockWinUI.Services;
using WallpaperDockWinUI.ViewModels;

namespace WallpaperDockWinUI.Views
{
    public sealed partial class PlaylistEditorDialog : ContentDialog
    {
        private MainViewModel? _viewModel;
        private IWallpaperService? _wallpaperService;
        private string? _currentPlaylist;

        public PlaylistEditorDialog()
        {
            this.InitializeComponent();
            Loaded += PlaylistEditorDialog_Loaded;
            
            // 添加点击外部关闭的功能
            this.Closing += PlaylistEditorDialog_Closing;
        }
        
        // 处理对话框关闭事件
        private void PlaylistEditorDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
            // 可以在这里添加关闭前的清理逻辑
        }

        private void PlaylistEditorDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取服务
            _viewModel = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
            _wallpaperService = App.Current.Services.GetService(typeof(IWallpaperService)) as IWallpaperService;

            // 初始化播放列表选择器
            if (_viewModel != null)
            {
                // 订阅播放状态变化事件
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                
                if (_viewModel.Playlists != null && _viewModel.Playlists.Count > 0)
                {
                    PlaylistComboBox.ItemsSource = _viewModel.Playlists;
                    PlaylistComboBox.SelectedIndex = 0;
                    _currentPlaylist = _viewModel.Playlists[0];
                    LoadPlaylistContent(_currentPlaylist);
                }
                else
                {
                    // 暂无播放列表
                    PlaylistComboBox.ItemsSource = new List<string> { "暂无播放列表" };
                    PlaylistComboBox.SelectedIndex = 0;
                    PlaylistComboBox.IsEnabled = false;
                }
            }

            // 初始化定时策略设置
            IntervalComboBox.SelectedIndex = 0; // 默认 5 分钟
            PlayOrderComboBox.SelectedIndex = 0; // 默认顺序播放

            // 添加间隔选择变化事件
            IntervalComboBox.SelectionChanged += IntervalComboBox_SelectionChanged;
            // 添加播放列表选择变化事件
            PlaylistComboBox.SelectionChanged += PlaylistComboBox_SelectionChanged;
            
            // 初始化播放状态显示
            UpdatePlaybackStatusDisplay();
        }

        private void PlaylistComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 加载选中的播放列表内容
            if (PlaylistComboBox.SelectedItem is string playlistName && _viewModel != null && playlistName != "暂无播放列表")
            {
                _currentPlaylist = playlistName;
                LoadPlaylistContent(playlistName);
            }
        }

        private void LoadPlaylistContent(string playlistName)
        {
            // 加载播放列表内容
            if (_viewModel != null && !string.IsNullOrEmpty(playlistName))
            {
                var playlistContent = _viewModel.GetPlaylistContent(playlistName);
                // 先设置为 null，再重新设置，强制 UI 刷新
                PlaylistListView.ItemsSource = null;
                PlaylistListView.ItemsSource = playlistContent;
            }
            else
            {
                // 如果 playlistName 为 null 或空，则清空列表
                PlaylistListView.ItemsSource = null;
                PlaylistListView.ItemsSource = new List<WallpaperInfo>();
            }
        }

        private void IntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 显示或隐藏自定义间隔输入
            if (IntervalComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag.ToString() == "custom")
            {
                // 显示自定义间隔输入
                var customIntervalGrid = FindName("CustomIntervalGrid") as Grid;
                if (customIntervalGrid != null)
                {
                    customIntervalGrid.Visibility = Visibility.Visible;
                }
            }
            else
            {
                // 隐藏自定义间隔输入
                var customIntervalGrid = FindName("CustomIntervalGrid") as Grid;
                if (customIntervalGrid != null)
                {
                    customIntervalGrid.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void NewPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            // 弹出新建播放列表对话框
            var dlg = new InputDialog { TitleText = "新建播放列表", Input = string.Empty };
            dlg.RequireNonEmpty = true;

            // 显示对话框
            var result = await dlg.ShowCenteredAsync();
            if (result)
            {
                string playlistName = dlg.Input?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(playlistName) && _viewModel != null)
                {
                    // 添加新播放列表
                    _viewModel.AddPlaylist(playlistName);
                    // 更新播放列表选择器
                    PlaylistComboBox.ItemsSource = _viewModel.Playlists;
                    PlaylistComboBox.SelectedItem = playlistName;
                    PlaylistComboBox.IsEnabled = true;
                    _currentPlaylist = playlistName;
                    // 清空播放列表内容
                    PlaylistListView.ItemsSource = new List<WallpaperInfo>();
                }
            }
        }

        private void RemoveFromPlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            // 从播放列表中移除选中的壁纸
            if (sender is Button button && button.Tag is WallpaperInfo wallpaper && _viewModel != null && _currentPlaylist != null)
            {
                _viewModel.RemoveWallpaperFromPlaylist(wallpaper, _currentPlaylist);
                // 重新加载播放列表内容
                LoadPlaylistContent(_currentPlaylist);
            }
        }

        private async void RenamePlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            // 重命名播放列表
            if (_viewModel != null && PlaylistComboBox.SelectedItem is string selectedPlaylist)
            {
                // 弹出重命名对话框
                var dlg = new InputDialog { TitleText = "重命名播放列表", Input = selectedPlaylist };
                dlg.RequireNonEmpty = true;

                bool ok = await dlg.ShowCenteredAsync();
                if (ok)
                {
                    string newName = dlg.Input?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(newName) && newName != selectedPlaylist)
                    {
                        // 调用重命名方法
                        _viewModel.RenamePlaylist(selectedPlaylist, newName);
                        // 强制更新播放列表选择器
                        // 先设为 null 再重新设置，确保UI刷新
                        PlaylistComboBox.ItemsSource = null;
                        PlaylistComboBox.ItemsSource = _viewModel.Playlists;
                        PlaylistComboBox.SelectedItem = newName;
                        _currentPlaylist = newName;
                        // 重新加载播放列表内容
                        LoadPlaylistContent(_currentPlaylist);
                    }
                }
            }
        }

        private async void DeletePlaylistButton_Click(object sender, RoutedEventArgs e)
        {
            // 删除播放列表
            if (_viewModel != null && PlaylistComboBox.SelectedItem is string selectedPlaylist)
            {
                // 创建自定义确认对话框
                var confirmDialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除播放列表 '{selectedPlaylist}' 吗？",
                    PrimaryButtonText = "确定",
                    SecondaryButtonText = "取消"
                };

                // 确保设置 XamlRoot
                if (this.XamlRoot != null)
                {
                    confirmDialog.XamlRoot = this.XamlRoot;
                }
                else if (this.Content is FrameworkElement rootElement && rootElement.XamlRoot != null)
                {
                    confirmDialog.XamlRoot = rootElement.XamlRoot;
                }

                try
                {
                    // 显示对话框并处理结果
                    var result = await confirmDialog.ShowAsync();
                    if (result == ContentDialogResult.Primary)
                    {
                        // 调用删除方法
                        _viewModel.DeletePlaylist(selectedPlaylist);
                        // 强制更新播放列表选择器
                        if (_viewModel.Playlists != null && _viewModel.Playlists.Count > 0)
                        {
                            // 重新设置 ItemsSource 以确保UI更新
                            PlaylistComboBox.ItemsSource = null;
                            PlaylistComboBox.ItemsSource = _viewModel.Playlists;
                            PlaylistComboBox.SelectedIndex = 0;
                            _currentPlaylist = _viewModel.Playlists[0];
                            LoadPlaylistContent(_currentPlaylist);
                        }
                        else
                        {
                            // 暂无播放列表
                            PlaylistComboBox.ItemsSource = new List<string> { "暂无播放列表" };
                            PlaylistComboBox.SelectedIndex = 0;
                            PlaylistComboBox.IsEnabled = false;
                            _currentPlaylist = null;
                            PlaylistListView.ItemsSource = new List<WallpaperInfo>();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 捕获并记录异常，防止应用崩溃
                    Console.WriteLine($"Error in DeletePlaylistButton_Click: {ex.Message}");
                    // 即使发生异常，也尝试执行删除操作
                    try
                    {
                        _viewModel.DeletePlaylist(selectedPlaylist);
                        // 强制更新播放列表选择器
                        if (_viewModel.Playlists != null && _viewModel.Playlists.Count > 0)
                        {
                            PlaylistComboBox.ItemsSource = null;
                            PlaylistComboBox.ItemsSource = _viewModel.Playlists;
                            PlaylistComboBox.SelectedIndex = 0;
                            _currentPlaylist = _viewModel.Playlists[0];
                            LoadPlaylistContent(_currentPlaylist);
                        }
                        else
                        {
                            PlaylistComboBox.ItemsSource = new List<string> { "暂无播放列表" };
                            PlaylistComboBox.SelectedIndex = 0;
                            PlaylistComboBox.IsEnabled = false;
                            _currentPlaylist = null;
                            PlaylistListView.ItemsSource = new List<WallpaperInfo>();
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Console.WriteLine($"Error executing delete operation: {innerEx.Message}");
                    }
                }
            }
        }

        private void StartPlaybackButton_Click(object sender, RoutedEventArgs e)
        {
            // 开始播放
            if (_viewModel != null && _currentPlaylist != null)
            {
                // 获取切换间隔
                int interval = 300; // 默认 5 分钟
                if (IntervalComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    if (selectedItem.Tag.ToString() == "custom")
                    {
                        // 自定义间隔
                        if (int.TryParse(CustomIntervalTextBox.Text, out int customInterval))
                        {
                            interval = customInterval;
                        }
                    }
                    else
                    {
                        // 预设间隔
                        interval = int.Parse(selectedItem.Tag.ToString());
                    }
                }

                // 获取播放顺序
                string playOrder = "Sequential";
                if (PlayOrderComboBox.SelectedItem is ComboBoxItem playOrderItem)
                {
                    playOrder = playOrderItem.Tag.ToString();
                }

                // 开始播放
                _viewModel.StartPlaylistPlayback(_currentPlaylist, interval, playOrder);
                // 显示成功消息
                var toastWindow = new ToastNotificationWindow("成功", "播放列表已开始播放");
                toastWindow.ShowWithoutActivation();
            }
        }

        private void StopPlaybackButton_Click(object sender, RoutedEventArgs e)
        {
            // 停止播放
            if (_viewModel != null)
            {
                _viewModel.StopPlaylistPlayback();
                // 显示成功消息
                var toastWindow = new ToastNotificationWindow("成功", "播放列表已停止播放");
                toastWindow.ShowWithoutActivation();
            }
        }

        // 保存播放列表内容的方法，在需要时调用
        private void SavePlaylistContent()
        {
            // 保存更改
            if (_viewModel != null && _currentPlaylist != null)
            {
                // 获取当前播放列表内容
                var playlistContent = PlaylistListView.ItemsSource as List<WallpaperInfo>;
                if (playlistContent != null)
                {
                    // 保存播放列表内容
                    _viewModel.SavePlaylistContent(_currentPlaylist, playlistContent);
                }
            }
        }

        // 播放状态变化事件处理
        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.IsPlaying) || e.PropertyName == nameof(MainViewModel.CurrentPlayingPlaylist))
            {
                UpdatePlaybackStatusDisplay();
            }
        }

        // 更新播放状态显示
        private void UpdatePlaybackStatusDisplay()
        {
            if (_viewModel != null && PlaybackStatusText != null)
            {
                if (_viewModel.IsPlaying && !string.IsNullOrEmpty(_viewModel.CurrentPlayingPlaylist))
                {
                    PlaybackStatusText.Text = $"正在播放: {_viewModel.CurrentPlayingPlaylist} (间隔: {_viewModel.PlayInterval}秒)";
                    PlaybackStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Green);
                }
                else
                {
                    PlaybackStatusText.Text = "未播放";
                    PlaybackStatusText.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray);
                }
            }
        }
    }
}
