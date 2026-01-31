using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.ComponentModel;

namespace WallpaperDockWinUI.Services
{
    public interface IFavoritesService
    {
        // 事件定义
        event EventHandler<DataChangedEventArgs>? DataChanged;

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
        List<string>? GetGroups(string projectJsonPath);
        void AddGroupToWallpaper(string projectJsonPath, string groupName);
        void RemoveGroupFromWallpaper(string projectJsonPath, string groupName);
        void ClearGroupsFromWallpaper(string projectJsonPath);
        List<string> GetAllGroups();
        void AddGroup(string groupName);
        void RemoveGroup(string groupName);
        void RenameGroup(string oldGroupName, string newGroupName);
    }

    // 数据变更事件参数类
    public class DataChangedEventArgs : EventArgs
    {
        public string? DataType { get; }

        public DataChangedEventArgs(string? dataType = null)
        {
            DataType = dataType;
        }
    }

    public class FavoritesService : IFavoritesService
    {
        private readonly string _favoritesFilePath;
        private readonly string _categoriesFilePath;
        private readonly string _metadataFilePath;
        private readonly string _groupsFilePath;

        // 实现 DataChanged 事件
        public event EventHandler<DataChangedEventArgs>? DataChanged;

        // 触发数据变更事件的辅助方法
        protected virtual void OnDataChanged(string? dataType = null)
        {
            DataChanged?.Invoke(this, new DataChangedEventArgs(dataType));
        }

        public FavoritesService()
        {
            string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolderPath = Path.Combine(appDataPath, "WallpaperDock");
            Directory.CreateDirectory(appFolderPath);

            _favoritesFilePath = Path.Combine(appFolderPath, "favorites.json");
            _categoriesFilePath = Path.Combine(appFolderPath, "categories.json");
            _metadataFilePath = Path.Combine(appFolderPath, "metadata.json");
            _groupsFilePath = Path.Combine(appFolderPath, "groups.json");
        }

        private record WallpaperMetadata(string? Alias, bool IsR18, List<string>? Groups);

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
                dict[projectJsonPath] = new WallpaperMetadata(alias, dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].IsR18 : false, dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].Groups : null);
                SaveAllMetadata(dict);
                OnDataChanged("metadata");
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
                dict[projectJsonPath] = new WallpaperMetadata(dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].Alias : null, isR18, dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath].Groups : null);
                SaveAllMetadata(dict);
                OnDataChanged("metadata");
            }
            catch { }
        }

        public List<string>? GetGroups(string projectJsonPath)
        {
            try
            {
                var dict = LoadAllMetadata();
                if (dict.ContainsKey(projectJsonPath))
                    return dict[projectJsonPath].Groups;
            }
            catch { }
            return null;
        }

        public void AddGroupToWallpaper(string projectJsonPath, string groupName)
        {
            try
            {
                var dict = LoadAllMetadata();
                var existingMetadata = dict.ContainsKey(projectJsonPath) ? dict[projectJsonPath] : new WallpaperMetadata(null, false, null);
                var groups = existingMetadata.Groups ?? new List<string>();
                if (!groups.Contains(groupName))
                {
                    groups.Add(groupName);
                    dict[projectJsonPath] = new WallpaperMetadata(existingMetadata.Alias, existingMetadata.IsR18, groups);
                    SaveAllMetadata(dict);
                    OnDataChanged("metadata");
                }
            }
            catch { }
        }

        public void RemoveGroupFromWallpaper(string projectJsonPath, string groupName)
        {
            try
            {
                var dict = LoadAllMetadata();
                if (dict.ContainsKey(projectJsonPath))
                {
                    var existingMetadata = dict[projectJsonPath];
                    var groups = existingMetadata.Groups ?? new List<string>();
                    if (groups.Contains(groupName))
                    {
                        groups.Remove(groupName);
                        dict[projectJsonPath] = new WallpaperMetadata(existingMetadata.Alias, existingMetadata.IsR18, groups.Count > 0 ? groups : null);
                        SaveAllMetadata(dict);
                        OnDataChanged("metadata");
                    }
                }
            }
            catch { }
        }

        public void ClearGroupsFromWallpaper(string projectJsonPath)
        {
            try
            {
                var dict = LoadAllMetadata();
                if (dict.ContainsKey(projectJsonPath))
                {
                    var existingMetadata = dict[projectJsonPath];
                    dict[projectJsonPath] = new WallpaperMetadata(existingMetadata.Alias, existingMetadata.IsR18, null);
                    SaveAllMetadata(dict);
                    OnDataChanged("metadata");
                }
            }
            catch { }
        }

        private List<string> LoadGroups()
        {
            try
            {
                if (File.Exists(_groupsFilePath))
                {
                    string json = File.ReadAllText(_groupsFilePath);
                    var groups = JsonSerializer.Deserialize<List<string>>(json);
                    return groups ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading groups: {ex.Message}");
            }
            return new List<string>();
        }

        private void SaveGroups(List<string> groups)
        {
            try
            {
                string json = JsonSerializer.Serialize(groups);
                File.WriteAllText(_groupsFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving groups: {ex.Message}");
            }
        }

        public List<string> GetAllGroups()
        {
            return LoadGroups();
        }

        public void AddGroup(string groupName)
        {
            try
            {
                var groups = LoadGroups();
                if (!groups.Contains(groupName))
                {
                    groups.Add(groupName);
                    SaveGroups(groups);
                    OnDataChanged("groups");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding group: {ex.Message}");
            }
        }

        public void RemoveGroup(string groupName)
        {
            try
            {
                var groups = LoadGroups();
                if (groups.Contains(groupName))
                {
                    groups.Remove(groupName);
                    SaveGroups(groups);
                    OnDataChanged("groups");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error removing group: {ex.Message}");
            }
        }

        public void RenameGroup(string oldGroupName, string newGroupName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(oldGroupName) || string.IsNullOrWhiteSpace(newGroupName))
                    return;
                
                if (oldGroupName == newGroupName)
                    return;
                
                var groups = LoadGroups();
                if (groups.Contains(oldGroupName))
                {
                    // 从分组列表中移除旧名称并添加新名称
                    groups.Remove(oldGroupName);
                    if (!groups.Contains(newGroupName))
                    {
                        groups.Add(newGroupName);
                    }
                    SaveGroups(groups);
                    
                    // 更新所有使用该分组的壁纸的分组信息
                    var metadata = LoadAllMetadata();
                    foreach (var key in metadata.Keys)
                    {
                        var wallpaperMetadata = metadata[key];
                        if (wallpaperMetadata.Groups != null && wallpaperMetadata.Groups.Contains(oldGroupName))
                        {
                            var newGroups = new List<string>(wallpaperMetadata.Groups);
                            newGroups.Remove(oldGroupName);
                            newGroups.Add(newGroupName);
                            metadata[key] = new WallpaperMetadata(wallpaperMetadata.Alias, wallpaperMetadata.IsR18, newGroups);
                        }
                    }
                    SaveAllMetadata(metadata);
                    
                    OnDataChanged("groups");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error renaming group: {ex.Message}");
            }
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
                    OnDataChanged("favorites");
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
                    OnDataChanged("favorites");
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
                OnDataChanged("categories");
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