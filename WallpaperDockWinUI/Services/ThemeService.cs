using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using Windows.UI.ViewManagement;
using Microsoft.Win32;

namespace WallpaperDockWinUI.Services
{
    public enum ThemePreference
    {
        FollowSystem = 0,
        Light = 1,
        Dark = 2
    }

    public interface IThemeService : IDisposable
    {
        void ApplyThemeTo(FrameworkElement root);
        void SetPreference(ThemePreference pref);
        ThemePreference GetPreference();
    }

    public class ThemeService : IThemeService
    {
        private readonly UISettings _uiSettings;
        private FrameworkElement? _root;
        private bool _disposed;
        private const string PrefKey = "ThemePreference";

        /// <summary>
        /// Raised when the effective theme has changed (follow system or user preference changes).
        /// </summary>
        public event EventHandler? ThemeChanged;

        public ThemeService()
        {
            _uiSettings = new UISettings();
            _uiSettings.ColorValuesChanged += UiSettings_ColorValuesChanged;
        }

        public void ApplyThemeTo(FrameworkElement root)
        {
            _root = root;
            SetTheme(root);
        }

        private void UiSettings_ColorValuesChanged(UISettings sender, object args)
        {
            // Called on a non-UI thread - marshal to UI thread
            try
            {
                _root?.DispatcherQueue?.TryEnqueue(() =>
                {
                    if (_root != null && GetPreference() == ThemePreference.FollowSystem)
                        SetTheme(_root);
                });
            }
            catch { }
        }

        private void SetTheme(FrameworkElement root)
        {
            var pref = GetPreference();
            if (pref == ThemePreference.Light)
            {
                root.RequestedTheme = ElementTheme.Light;
                ThemeChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (pref == ThemePreference.Dark)
            {
                root.RequestedTheme = ElementTheme.Dark;
                ThemeChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Follow system
            bool light = IsSystemInLightTheme();
            root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool IsSystemInLightTheme()
        {
            try
            {
                object? val = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                if (val is int i)
                    return i != 0;
            }
            catch { }
            return true;
        }

        public void SetPreference(ThemePreference pref)
        {
            try
            {
                Windows.Storage.ApplicationData.Current.LocalSettings.Values[PrefKey] = (int)pref;
            }
            catch { }

            if (_root != null)
                SetTheme(_root);
        }

        public ThemePreference GetPreference()
        {
            try
            {
                var v = Windows.Storage.ApplicationData.Current.LocalSettings.Values[PrefKey];
                if (v is int i)
                    return (ThemePreference)i;
            }
            catch { }
            return ThemePreference.FollowSystem;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _uiSettings.ColorValuesChanged -= UiSettings_ColorValuesChanged;
                _disposed = true;
            }
        }
    }
}
