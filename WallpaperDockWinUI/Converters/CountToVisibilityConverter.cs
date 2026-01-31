using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace WallpaperDockWinUI.Converters
{
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // 安全检查 value 是否为 int 类型
            if (value is int count)
            {
                return count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // 安全检查 value 是否为 ICollection 类型
            if (value is System.Collections.ICollection collection)
            {
                return collection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // 不支持反向转换
            return 0;
        }
    }
}
