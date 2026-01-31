using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace WallpaperDockWinUI.Services
{
    public class WallpaperInfo
    {
        public string? Title { get; set; }
        public string? PreviewPath { get; set; }
        public string? ProjectJsonPath { get; set; }
        public string? ThemeColor { get; set; }
        public string? Category { get; set; }
        public bool IsFavorite { get; set; }

        // 新增元数据字段（UI / 本地设置）
        public string? Alias { get; set; }
        public bool IsR18 { get; set; }
        public string? Group { get; set; }

        public override string ToString()
        {
            return Title ?? "Unknown Wallpaper";
        }
    }

    public class ProjectJson
    {
        public string? title { get; set; }
        public string? preview { get; set; }
        public General? general { get; set; }
    }

    public class General
    {
        public Subtheme? subtheme { get; set; }
    }

    public class Subtheme
    {
        public string? schemecolor { get; set; }
    }

    public interface IWallpaperService
    {
        List<WallpaperInfo> ScanWallpapers(string workshopPath);
        void SwitchWallpaper(string projectJsonPath, int monitorIndex = -1);
        string? GetWallpaperEnginePath();
    }

    public class WallpaperEngineService : IWallpaperService
    {
        private readonly ISteamLibraryService _steamLibraryService;

        public WallpaperEngineService(ISteamLibraryService steamLibraryService)
        {
            _steamLibraryService = steamLibraryService;
        }

        public string? GetWallpaperEnginePath()
        {
            List<string> libraryPaths = _steamLibraryService.GetAllSteamLibraryPaths();
            foreach (string libraryPath in libraryPaths)
            {
                string potentialPath32 = Path.Combine(libraryPath, "steamapps", "common", "wallpaper_engine", "wallpaper32.exe");
                string potentialPath64 = Path.Combine(libraryPath, "steamapps", "common", "wallpaper_engine", "wallpaper64.exe");

                if (File.Exists(potentialPath32))
                {
                    return potentialPath32;
                }
                else if (File.Exists(potentialPath64))
                {
                    return potentialPath64;
                }
            }
            return null;
        }

        public List<WallpaperInfo> ScanWallpapers(string workshopPath)
        {
            List<WallpaperInfo> wallpapers = new List<WallpaperInfo>();

            try
            {
                // Get all subdirectories (each is a wallpaper)
                string[] subdirectories = Directory.GetDirectories(workshopPath);

                foreach (string dir in subdirectories)
                {
                    string projectJsonPath = Path.Combine(dir, "project.json");
                    if (File.Exists(projectJsonPath))
                    {
                        try
                        {
                            // Read and parse project.json
                            string jsonContent = File.ReadAllText(projectJsonPath);
                            ProjectJson? projectJson = JsonSerializer.Deserialize<ProjectJson>(jsonContent);

                            if (projectJson != null)
                            {
                                WallpaperInfo wallpaper = new WallpaperInfo
                                {
                                    Title = projectJson.title,
                                    ProjectJsonPath = projectJsonPath
                                };

                                // Get preview image path
                                if (!string.IsNullOrEmpty(projectJson.preview))
                                {
                                    string previewPath = Path.Combine(dir, projectJson.preview);
                                    if (File.Exists(previewPath))
                                    {
                                        wallpaper.PreviewPath = previewPath;
                                    }
                                    else
                                    {
                                        // Try common preview image names
                                        string[] commonPreviews = { "preview.jpg", "preview.png", "preview.gif" };
                                        foreach (string previewName in commonPreviews)
                                        {
                                            string commonPreviewPath = Path.Combine(dir, previewName);
                                            if (File.Exists(commonPreviewPath))
                                            {
                                                wallpaper.PreviewPath = commonPreviewPath;
                                                break;
                                            }
                                        }
                                    }
                                }

                                // Get theme color
                                if (projectJson.general != null && projectJson.general.subtheme != null)
                                {
                                    wallpaper.ThemeColor = projectJson.general.subtheme.schemecolor;
                                }

                                wallpapers.Add(wallpaper);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error parsing project.json in {dir}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning wallpapers: {ex.Message}");
            }

            return wallpapers;
        }

        public void SwitchWallpaper(string projectJsonPath, int monitorIndex = -1)
        {
            try
            {
                // Get Wallpaper Engine executable path
                string? wallpaperEnginePath = GetWallpaperEnginePath();
                if (string.IsNullOrEmpty(wallpaperEnginePath))
                {
                    Console.WriteLine("Error: Could not find Wallpaper Engine executable.");
                    return;
                }

                // Build command arguments
                string arguments = $"-control openWallpaper -file \"{projectJsonPath}\"";
                if (monitorIndex >= 0)
                {
                    arguments += $" -monitor {monitorIndex}";
                }

                // Execute the command
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = wallpaperEnginePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process? process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            Console.WriteLine($"Error switching wallpaper. Exit code: {process.ExitCode}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Error starting Wallpaper Engine process.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error switching wallpaper: {ex.Message}");
            }
        }
    }
}
