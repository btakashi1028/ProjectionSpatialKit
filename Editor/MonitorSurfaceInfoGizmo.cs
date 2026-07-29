using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>Monitor spec plate content; rendering/interaction is DeviceInfoPlate.</summary>
    internal static class MonitorInfoText
    {
        internal static string Full(MonitorSurface m)
        {
            MonitorDeviceProfile dp = m.DeviceProfile;
            string model = dp != null ? dp.modelName : "(no device profile)";
            string resolution = dp != null ? $"{dp.resolution.x}x{dp.resolution.y}" : "—";
            string nits = dp != null ? $"{dp.brightnessNits:0} nit" : "—";
            string inches = dp != null ? $"{dp.diagonalInches:0.#}\"" : "—";
            Vector2 size = m.PanelSize;
            return
                $"<b>MONITOR · {model}</b>\n" +
                $"panel  {inches}   {size.x:0.00}x{size.y:0.00} m\n" +
                $"res    {resolution}   {nits}\n" +
                $"touch  {(m.IsTouchEnabled ? "yes" : "no")}   display {m.ContentDisplayIndex + 1}" +
                (m.Orientation == DisplayOrientation.Portrait ? "   portrait" : "");
        }

        internal static string Minimized(MonitorSurface m)
        {
            return "<b>▸ " + (m.DeviceProfile != null ? m.DeviceProfile.modelName : "MONITOR") + "</b>";
        }
    }

    // The passive plate is shown on mouse-hover by DeviceInfoPlateHover (not always).

    /// <summary>Interactive plate (minimize/resize) shown when the monitor IS selected.</summary>
    [CustomEditor(typeof(MonitorSurface))]
    public sealed class MonitorSurfaceEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            MonitorSurface monitor = (MonitorSurface)target;
            Handles.BeginGUI();
            DeviceInfoPlate.DrawInteractive(monitor,
                MonitorInfoText.Full(monitor), MonitorInfoText.Minimized(monitor),
                monitor.InfoPlateScale, monitor.InfoPlateMinimized);
            Handles.EndGUI();
        }
    }
}
