using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Shows a device's spec plate ONLY while the mouse hovers over (or near) that device in
    /// the Scene view, instead of always. Hover resolves by picking the device geometry, with
    /// a small screen-space proximity fallback so tiny bodies (e.g. the URG box) are easy to
    /// point at. Selected devices keep drawing their own interactive plate (each CustomEditor's
    /// OnSceneGUI); this hook covers the not-selected, hovered case, so the scene stays
    /// uncluttered until you point at a piece of equipment.
    /// </summary>
    [InitializeOnLoad]
    internal static class DeviceInfoPlateHover
    {
        private const float ProximityPixels = 36f;
        private static Component hoveredDevice;

        /// <summary>Hover plates on/off (toggled from the Scene-view overlay).</summary>
        public static bool Enabled { get; set; } = true;

        static DeviceInfoPlateHover()
        {
            SceneView.duringSceneGui += OnScene;
        }

        private static void OnScene(SceneView sceneView)
        {
            if (!Enabled)
            {
                return;
            }
            Event e = Event.current;
            if (e.type == EventType.MouseMove)
            {
                hoveredDevice = ResolveHover(e.mousePosition);
                sceneView.Repaint();
                return;
            }
            if (e.type != EventType.Repaint || hoveredDevice == null || hoveredDevice.Equals(null))
            {
                return;
            }
            if (IsSelected(hoveredDevice.gameObject))
            {
                return; // its own editor draws the interactive plate
            }

            Handles.BeginGUI();
            switch (hoveredDevice)
            {
                case VirtualProjectorLight projector:
                    DeviceInfoPlate.DrawPassive(projector,
                        ProjectorInfoText.Full(projector), ProjectorInfoText.Minimized(projector),
                        projector.InfoPlateScale, projector.InfoPlateMinimized);
                    break;
                case MonitorSurface monitor:
                    DeviceInfoPlate.DrawPassive(monitor,
                        MonitorInfoText.Full(monitor), MonitorInfoText.Minimized(monitor),
                        monitor.InfoPlateScale, monitor.InfoPlateMinimized);
                    break;
                case UrgRig urg:
                    DeviceInfoPlate.DrawPassive(urg,
                        UrgInfoText.Full(urg), UrgInfoText.Minimized(urg),
                        urg.InfoPlateScale, urg.InfoPlateMinimized);
                    break;
            }
            Handles.EndGUI();
        }

        private static Component ResolveHover(Vector2 mousePosition)
        {
            // 1) Pointing directly at the device geometry (body / panel).
            GameObject picked = HandleUtility.PickGameObject(mousePosition, false);
            if (picked != null)
            {
                Component direct = DeviceInParent(picked);
                if (direct != null)
                {
                    return direct;
                }
            }

            // 2) Near a device's screen anchor (forgiving for small bodies like the URG).
            Component best = null;
            float bestDistance = ProximityPixels;
            foreach (Component device in AllDevices())
            {
                Vector2 anchor = HandleUtility.WorldToGUIPoint(device.transform.position);
                float distance = Vector2.Distance(anchor, mousePosition);
                if (distance < bestDistance)
                {
                    best = device;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static Component DeviceInParent(GameObject go)
        {
            VirtualProjectorLight projector = go.GetComponentInParent<VirtualProjectorLight>();
            if (projector != null)
            {
                return projector;
            }
            MonitorSurface monitor = go.GetComponentInParent<MonitorSurface>();
            if (monitor != null)
            {
                return monitor;
            }
            return go.GetComponentInParent<UrgRig>();
        }

        private static IEnumerable<Component> AllDevices()
        {
            foreach (VirtualProjectorLight p in Object.FindObjectsByType<VirtualProjectorLight>(FindObjectsSortMode.None))
            {
                yield return p;
            }
            foreach (MonitorSurface m in Object.FindObjectsByType<MonitorSurface>(FindObjectsSortMode.None))
            {
                yield return m;
            }
            foreach (UrgRig u in Object.FindObjectsByType<UrgRig>(FindObjectsSortMode.None))
            {
                yield return u;
            }
        }

        private static bool IsSelected(GameObject go)
        {
            foreach (GameObject s in Selection.gameObjects)
            {
                if (s == go)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
