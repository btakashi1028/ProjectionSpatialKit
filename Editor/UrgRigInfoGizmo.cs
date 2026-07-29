using UnityEditor;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>URG spec plate content; rendering/interaction is DeviceInfoPlate.</summary>
    internal static class UrgInfoText
    {
        internal static string Full(UrgRig u)
        {
            return
                $"<b>URG · scanning rangefinder</b>\n" +
                $"mode   {u.Mode}\n" +
                $"fan    {u.ScanAngleDegrees:0}°   {u.BeamCount} beams\n" +
                $"range  {u.MaxRangeMetres:0.0} m\n" +
                $"status {u.LastStatus}";
        }

        internal static string Minimized(UrgRig u)
        {
            return $"<b>▸ URG ({u.Mode})</b>";
        }
    }

    // The passive plate is shown on mouse-hover by DeviceInfoPlateHover (not always).

    /// <summary>Interactive plate (minimize/resize) shown when the URG IS selected.</summary>
    [CustomEditor(typeof(UrgRig))]
    public sealed class UrgRigEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            UrgRig urg = (UrgRig)target;
            Handles.BeginGUI();
            DeviceInfoPlate.DrawInteractive(urg,
                UrgInfoText.Full(urg), UrgInfoText.Minimized(urg),
                urg.InfoPlateScale, urg.InfoPlateMinimized);
            Handles.EndGUI();
        }
    }
}
