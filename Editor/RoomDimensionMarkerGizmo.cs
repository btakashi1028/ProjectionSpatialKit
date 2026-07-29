using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Draws the <see cref="RoomDimensionMarker"/>'s star and dotted measurement axes
    /// with metre labels in the Scene view. Lives in the editor assembly so it can use
    /// Handles (dotted lines + labels) without pulling UnityEditor into runtime code.
    /// </summary>
    public static class RoomDimensionMarkerGizmo
    {
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Pickable)]
        private static void Draw(RoomDimensionMarker marker, GizmoType gizmoType)
        {
            RoomBox room = marker.Room;
            Vector3 markerPos = marker.transform.position;

            if (room == null)
            {
                DrawStar(markerPos, marker.transform.rotation, marker.StarSize, marker.StarColor);
                return;
            }

            // Caliper lines that always span the room wall-to-wall (so they never poke
            // through a wall), positioned to pass through the marker. Moving the marker
            // slides where the measurement is taken; the length stays the room's inner size.
            // Computed in the room's local unit-cube space [-0.5, +0.5] so it follows the
            // room's transform (position, rotation, scale) exactly.
            Transform rt = room.transform;
            Vector3 local = rt.InverseTransformPoint(markerPos);
            local.x = Mathf.Clamp(local.x, -0.5f, 0.5f);
            local.y = Mathf.Clamp(local.y, -0.5f, 0.5f);
            local.z = Mathf.Clamp(local.z, -0.5f, 0.5f);
            Vector3 size = room.InnerSize;

            DrawSpan(
                rt.TransformPoint(new Vector3(-0.5f, local.y, local.z)),
                rt.TransformPoint(new Vector3(0.5f, local.y, local.z)),
                marker.XColor, "W", size.x);
            DrawSpan(
                rt.TransformPoint(new Vector3(local.x, -0.5f, local.z)),
                rt.TransformPoint(new Vector3(local.x, 0.5f, local.z)),
                marker.YColor, "H", size.y);
            DrawSpan(
                rt.TransformPoint(new Vector3(local.x, local.y, -0.5f)),
                rt.TransformPoint(new Vector3(local.x, local.y, 0.5f)),
                marker.ZColor, "D", size.z);

            DrawStar(markerPos, marker.transform.rotation, marker.StarSize, marker.StarColor);
        }

        private static void DrawSpan(Vector3 a, Vector3 b, Color color, string label, float meters)
        {
            Handles.color = color;
            Handles.DrawDottedLine(a, b, 4f);

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = color;
            // Offset the label along the span (not the midpoint) so the three axis labels
            // don't stack on top of each other when the marker sits at the room centre.
            Handles.Label(Vector3.Lerp(a, b, 0.72f), $"{label}: {meters:F2} m", style);
        }

        private static void DrawStar(Vector3 center, Quaternion rotation, float radius, Color color)
        {
            const int points = 5;
            float innerRadius = radius * 0.42f;
            Vector3[] star = new Vector3[points * 2 + 1];
            for (int i = 0; i < points * 2; i++)
            {
                float angle = Mathf.PI / 2f + i * Mathf.PI / points; // start pointing up
                float r = (i % 2 == 0) ? radius : innerRadius;
                Vector3 local = new Vector3(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r, 0f);
                star[i] = center + rotation * local;
            }
            star[points * 2] = star[0];

            Handles.color = new Color(color.r, color.g, color.b, 0.8f);
            Handles.DrawAAPolyLine(2f, star);
        }
    }
}
