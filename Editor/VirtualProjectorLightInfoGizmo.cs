using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>Projector spec plate content; rendering/interaction is DeviceInfoPlate.</summary>
    internal static class ProjectorInfoText
    {
        internal static string Full(VirtualProjectorLight p)
        {
            ProjectorDeviceProfile dp = p.DeviceProfile;
            string model = dp != null ? dp.modelName : "(no device profile)";
            string resolution = dp != null ? $"{dp.resolution.x}x{dp.resolution.y}" : "—";
            string lumens = dp != null ? $"{dp.brightnessLumens:0} lm" : "—";
            return
                $"<b>PROJECTOR · {model}</b>\n" +
                $"res    {resolution}   {lumens}\n" +
                $"throw  {p.EffectiveThrowRatio:0.00}  (zoom {p.Zoom:0.00})\n" +
                $"fov    H {p.HorizontalFovDegrees:0.0}°   ar {p.ImageAspect:0.00}" +
                (p.ImageOrientation == DisplayOrientation.Portrait ? "   portrait" : "") + "\n" +
                $"focus  {p.FocusDistance:0.0} m   f/{p.Aperture:0.0}\n" +
                $"shift  {p.LensShift.x:0.00} / {p.LensShift.y:0.00}   keystone {p.VerticalKeystone:0.00}";
        }

        internal static string Minimized(VirtualProjectorLight p)
        {
            return "<b>▸ " + (p.DeviceProfile != null ? p.DeviceProfile.modelName : "PROJECTOR") + "</b>";
        }
    }

    // The passive plate is shown on mouse-hover by DeviceInfoPlateHover (not always).

    /// <summary>Interactive plate (minimize/resize) shown when the projector IS selected.</summary>
    [CustomEditor(typeof(VirtualProjectorLight))]
    public sealed class VirtualProjectorLightEditor : UnityEditor.Editor
    {
        private void OnSceneGUI()
        {
            VirtualProjectorLight projector = (VirtualProjectorLight)target;
            Handles.BeginGUI();
            DeviceInfoPlate.DrawInteractive(projector,
                ProjectorInfoText.Full(projector), ProjectorInfoText.Minimized(projector),
                projector.InfoPlateScale, projector.InfoPlateMinimized);
            Handles.EndGUI();
        }
    }
}
