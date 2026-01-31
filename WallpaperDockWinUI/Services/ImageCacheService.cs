using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace WallpaperDockWinUI.Services
{
    public interface IImageCacheService
    {
        Task<BitmapImage> LoadImageAsync(string imagePath, int desiredWidth = 180, int desiredHeight = 120);
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

        public async Task<BitmapImage> LoadImageAsync(string imagePath, int desiredWidth = 180, int desiredHeight = 120)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                return GetPlaceholderImage();
            }

            // Generate cache key based on image path and desired size
            string cacheKey = $"{imagePath}_{desiredWidth}x{desiredHeight}";

            // Try to get from cache first
            if (_cache.ContainsKey(cacheKey))
            {
                return _cache[cacheKey];
            }

            try
            {
                // Load image asynchronously
                BitmapImage image = await LoadAndResizeImageAsync(imagePath, desiredWidth, desiredHeight);

                // Add to cache
                lock (_cacheLock)
                {
                    if (!_cache.ContainsKey(cacheKey))
                    {
                        _cache[cacheKey] = image;
                    }
                }

                return image;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading image {imagePath}: {ex.Message}");
                return GetPlaceholderImage();
            }
        }

        private async Task<BitmapImage> LoadAndResizeImageAsync(string imagePath, int desiredWidth, int desiredHeight)
        {
            // 关键修改：BitmapImage 必须在 UI 线程创建
            // 我们不需要 Task.Run，因为 SetSourceAsync 本身就是异步的，不会阻塞 UI
            
            try
            {
                // 1. 在 UI 线程创建对象
                BitmapImage image = new BitmapImage();
                
                // 2. 设置解码尺寸（优化内存）
                image.DecodePixelWidth = desiredWidth;
                image.DecodePixelHeight = desiredHeight;

                // 3. 异步读取文件流
                StorageFile file = await StorageFile.GetFileFromPathAsync(imagePath);
                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    // 4. 异步设置源
                    await image.SetSourceAsync(stream);
                }

                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image {imagePath}: {ex.Message}");
                return GetPlaceholderImage();
            }
        }

        private BitmapImage GetPlaceholderImage()
        {
            // Return a placeholder image when loading fails
            BitmapImage placeholder = new BitmapImage();
            // You could set a default placeholder image here
            return placeholder;
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }
    }

    // Helper class for BitmapImage options
    public class BitmapImageOptions
    {
        public int DecodePixelWidth { get; set; }
        public int DecodePixelHeight { get; set; }
    }
}