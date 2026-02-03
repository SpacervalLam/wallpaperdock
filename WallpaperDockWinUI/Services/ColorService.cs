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
            try
            {
                // Update primary color resource (keeps theme palettes consistent)
                if (App.Current.Resources.ContainsKey("PrimaryColor"))
                    App.Current.Resources["PrimaryColor"] = color;
                else
                    App.Current.Resources.Add("PrimaryColor", color);

                // Update primary brush instance so UI updates immediately
                var primaryBrush = new SolidColorBrush(color);
                if (App.Current.Resources.ContainsKey("PrimaryBrush"))
                    App.Current.Resources["PrimaryBrush"] = primaryBrush;
                else
                    App.Current.Resources.Add("PrimaryBrush", primaryBrush);

                // Keep SystemAccentColor for compatibility with other code
                if (App.Current.Resources.ContainsKey("SystemAccentColor"))
                    App.Current.Resources["SystemAccentColor"] = color;
                else
                    App.Current.Resources.Add("SystemAccentColor", color);

                // Choose an accessible foreground color (white or black) for text/icons on the accent background
                var white = Color.FromArgb(255, 255, 255, 255);
                var black = Color.FromArgb(255, 0, 0, 0);
                double contrastWhite = ContrastRatio(color, white);
                double contrastBlack = ContrastRatio(color, black);

                Color chosen = contrastWhite >= contrastBlack ? white : black;

                if (Math.Max(contrastWhite, contrastBlack) < 4.5)
                {
                    // If neither white nor black meets WCAG 4.5:1, log a warning.
                    Console.WriteLine($"Warning: accent color contrast ({Math.Max(contrastWhite, contrastBlack):F2}) < 4.5 with both white and black.");
                }

                if (App.Current.Resources.ContainsKey("PrimaryActionForegroundBrush"))
                    App.Current.Resources["PrimaryActionForegroundBrush"] = new SolidColorBrush(chosen);
                else
                    App.Current.Resources.Add("PrimaryActionForegroundBrush", new SolidColorBrush(chosen));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating accent color: {ex.Message}");
            }
        }

        private double RelativeLuminance(Color c)
        {
            // Convert sRGB to linear values (0..1)
            double Rs = c.R / 255.0;
            double Gs = c.G / 255.0;
            double Bs = c.B / 255.0;

            double R = Rs <= 0.03928 ? Rs / 12.92 : Math.Pow((Rs + 0.055) / 1.055, 2.4);
            double G = Gs <= 0.03928 ? Gs / 12.92 : Math.Pow((Gs + 0.055) / 1.055, 2.4);
            double B = Bs <= 0.03928 ? Bs / 12.92 : Math.Pow((Bs + 0.055) / 1.055, 2.4);

            return 0.2126 * R + 0.7152 * G + 0.0722 * B;
        }

        private double ContrastRatio(Color a, Color b)
        {
            double la = RelativeLuminance(a);
            double lb = RelativeLuminance(b);
            double lighter = Math.Max(la, lb);
            double darker = Math.Min(la, lb);
            return (lighter + 0.05) / (darker + 0.05);
        }
    }
}