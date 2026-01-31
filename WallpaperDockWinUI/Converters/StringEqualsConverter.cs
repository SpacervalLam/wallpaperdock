using Microsoft.UI.Xaml.Data;

namespace WallpaperDockWinUI.Converters
{
    public class StringEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // 安全检查
            if (value == null || parameter == null)
            {
                return false;
            }
            
            string valueStr = value.ToString() ?? string.Empty;
            string paramStr = parameter.ToString() ?? string.Empty;
            
            return valueStr == paramStr;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            // 安全检查
            if (value is bool boolValue && boolValue)
            {
                return parameter?.ToString() ?? string.Empty;
            }
            
            return string.Empty;
        }
    }
}
