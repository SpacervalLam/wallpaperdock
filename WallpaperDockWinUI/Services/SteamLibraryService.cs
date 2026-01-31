using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace WallpaperDockWinUI.Services
{
    public interface ISteamLibraryService
    {
        List<string> GetAllSteamLibraryPaths();
        string? GetSteamPath();
    }

    public class SteamLibraryService : ISteamLibraryService
    {
        public string? GetSteamPath()
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

        public List<string> GetAllSteamLibraryPaths()
        {
            List<string> libraryPaths = new List<string>();

            // Get main Steam path
            string? steamPath = GetSteamPath();
            if (!string.IsNullOrEmpty(steamPath))
            {
                libraryPaths.Add(steamPath);

                // Read libraryfolders.vdf to find additional libraries
                string libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFoldersPath))
                {
                    try
                    {
                        string content = File.ReadAllText(libraryFoldersPath);
                        // Simple parsing: split by lines and look for path entries
                        string[] lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string line in lines)
                        {
                            string trimmedLine = line.Trim();
                            // Check if line contains path information
                            if (trimmedLine.Contains("path"))
                            {
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
                                                // Clean up the path
                                                path = path.Trim();
                                                path = path.Replace("\\\\", "\\");
                                                if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !libraryPaths.Contains(path))
                                                {
                                                    libraryPaths.Add(path);
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
                    }
                }


            }

            return libraryPaths;
        }
    }
}
