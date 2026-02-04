using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

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
        public List<string>? Groups { get; set; }

        public override string ToString()
        {
            return Title ?? "Unknown Wallpaper";
        }

        public override bool Equals(object? obj)
        {
            if (obj is WallpaperInfo other)
            {
                // 使用 ProjectJsonPath 作为唯一标识来判断壁纸是否相同
                return string.Equals(this.ProjectJsonPath, other.ProjectJsonPath, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            // 使用 ProjectJsonPath 的哈希码，确保相同 ProjectJsonPath 的壁纸具有相同的哈希码
            return ProjectJsonPath?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
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
        void SwitchWallpaper(string projectJsonPath, int monitorIndex = -1, bool silent = false);
        string? GetWallpaperEnginePath();
        void OpenWallpaperEngineMainWindow();
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

        public void SwitchWallpaper(string projectJsonPath, int monitorIndex = -1, bool silent = false)
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
                
                // If need silent, add properties parameter
                if (silent)
                {
                    arguments += " -properties \"{\\\"volume\\\":0}\"";
                }
                
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

        public void OpenWallpaperEngineMainWindow()
        {
            try
            {
                string? wallpaperEnginePath = GetWallpaperEnginePath();
                if (string.IsNullOrEmpty(wallpaperEnginePath) || !File.Exists(wallpaperEnginePath))
                {
                    Console.WriteLine("错误：未找到 Wallpaper Engine 可执行文件，请在设置中手动指定路径或安装 Wallpaper Engine。");
                    return;
                }

                Console.WriteLine($"找到 Wallpaper Engine 路径: {wallpaperEnginePath}");

                // 1) 如果已经运行 -> 尝试唤回主窗口（更可靠）
                var runningProcess = GetRunningWallpaperProcess();
                if (runningProcess != null)
                {
                    Console.WriteLine($"Wallpaper Engine 已在运行，进程 ID: {runningProcess.Id}");
                    IntPtr hWnd = runningProcess.MainWindowHandle;
                    if (hWnd == IntPtr.Zero)
                    {
                        // 有时 MainWindowHandle 可能为 0（尚未创建或隐藏），尝试枚举或直接尝试启动无参数以让主程序处理
                        Console.WriteLine("MainWindowHandle 为 0，尝试启动无参数以唤回窗口");
                        TryStartProcessNormally(wallpaperEnginePath);
                        return;
                    }

                    // 如果最小化则还原
                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        Console.WriteLine("Wallpaper Engine 窗口已从最小化状态还原");
                    }

                    // 试图安全地把窗口置前
                    BringWindowToFront(hWnd);
                    Console.WriteLine("已唤回 Wallpaper Engine 主窗口（进程已在运行）。");
                    return;
                }

                // 2) 未运行 -> 正常启动 exe
                Console.WriteLine("Wallpaper Engine 未运行，尝试启动可执行程序");
                TryStartProcessNormally(wallpaperEnginePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动 Wallpaper Engine 失败: {ex.Message}");
            }
        }

        // ----------------- P/Invoke 用于将窗口带到前台 -----------------
        private const int SW_RESTORE = 9;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        // -----------------------------------------------------------------

        private bool IsWallpaperEngineRunning()
        {
            try
            {
                // Check for both 32-bit and 64-bit versions
                Process[] processes32 = Process.GetProcessesByName("wallpaper32");
                Process[] processes64 = Process.GetProcessesByName("wallpaper64");
                
                return processes32.Length > 0 || processes64.Length > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking if Wallpaper Engine is running: {ex.Message}");
                return false;
            }
        }

        private Process? GetRunningWallpaperProcess()
        {
            try
            {
                var procs = Process.GetProcesses();
                // 优先查找 wallpaper64 / wallpaper32
                var p = procs.FirstOrDefault(p => string.Equals(p.ProcessName, "wallpaper64", StringComparison.OrdinalIgnoreCase)
                                              || string.Equals(p.ProcessName, "wallpaper32", StringComparison.OrdinalIgnoreCase));
                return p;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"检查 Wallpaper Engine 是否正在运行时发生错误: {ex.Message}");
                return null;
            }
        }

        private void TryStartProcessNormally(string wallpaperEnginePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = wallpaperEnginePath,
                    Arguments = "", // 不传参数以确保主程序正常启动并显示主窗口
                    UseShellExecute = true, // 启动 GUI 应用时建议 true
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(wallpaperEnginePath) ?? Environment.CurrentDirectory
                };

                Process? proc = Process.Start(startInfo);
                if (proc != null)
                {
                    Console.WriteLine("成功启动 Wallpaper Engine 可执行程序。");
                    // 不等待退出，不做 WaitForExit
                }
                else
                {
                    Console.WriteLine("启动 Wallpaper Engine 失败：Process.Start 返回 null。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动 Wallpaper Engine 时发生异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 尝试把窗口带到前台（安全方式，考虑线程输入 attach）
        /// </summary>
        private void BringWindowToFront(IntPtr hWnd)
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == hWnd)
                    return;

                // 获取线程 id
                uint fgThread = GetWindowThreadProcessId(fg, out _);
                uint targetThread = GetWindowThreadProcessId(hWnd, out _);

                uint currentThread = GetCurrentThreadId();

                // attach 动作使 SetForegroundWindow 更有可能成功
                bool attached = false;
                try
                {
                    if (AttachThreadInput(currentThread, fgThread, true))
                    {
                        attached = true;
                    }
                    // 还原/显示窗口
                    if (IsIconic(hWnd))
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                    }
                    SetForegroundWindow(hWnd);
                }
                finally
                {
                    if (attached)
                    {
                        AttachThreadInput(currentThread, fgThread, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"尝试将窗口置前失败: {ex.Message}");
            }
        }
    }
}
