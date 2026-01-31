using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.Win32;

namespace WallpaperDock
{
    public class WallpaperInfo
    {
        public string? Title { get; set; }
        public string? PreviewPath { get; set; }
        public string? ProjectJsonPath { get; set; }
        public string? ThemeColor { get; set; }
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

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Wallpaper Dock - Wallpaper Engine Manager");
            Console.WriteLine("=========================================");

            try
            {
                // Step 1: Get all Steam library paths
                List<string> libraryPaths = GetAllSteamLibraryPaths();
                if (libraryPaths.Count == 0)
                {
                    Console.WriteLine("Error: Could not find any Steam library paths.");
                    return;
                }

                Console.WriteLine("Found Steam libraries at:");
                foreach (string path in libraryPaths)
                {
                    Console.WriteLine($"- {path}");
                }

                // Step 2: Find Wallpaper Engine workshop directories
                List<string> workshopPaths = new List<string>();
                foreach (string libraryPath in libraryPaths)
                {
                    string workshopPath = Path.Combine(libraryPath, "steamapps", "workshop", "content", "431960");
                    if (Directory.Exists(workshopPath))
                    {
                        workshopPaths.Add(workshopPath);
                        Console.WriteLine($"Found Workshop directory: {workshopPath}");
                    }
                }

                if (workshopPaths.Count == 0)
                {
                    Console.WriteLine("Error: Could not find any Wallpaper Engine workshop directories.");
                    return;
                }

                // Step 3: Scan and parse wallpaper information from all workshop directories
                List<WallpaperInfo> wallpapers = new List<WallpaperInfo>();
                foreach (string workshopPath in workshopPaths)
                {
                    wallpapers.AddRange(ScanWallpapers(workshopPath));
                }

                Console.WriteLine($"Found {wallpapers.Count} wallpapers in total.");

                // Step 4: Display wallpaper information
                for (int i = 0; i < wallpapers.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {wallpapers[i].Title ?? "Unknown Title"}");
                    Console.WriteLine($"   Preview: {wallpapers[i].PreviewPath ?? "No Preview"}");
                    Console.WriteLine($"   Project JSON: {wallpapers[i].ProjectJsonPath ?? "No Path"}");
                    Console.WriteLine($"   Theme Color: {wallpapers[i].ThemeColor ?? "No Color"}");
                    Console.WriteLine();
                }

                // Step 5: Test wallpaper switching
                if (wallpapers.Count > 0)
                {
                    Console.WriteLine("Enter the number of the wallpaper to switch to (or 0 to exit):");
                    string? input = Console.ReadLine();
                    if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int choice) && choice > 0 && choice <= wallpapers.Count)
                    {
                        WallpaperInfo selectedWallpaper = wallpapers[choice - 1];
                        if (!string.IsNullOrEmpty(selectedWallpaper.ProjectJsonPath))
                        {
                            SwitchWallpaper(selectedWallpaper.ProjectJsonPath);
                        }
                        else
                        {
                            Console.WriteLine("Error: Selected wallpaper has no project.json path.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        static string? GetSteamPath()
        {
            // Try to get Steam path from registry
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string? steamPath = key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrEmpty(steamPath))
                        {
                            return steamPath;
                        }
                    }

                    // Try 64-bit registry
                    using (RegistryKey? localKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                    {
                        if (localKey != null)
                        {
                            string? steamPath = localKey.GetValue("InstallPath") as string;
                            if (!string.IsNullOrEmpty(steamPath))
                            {
                                return steamPath;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading registry: {ex.Message}");
            }

            return null;
        }

        static List<string> GetAllSteamLibraryPaths()
        {
            List<string> libraryPaths = new List<string>();

            // Get main Steam path
            string? steamPath = GetSteamPath();
            if (!string.IsNullOrEmpty(steamPath))
            {
                libraryPaths.Add(steamPath);
                Console.WriteLine($"Added main Steam path: {steamPath}");

                // Read libraryfolders.vdf to find additional libraries
                string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                Console.WriteLine($"Looking for libraryfolders.vdf at: {libraryFoldersPath}");
                
                if (File.Exists(libraryFoldersPath))
                {
                    Console.WriteLine("Found libraryfolders.vdf, attempting to parse...");
                    try
                    {
                        string content = File.ReadAllText(libraryFoldersPath);
                        Console.WriteLine("Successfully read libraryfolders.vdf content");
                        
                        // Simple parsing: split by lines and look for path entries
                        string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        Console.WriteLine($"Split into {lines.Length} lines");
                        
                        foreach (string line in lines)
                        {
                            string trimmedLine = line.Trim();
                            Console.WriteLine($"Processing line: {trimmedLine}");
                            
                            // Check if line contains path information
                            if (trimmedLine.Contains("path"))
                            {
                                Console.WriteLine("Line contains 'path', attempting to extract path");
                                // Extract everything between quotes
                                int firstQuote = trimmedLine.IndexOf('"');
                                if (firstQuote != -1)
                                {
                                    int secondQuote = trimmedLine.IndexOf('"', firstQuote + 1);
                                    if (secondQuote != -1)
                                    {
                                        int thirdQuote = trimmedLine.IndexOf('"', secondQuote + 1);
                                        if (thirdQuote != -1)
                                        {
                                            int fourthQuote = trimmedLine.IndexOf('"', thirdQuote + 1);
                                            if (fourthQuote != -1)
                                            {
                                                string path = trimmedLine.Substring(thirdQuote + 1, fourthQuote - thirdQuote - 1);
                                                Console.WriteLine($"Extracted path: {path}");
                                                
                                                // Clean up the path
                                                path = path.Trim();
                                                path = path.Replace("\\\\", "\\");
                                                
                                                Console.WriteLine($"Cleaned path: {path}");
                                                
                                                if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !libraryPaths.Contains(path))
                                                {
                                                    libraryPaths.Add(path);
                                                    Console.WriteLine($"Added library path: {path}");
                                                }
                                                else
                                                {
                                                    if (!Directory.Exists(path))
                                                    {
                                                        Console.WriteLine($"Path does not exist: {path}");
                                                    }
                                                    else if (libraryPaths.Contains(path))
                                                    {
                                                        Console.WriteLine($"Path already exists in list: {path}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing libraryfolders.vdf: {ex.Message}");
                        Console.WriteLine(ex.StackTrace);
                    }
                }
                else
                {
                    Console.WriteLine("libraryfolders.vdf not found");
                }
                

            }

            return libraryPaths;
        }

        static List<WallpaperInfo> ScanWallpapers(string workshopPath)
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
                                        string[] commonPreviews = { "preview.jpg", "preview.png" };
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

        static void SwitchWallpaper(string projectJsonPath)
        {
            try
            {
                // Find Wallpaper Engine executable path by searching all library paths
                List<string> libraryPaths = GetAllSteamLibraryPaths();
                string? wallpaperEnginePath = null;

                foreach (string libraryPath in libraryPaths)
                {
                    string potentialPath32 = Path.Combine(libraryPath, "steamapps", "common", "wallpaper_engine", "wallpaper32.exe");
                    string potentialPath64 = Path.Combine(libraryPath, "steamapps", "common", "wallpaper_engine", "wallpaper64.exe");

                    if (File.Exists(potentialPath32))
                    {
                        wallpaperEnginePath = potentialPath32;
                        break;
                    }
                    else if (File.Exists(potentialPath64))
                    {
                        wallpaperEnginePath = potentialPath64;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(wallpaperEnginePath))
                {
                    Console.WriteLine("Error: Could not find Wallpaper Engine executable.");
                    return;
                }

                // Build command arguments
                string arguments = $"-control openWallpaper -file \"{projectJsonPath}\"";

                Console.WriteLine($"Switching to wallpaper: {projectJsonPath}");
                Console.WriteLine($"Running: {wallpaperEnginePath} {arguments}");

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
                        if (process.ExitCode == 0)
                        {
                            Console.WriteLine("Wallpaper switched successfully!");
                        }
                        else
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
