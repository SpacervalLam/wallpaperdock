using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using WallpaperDockWinUI.Services;

namespace WallpaperDockWinUI.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ISteamLibraryService _steamLibraryService;
        private readonly IWallpaperService _wallpaperService;
        private readonly IMonitorService _monitorService;
        private readonly IColorService _colorService;
        private readonly IFavoritesService _favoritesService;

        private List<WallpaperInfo> _wallpapers = new List<WallpaperInfo>();
        private List<WallpaperInfo> _filteredWallpapers = new List<WallpaperInfo>();
        private WallpaperInfo? _selectedWallpaper;
        private bool _isLoading = false;
        private string _searchText = string.Empty;
        private List<MonitorInfo> _monitors = new List<MonitorInfo>();
        private int _selectedMonitorIndex = -1;
        private string _selectedCategory = "All";
        private List<string> _categories = new List<string>();

        public event PropertyChangedEventHandler? PropertyChanged;

        public List<WallpaperInfo> Wallpapers
        {
            get => _filteredWallpapers;
            set => SetProperty(ref _filteredWallpapers, value);
        }

        public WallpaperInfo? SelectedWallpaper
        {
            get => _selectedWallpaper;
            set
            {
                if (SetProperty(ref _selectedWallpaper, value))
                {
                    // Update accent color when wallpaper is selected
                    if (value != null && !string.IsNullOrEmpty(value.ThemeColor))
                    {
                        UpdateAccentColor(value.ThemeColor);
                    }
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    FilterWallpapers();
                }
            }
        }

        public List<MonitorInfo> Monitors
        {
            get => _monitors;
            set => SetProperty(ref _monitors, value);
        }

        public int SelectedMonitorIndex
        {
            get => _selectedMonitorIndex;
            set => SetProperty(ref _selectedMonitorIndex, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    FilterWallpapers();
                }
            }
        }

        public List<string> Categories
        {
            get => _categories;
            set => SetProperty(ref _categories, value);
        }

        public MainViewModel(ISteamLibraryService steamLibraryService, IWallpaperService wallpaperService, IMonitorService monitorService, IColorService colorService, IFavoritesService favoritesService)
        {
            _steamLibraryService = steamLibraryService;
            _wallpaperService = wallpaperService;
            _monitorService = monitorService;
            _colorService = colorService;
            _favoritesService = favoritesService;
            LoadMonitors();
            LoadCategories();
            LoadGroups();
        }

        public async Task LoadWallpapersAsync()
        {
            IsLoading = true;
            try
            {
                // Get all Steam library paths
                List<string> libraryPaths = _steamLibraryService.GetAllSteamLibraryPaths();
                if (libraryPaths.Count == 0)
                {
                    Console.WriteLine("Error: Could not find any Steam library paths.");
                    return;
                }

                // Find Wallpaper Engine workshop directories
                List<string> workshopPaths = new List<string>();
                foreach (string libraryPath in libraryPaths)
                {
                    string workshopPath = System.IO.Path.Combine(libraryPath, "steamapps", "workshop", "content", "431960");
                    if (System.IO.Directory.Exists(workshopPath))
                    {
                        workshopPaths.Add(workshopPath);
                    }
                }

                if (workshopPaths.Count == 0)
                {
                    Console.WriteLine("Error: Could not find any Wallpaper Engine workshop directories.");
                    return;
                }

                // Scan and parse wallpaper information from all workshop directories
                List<WallpaperInfo> allWallpapers = new List<WallpaperInfo>();
                foreach (string workshopPath in workshopPaths)
                {
                    allWallpapers.AddRange(_wallpaperService.ScanWallpapers(workshopPath));
                }

                // Update favorites, categories and metadata
                foreach (var wallpaper in allWallpapers)
                {
                    wallpaper.IsFavorite = _favoritesService.IsFavorite(wallpaper.ProjectJsonPath!);
                    wallpaper.Category = _favoritesService.GetCategory(wallpaper.ProjectJsonPath!);

                    // load alias, r18 flag and group
                    wallpaper.Alias = _favoritesService.GetAlias(wallpaper.ProjectJsonPath!);
                    wallpaper.IsR18 = _favoritesService.IsR18(wallpaper.ProjectJsonPath!);
                    wallpaper.Group = _favoritesService.GetGroup(wallpaper.ProjectJsonPath!);
                }

                _wallpapers = allWallpapers;
                FilterWallpapers();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading wallpapers: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void LoadMonitors()
        {
            Monitors = _monitorService.GetAllMonitors();
        }

        public void LoadCategories()
        {
            var categories = _favoritesService.GetCategories();
            categories.Insert(0, "All");
            categories.Insert(1, "Favorites");
            Categories = categories;
        }

        // Groups and R18 filtering
        private List<string> _groups = new List<string>();
        private string _selectedGroup = "All";
        private bool _showR18 = true;

        public List<string> Groups
        {
            get => _groups;
            set => SetProperty(ref _groups, value);
        }

        public string SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (SetProperty(ref _selectedGroup, value))
                {
                    FilterWallpapers();
                }
            }
        }

        public bool ShowR18
        {
            get => _showR18;
            set
            {
                if (SetProperty(ref _showR18, value))
                {
                    FilterWallpapers();
                }
            }
        }

        public void LoadGroups()
        {
            var groups = _favoritesService.GetGroups();
            groups.Sort();
            groups.Insert(0, "All");
            Groups = groups;
        }

        // Convenience methods for other components to trigger refreshes after metadata changes
        public void RefreshGroupsAndFilter()
        {
            LoadGroups();
            FilterWallpapers();
        }

        public void RefreshFilters()
        {
            FilterWallpapers();
        }

        public void SwitchWallpaper(int monitorIndex = -1)
        {
            if (SelectedWallpaper != null && !string.IsNullOrEmpty(SelectedWallpaper.ProjectJsonPath))
            {
                _wallpaperService.SwitchWallpaper(SelectedWallpaper.ProjectJsonPath, monitorIndex);
            }
        }

        public void ToggleFavorite(WallpaperInfo wallpaper)
        {
            if (wallpaper != null && !string.IsNullOrEmpty(wallpaper.ProjectJsonPath))
            {
                if (wallpaper.IsFavorite)
                {
                    _favoritesService.RemoveFavorite(wallpaper.ProjectJsonPath);
                    wallpaper.IsFavorite = false;
                }
                else
                {
                    _favoritesService.AddFavorite(wallpaper.ProjectJsonPath);
                    wallpaper.IsFavorite = true;
                }
                FilterWallpapers();
            }
        }

        public void SetCategory(WallpaperInfo wallpaper, string category)
        {
            if (wallpaper != null && !string.IsNullOrEmpty(wallpaper.ProjectJsonPath))
            {
                _favoritesService.SetCategory(wallpaper.ProjectJsonPath, category);
                wallpaper.Category = category;
                LoadCategories();
                FilterWallpapers();
            }
        }

        private void UpdateAccentColor(string schemeColor)
        {
            try
            {
                var color = _colorService.ParseSchemeColor(schemeColor);
                _colorService.UpdateAccentColor(color);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating accent color: {ex.Message}");
            }
        }

        private void FilterWallpapers()
        {
            var filtered = _wallpapers;

            // Filter by category
            if (SelectedCategory == "Favorites")
            {
                filtered = filtered.Where(w => w.IsFavorite).ToList();
            }
            else if (SelectedCategory != "All")
            {
                filtered = filtered.Where(w => w.Category == SelectedCategory).ToList();
            }

            // Filter by group (if selected)
            if (!string.IsNullOrWhiteSpace(SelectedGroup) && SelectedGroup != "All")
            {
                filtered = filtered.Where(w => (w.Group ?? string.Empty) == SelectedGroup).ToList();
            }

            // Filter out R18 items if user disabled showing them
            if (!ShowR18)
            {
                filtered = filtered.Where(w => !w.IsR18).ToList();
            }

            // Filter by search text
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string searchLower = SearchText.ToLower();
                filtered = filtered.Where(w =>
                    w.Title?.ToLower().Contains(searchLower) ?? false
                ).ToList();
            }

            Wallpapers = filtered;
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
