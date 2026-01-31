using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WallpaperDockWinUI.Services
{
    public interface IFavoritesService
    {
        List<string> GetFavorites();
        void AddFavorite(string projectJsonPath);
        void RemoveFavorite(string projectJsonPath);
        bool IsFavorite(string projectJsonPath);
        List<string> GetCategories();
        string GetCategory(string projectJsonPath);
        void SetCategory(string projectJsonPath, string category);

        // 元数据操作
        string? GetAlias(string projectJsonPath);
        void SetAlias(string projectJsonPath, string? alias);
        bool IsR18(string projectJsonPath);
        void SetR18(string projectJsonPath, bool isR18);
        string? GetGroup(string projectJsonPath);
        void SetGroup(string projectJsonPath, string? group);
        List<string> GetGroups();
    }

    public class FavoritesService : IFavoritesService
    {
        private readonly string _favoritesFilePath;
        private readonly string _categoriesFilePath;
        private readonly string _metadataFilePath;

        public FavoritesService()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "WallpaperDock");
            Directory.CreateDirectory(appFolderPath);

            _favoritesFilePath = Path.Combine(appFolderPath, "favorites.json");
            _categoriesFilePath = Path.Combine(appFolderPath, "categories.json");
            _metadataFilePath = Path.Combine(appFolderPath, "metadata.json");
        }

        private record WallpaperMetadata(string? Alias, bool IsR18, string? Group);

        private System.Collections.Generic.Dictionary<string, WallpaperMetadata> LoadAllMetadata()
        {
            try
            {
                if (File.Exists(_metadataFilePath))
                {
                    string json = File.ReadAllText(_metadataFilePath);
                    var dict = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, WallpaperMetadata>>(json);
                    return dict ?? new System.Collections.Generic.Dictionary<string, WallpaperMetadata>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading metadata: {ex.Message}");
            }
            return new System.Collections.Generic.Dictionary<string, WallpaperMetadata>();
        }

        private void SaveAllMetadata(System.Collections.Generic.Dictionary<string, WallpaperMetadata> dict)
        {
            try
            {
                string json = JsonSerializer.Serialize(dict);
                File.WriteAllText(_metadataFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving metadata: {ex.Message}");
            }
        }

        public string? GetAlias(string projectJsonPath)
        {
            try
            {
                var dict = LoadAllMetadata();
                if (dict.ContainsKey(projectJsonPath))
                    return dict[projectJsonPath].Alias;
            }
            catch { }
            return null;
        }

        public void SetAlias(string projectJsonPath, string? alias)
        {
            try
            {
                var dict = LoadAllMetadata();
                dict[projectJsonPath] = new WallpaperMetadata(alias, dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].IsR18 : false, dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].Group : null);
                SaveAllMetadata(dict);
            }
            catch { }
        }

        public bool IsR18(string projectJsonPath)
        {
            try
            {
                var dict = LoadAllMetadata();
                if (dict.ContainsKey(projectJsonPath))
                    return dict[projectJsonPath].IsR18;
            }
            catch { }
            return false;
        }

        public void SetR18(string projectJsonPath, bool isR18)
        {
            try
            {
                var dict = LoadAllMetadata();
                dict[projectJsonPath] = new WallpaperMetadata(dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].Alias : null, isR18, dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].Group : null);
                SaveAllMetadata(dict);
            }
            catch { }
        }

        public string? GetGroup(string projectJsonPath)
        {
            try
            {
                var dict = LoadAllMetadata();
                if (dict.ContainsKey(projectJsonPath))
                    return dict[projectJsonPath].Group;
            }
            catch { }
            return null;
        }

        public void SetGroup(string projectJsonPath, string? group)
        {
            try
            {
                var dict = LoadAllMetadata();
                dict[projectJsonPath] = new WallpaperMetadata(dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].Alias : null, dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].IsR18 : false, group);
                SaveAllMetadata(dict);
            }
            catch { }
        }

        public System.Collections.Generic.List<string> GetGroups()
        {
            try
            {
                var dict = LoadAllMetadata();
                var groups = new System.Collections.Generic.HashSet<string>();
                foreach (var kvp in dict)
                {
                    if (!string.IsNullOrEmpty(kvp.Value.Group))
                        groups.Add(kvp.Value.Group!);
                }
                return new System.Collections.Generic.List<string>(groups);
            }
            catch { }
            return new System.Collections.Generic.List<string>();
        }

        public List<string> GetFavorites()
        {
            try
            {
                if (File.Exists(_favoritesFilePath))
                {
                    string json = File.ReadAllText(_favoritesFilePath);
                    return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading favorites: {ex.Message}");
            }
            return new List<string>();
        }

        public void AddFavorite(string projectJsonPath)
        {
            try
            {
                List<string> favorites = GetFavorites();
                if (!favorites.Contains(projectJsonPath))
                {
                    favorites.Add(projectJsonPath);
                    SaveFavorites(favorites);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding favorite: {ex.Message}");
            }
        }

        public void RemoveFavorite(string projectJsonPath)
        {
            try
            {
                List<string> favorites = GetFavorites();
                if (favorites.Contains(projectJsonPath))
                {
                    favorites.Remove(projectJsonPath);
                    SaveFavorites(favorites);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing favorite: {ex.Message}");
            }
        }

        public bool IsFavorite(string projectJsonPath)
        {
            try
            {
                List<string> favorites = GetFavorites();
                return favorites.Contains(projectJsonPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking favorite: {ex.Message}");
                return false;
            }
        }

        public List<string> GetCategories()
        {
            try
            {
                if (File.Exists(_categoriesFilePath))
                {
                    string json = File.ReadAllText(_categoriesFilePath);
                    var categoriesData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (categoriesData != null)
                    {
                        return new List<string>(categoriesData.Values.Distinct());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading categories: {ex.Message}");
            }
            return new List<string>();
        }

        public string GetCategory(string projectJsonPath)
        {
            try
            {
                if (File.Exists(_categoriesFilePath))
                {
                    string json = File.ReadAllText(_categoriesFilePath);
                    var categoriesData = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (categoriesData != null && categoriesData.ContainsKey(projectJsonPath))
                    {
                        return categoriesData[projectJsonPath];
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting category: {ex.Message}");
            }
            return "All";
        }

        public void SetCategory(string projectJsonPath, string category)
        {
            try
            {
                var categoriesData = new Dictionary<string, string>();
                if (File.Exists(_categoriesFilePath))
                {
                    string json = File.ReadAllText(_categoriesFilePath);
                    categoriesData = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }

                categoriesData[projectJsonPath] = category;
                SaveCategories(categoriesData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting category: {ex.Message}");
            }
        }

        private void SaveFavorites(List<string> favorites)
        {
            try
            {
                string json = JsonSerializer.Serialize(favorites);
                File.WriteAllText(_favoritesFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving favorites: {ex.Message}");
            }
        }

        private void SaveCategories(Dictionary<string, string> categoriesData)
        {
            try
            {
                string json = JsonSerializer.Serialize(categoriesData);
                File.WriteAllText(_categoriesFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving categories: {ex.Message}");
            }
        }
    }
}