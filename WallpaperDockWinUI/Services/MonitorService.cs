using Microsoft.UI.Windowing;
using System.Collections.Generic;
using Windows.Graphics.Display;

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

            // Get all display areas
            var displayAreas = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(0, 0), DisplayAreaFallback.Primary);
            if (displayAreas != null)
            {
                // Add primary monitor
                monitors.Add(new MonitorInfo
                {
                    Index = index,
                    Name = "Primary Monitor",
                    Bounds = displayAreas.WorkArea,
                    IsPrimary = true
                });
                index++;

                // Note: This is a simplified approach. In a real-world scenario,
                // you would use a more comprehensive method to enumerate all monitors.
                // For now, we'll just return the primary monitor and assume it's monitor 0.
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
    }
}
