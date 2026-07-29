using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Scene-view "click a surface to aim the projection" mode. Lives outside any Inspector
    /// so it works regardless of what is selected — toggled from the Scene-view overlay.
    /// Clicking the INSIDE of the room model places the projector + URG on that surface via
    /// <see cref="ProjectionRig.AimAtSurface"/>: any orientation (walls, slanted partitions,
    /// floor, ceiling), driven by the clicked point and its inward normal.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectionAimTool
    {
        private static bool active;
        private static GUIStyle hintStyle;

        public static bool Active
        {
            get => active;
            set
            {
                if (active == value)
                {
                    return;
                }
                active = value;
                SceneView.RepaintAll();
            }
        }

        static ProjectionAimTool()
        {
            SceneView.duringSceneGui += OnScene;
        }

        private static void OnScene(SceneView sceneView)
        {
            if (!active)
            {
                return;
            }

            // Take over scene input so a click aims instead of selecting. Clicks on the
            // overlay panel itself are UI-Toolkit events and never reach here.
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            EditorGUIUtility.AddCursorRect(new Rect(0f, 0f, 100000f, 100000f), MouseCursor.ArrowPlus);

            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                Active = false;
                e.Use();
                return;
            }
            if (e.type == EventType.Repaint)
            {
                DrawHint(sceneView);
                return;
            }
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                ProjectionRig rig = Object.FindFirstObjectByType<ProjectionRig>();
                if (rig == null)
                {
                    Debug.LogWarning("[SpatialKit] シーンに ProjectionRig(投影セット)がありません。");
                }
                else if (TryPickVisibleSurface(HandleUtility.GUIPointToWorldRay(e.mousePosition),
                             out Vector3 point, out Vector3 inwardNormal))
                {
                    Apply(rig, point, inwardNormal);
                }
                e.Use();
            }
        }

        private static void DrawHint(SceneView sceneView)
        {
            if (hintStyle == null)
            {
                hintStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.75f, 0.35f) }
                };
            }
            Handles.BeginGUI();
            Rect band = new Rect(0f, sceneView.position.height - 58f, sceneView.position.width, 22f);
            EditorGUI.DrawRect(band, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(band, "部屋の内側の面(壁 / 床 / 天井 / 斜め)をクリックして投影面に設定  —  Esc で解除", hintStyle);
            Handles.EndGUI();
        }

        private static void Apply(ProjectionRig rig, Vector3 point, Vector3 inwardNormal)
        {
            var record = new System.Collections.Generic.List<Object> { rig };
            if (rig.Projector != null)
            {
                record.Add(rig.Projector.transform);
            }
            if (rig.Urg != null)
            {
                record.Add(rig.Urg.transform);
            }
            Undo.RecordObjects(record.ToArray(), "Aim projection at surface");
            rig.AimAtSurface(point, inwardNormal);
            if (rig.Projector != null)
            {
                EditorUtility.SetDirty(rig.Projector.transform);
            }
            if (rig.Urg != null)
            {
                EditorUtility.SetDirty(rig.Urg.transform);
            }
            SceneView.RepaintAll();
        }

        /// <summary>
        /// The visible interior surface under the ray (front-facing to the camera). For the
        /// inverted room mesh this is the wall you SEE, not the near back-face the ray meets
        /// first.
        /// </summary>
        private static bool TryPickVisibleSurface(Ray ray, out Vector3 point, out Vector3 inwardNormal)
        {
            point = default;
            inwardNormal = default;
            RaycastHit[] hits = Physics.RaycastAll(ray, 1000f);
            if (hits.Length == 0)
            {
                return false;
            }
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (Vector3.Dot(hit.normal, -ray.direction) > 0f)
                {
                    point = hit.point;
                    inwardNormal = hit.normal;
                    return true;
                }
            }
            return false;
        }
    }
}
