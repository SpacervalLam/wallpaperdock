using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace WallpaperDockWinUI.Services
{
    public interface IImageCacheService
    {
        Task<BitmapImage?> LoadImageAsync(string imagePath, int desiredWidth = 180, int desiredHeight = 120);
        void ClearCache();
    }

    public class ImageCacheService : IImageCacheService
    {
        private readonly Dictionary<string, BitmapImage> _cache;
        private readonly object _cacheLock = new object();

        public ImageCacheService()
        {
            _cache = new Dictionary<string, BitmapImage>();
        }

        public Task<BitmapImage?> LoadImageAsync(string imagePath, int desiredWidth = 180, int desiredHeight = 120)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                return Task.FromResult<BitmapImage?>(null);
            }

            // 规范化路径：Steam 注册表返回的 SteamPath 使用正斜杠，Path.Combine 不会规范化混合斜杠，
            // 而 StorageFile / Uri 在 Windows 11 上对路径格式更敏感。这里统一规范化用作缓存键。
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(imagePath);
            }
            catch
            {
                fullPath = imagePath;
            }

            string cacheKey = $"{fullPath}_{desiredWidth}x{desiredHeight}";

            lock (_cacheLock)
            {
                if (_cache.TryGetValue(cacheKey, out BitmapImage? cached))
                {
                    return Task.FromResult(cached);
                }
            }

            try
            {
                // 使用 UriSource 而非 StorageFile + SetSourceAsync(stream)：
                // 1. BitmapImage 是 DependencyObject，SetSourceAsync 必须在 UI 线程调用；
                //    而 await StorageFile.GetFileFromPathAsync 之后可能运行在线程池线程，
                //    导致 SetSourceAsync 在非 UI 线程执行，图像静默加载失败（Windows 11 上更严格）
                // 2. UriSource 是延迟加载：仅当 BitmapImage 被 Image 控件绑定时，
                //    WinUI 才在 UI 线程上异步加载图像，从根本上避免线程亲和性问题
                // 3. 同时也避免了 using 块提前释放 stream 的潜在风险
                // 使用默认 Uri 构造（等价于 UriKind.RelativeOrAbsolute）：
                // 对 "C:\foo\bar.jpg" 形式的 Windows 路径，Uri 解析器会自动识别盘符并构造
                // file:/// URI；而 UriKind.Absolute 会因缺少 scheme 抛出 UriFormatException。
                BitmapImage image = new BitmapImage();
                image.DecodePixelWidth = desiredWidth;
                image.DecodePixelHeight = desiredHeight;
                image.UriSource = new Uri(fullPath);

                lock (_cacheLock)
                {
                    // 双重检查：并发场景下若另一线程已加载，则复用其结果
                    if (_cache.TryGetValue(cacheKey, out BitmapImage? existing))
                    {
                        return Task.FromResult(existing);
                    }
                    _cache[cacheKey] = image;
                }

                return Task.FromResult(image);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image {imagePath}: {ex.Message}");
                return Task.FromResult<BitmapImage?>(null);
            }
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }
    }
}
