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
        }

        private void PlaylistEditorDialog_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取服务
            _viewModel = App.Current.Services.GetService(typeof(MainViewModel)) as MainViewModel;
            _wallpaperService = App.Current.Services.GetService(typeof(IWallpaperService)) as IWallpaperService;

            // 初始化播放列表选择器
            if (_viewModel != null)
            {
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
        }

        private void PlaylistComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 加载选中的播放列表内容
            if (PlaylistComboBox.SelectedItem is string playlistName)
            {
                _currentPlaylist = playlistName;
                LoadPlaylistContent(playlistName);
            }
        }

        private void LoadPlaylistContent(string playlistName)
        {
            // 加载播放列表内容
            if (_viewModel != null)
            {
                var playlistContent = _viewModel.GetPlaylistContent(playlistName);
                PlaylistListView.ItemsSource = playlistContent;
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

        private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
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

        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            // 取消
        }
    }
}
