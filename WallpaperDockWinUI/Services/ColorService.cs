using Windows.UI;
using Microsoft.UI.Xaml.Media;
using System;

namespace WallpaperDockWinUI.Services
{
    public interface IColorService
    {
        Color ParseSchemeColor(string schemeColor);
        SolidColorBrush ParseSchemeColorToBrush(string schemeColor);
        void UpdateAccentColor(Color color);
    }

    public class ColorService : IColorService
    {
        public Color ParseSchemeColor(string schemeColor)
        {
            if (string.IsNullOrEmpty(schemeColor))
            {
                return Color.FromArgb(255, 65, 105, 225); // RoyalBlue
            }

            try
            {
                // Wallpaper Engine scheme color format: "0.1 0.5 0.8"
                string[] parts = schemeColor.Split(' ');
                if (parts.Length >= 3)
                {
                    float r = float.Parse(parts[0]);
                    float g = float.Parse(parts[1]);
                    float b = float.Parse(parts[2]);

                    // Clamp values to 0-1 range
                    r = Math.Clamp(r, 0, 1);
                    g = Math.Clamp(g, 0, 1);
                    b = Math.Clamp(b, 0, 1);

                    // Convert to 0-255 range
                    byte red = (byte)(r * 255);
                    byte green = (byte)(g * 255);
                    byte blue = (byte)(b * 255);

                    return Color.FromArgb(255, red, green, blue);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing scheme color: {ex.Message}");
            }

            return Color.FromArgb(255, 65, 105, 225); // RoyalBlue
        }

        public SolidColorBrush ParseSchemeColorToBrush(string schemeColor)
        {
            Color color = ParseSchemeColor(schemeColor);
            return new SolidColorBrush(color);
        }

        public void UpdateAccentColor(Color color)
        {
            // Update the system accent color resource
            // Note: This is a simplified approach. In a real-world scenario,
            // you might want to update multiple resources or use a more sophisticated theming system.
            try
            {
                if (App.Current.Resources.ContainsKey("SystemAccentColor"))
                {
                    App.Current.Resources["SystemAccentColor"] = color;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating accent color: {ex.Message}");
            }
        }
    }
}