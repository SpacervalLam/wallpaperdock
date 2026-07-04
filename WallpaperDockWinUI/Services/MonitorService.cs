using Microsoft.UI.Windowing;
using System.Collections.Generic;
using Windows.Graphics.Display;
using System.Runtime.InteropServices;

namespace WallpaperDockWinUI.Services
{
    public interface IMonitorService
    {
        List<MonitorInfo> GetAllMonitors();
        int GetMonitorCount();
        MonitorInfo GetPrimaryMonitor();
    }

    public class MonitorInfo
    {
        public int Index { get; set; }
        public required string Name { get; set; }
        public Windows.Graphics.RectInt32 Bounds { get; set; }
        public bool IsPrimary { get; set; }
    }

    public class MonitorService : IMonitorService
    {
        public List<MonitorInfo> GetAllMonitors()
        {
            List<MonitorInfo> monitors = new List<MonitorInfo>();
            int index = 0;

            // Use Windows API to enumerate all displays
            var displayDevice = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };

            for (uint i = 0; EnumDisplayDevices(null, i, ref displayDevice, 0); i++)
            {
                if (displayDevice.StateFlags.HasFlag(DISPLAY_DEVICE_FLAGS.DISPLAY_DEVICE_ACTIVE))
                {
                    // 修复：原来只调用 EnumDisplayDevices(adapterName, 0, ...)，仅枚举每个适配器的第 0 号显示器。
                    // 现代多显示器系统通常是一个适配器拖多个显示器，会漏掉除第一块屏以外的所有屏幕。
                    // 改为遍历该适配器下所有显示器。
                    for (uint j = 0; ; j++)
                    {
                        var detailedDisplayDevice = new DISPLAY_DEVICE { cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                        if (!EnumDisplayDevices(displayDevice.DeviceName, j, ref detailedDisplayDevice, 0))
                            break;

                        // 只要处于活动状态的显示器
                        if (!detailedDisplayDevice.StateFlags.HasFlag(DISPLAY_DEVICE_FLAGS.DISPLAY_DEVICE_ACTIVE))
                            continue;

                        // Get display settings
                        var devMode = new DEVMODE { dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
                        if (EnumDisplaySettings(displayDevice.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode))
                        {
                            // Check if this is the primary monitor
                            bool isPrimary = (devMode.dmPosition.x == 0 && devMode.dmPosition.y == 0)
                                || displayDevice.StateFlags.HasFlag(DISPLAY_DEVICE_FLAGS.DISPLAY_DEVICE_PRIMARY_DEVICE);

                            // 显示器名称：detailedDisplayDevice.DeviceString 通常是显示器型号；
                            // 若为空则回退到适配器名以避免 UI 显示空白
                            string monitorName = string.IsNullOrWhiteSpace(detailedDisplayDevice.DeviceString)
                                ? displayDevice.DeviceString
                                : detailedDisplayDevice.DeviceString;

                            monitors.Add(new MonitorInfo
                            {
                                Index = index,
                                Name = monitorName,
                                Bounds = new Windows.Graphics.RectInt32
                                {
                                    X = devMode.dmPosition.x,
                                    Y = devMode.dmPosition.y,
                                    Width = devMode.dmPelsWidth,
                                    Height = devMode.dmPelsHeight
                                },
                                IsPrimary = isPrimary
                            });
                            index++;
                        }
                    }
                }
            }

            // Fallback to DisplayArea if no monitors found via Windows API
            if (monitors.Count == 0)
            {
                var displayArea = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(0, 0), DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    monitors.Add(new MonitorInfo
                    {
                        Index = 0,
                        Name = "Primary Monitor",
                        Bounds = displayArea.WorkArea,
                        IsPrimary = true
                    });
                }
            }

            return monitors;
        }

        public int GetMonitorCount()
        {
            return GetAllMonitors().Count;
        }

        public MonitorInfo GetPrimaryMonitor()
        {
            var monitors = GetAllMonitors();
            return monitors.Find(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        }

        // Windows API definitions
        private const int ENUM_CURRENT_SETTINGS = -1;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public DISPLAY_DEVICE_FLAGS StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [Flags]
        private enum DISPLAY_DEVICE_FLAGS : int
        {
            DISPLAY_DEVICE_ACTIVE = 0x00000001,
            DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public POINTL dmPosition;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTL
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);
    }
}
