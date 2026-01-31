using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace WallpaperDockWinUI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // 安全检查 value 是否为 bool 类型
            if (value is bool boolValue)
            {
                // 安全检查 parameter 是否为可解析的 bool 字符串
                bool invert = false;
                if (parameter != null && parameter.ToString() != null)
                {
                    bool.TryParse(parameter.ToString(), out invert);
                }
                
                if (invert)
                {
                    return boolValue ? Visibility.Collapsed : Visibility.Visible;
                }
                else
                {
                    return boolValue ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            // 默认返回值
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // 安全检查 value 是否为 Visibility 类型
            if (value is Visibility visibility)
            {
                // 安全检查 parameter 是否为可解析的 bool 字符串
                bool invert = false;
                if (parameter != null && parameter.ToString() != null)
                {
                    bool.TryParse(parameter.ToString(), out invert);
                }
                
                if (invert)
                {
                    return visibility == Visibility.Collapsed;
                }
                else
                {
                    return visibility == Visibility.Visible;
                }
            }
            // 默认返回值
            return false;
        }
    }
}
