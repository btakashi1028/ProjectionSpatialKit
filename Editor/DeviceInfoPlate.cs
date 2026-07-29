using UnityEditor;
using UnityEngine;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Shared Scene-view spec plate for venue devices (projector, monitor, ...). Anchored
    /// at the device (top-left pivot, or bottom-left when the device sits low in the room).
    /// Passive display when the object is not selected; when selected it becomes interactive:
    /// - a minimize/restore button at the pivot corner,
    /// - a resize grip at the corner OPPOSITE the pivot (drag to scale).
    /// State persists on the component via the hidden serialized fields
    /// "infoPlateScale" / "infoPlateMinimized". Editor Handles/GUI only — not a runtime Canvas.
    /// </summary>
    internal static class DeviceInfoPlate
    {
        private const int BaseFontSize = 15; // ~1.5× the previous 10 for readability
        private static Texture2D background;
        private static GUIStyle textStyle;
        private static GUIStyle buttonStyle;
        private static GUIStyle gripStyle;

        private static void EnsureStyles()
        {
            if (background == null)
            {
                background = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                background.SetPixel(0, 0, new Color(0.02f, 0.025f, 0.03f, 0.92f));
                background.Apply();
            }
            if (textStyle == null)
            {
                textStyle = new GUIStyle();
            }
            textStyle.normal.background = background;
            textStyle.normal.textColor = new Color(0.7f, 1f, 0.8f, 0.95f);
            textStyle.fontSize = BaseFontSize;
            textStyle.padding = new RectOffset(7, 7, 6, 6);
            textStyle.richText = true; // the first line uses <b> to bold the model name
            Font mono = SpatialKitPaths.LoadMonoFont();
            if (mono != null)
            {
                textStyle.font = mono;
            }

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = BaseFontSize, padding = new RectOffset(2, 2, 0, 2) };
            }
            buttonStyle.font = textStyle.font;

            if (gripStyle == null)
            {
                gripStyle = new GUIStyle { normal = { background = background } };
            }
        }

        /// <summary>Passive plate (no input), used when the device is not selected.</summary>
        internal static void DrawPassive(Component device, string fullText, string minimizedLabel, float scale, bool minimized)
        {
            EnsureStyles();
            scale = Mathf.Clamp(scale, 0.4f, 2.5f);
            GUIContent content = new GUIContent(minimized ? minimizedLabel : fullText);
            Vector2 size = textStyle.CalcSize(content);
            Vector2 topLeft = ComputeTopLeft(device, size * scale);
            DrawScaledAt(topLeft, scale, () => GUI.Label(new Rect(topLeft, size), content, textStyle));
        }

        /// <summary>Interactive plate (minimize button + resize grip), used from OnSceneGUI.</summary>
        internal static void DrawInteractive(Component device, string fullText, string minimizedLabel, float scale, bool minimized)
        {
            EnsureStyles();
            scale = Mathf.Clamp(scale, 0.4f, 2.5f);

            if (minimized)
            {
                GUIContent mini = new GUIContent(minimizedLabel);
                Vector2 miniSize = textStyle.CalcSize(mini);
                Vector2 miniTopLeft = ComputeTopLeft(device, miniSize * scale);
                DrawScaledAt(miniTopLeft, scale, () =>
                {
                    if (GUI.Button(new Rect(miniTopLeft, miniSize), mini, textStyle))
                    {
                        SetBool(device, "infoPlateMinimized", false);
                    }
                });
                return;
            }

            GUIContent content = new GUIContent(fullText);
            Vector2 size = textStyle.CalcSize(content);
            Vector2 topLeft = ComputeTopLeft(device, size * scale);

            DrawScaledAt(topLeft, scale, () =>
            {
                GUI.Label(new Rect(topLeft, size), content, textStyle);
                // Minimize button at the plate's top-left corner.
                Rect minBtn = new Rect(topLeft.x + 2, topLeft.y + 2, 14, 12);
                if (GUI.Button(minBtn, "_", buttonStyle))
                {
                    SetBool(device, "infoPlateMinimized", true);
                }
            });

            HandleResizeGrip(device, topLeft, size, scale);
        }

        // Screen-space top-left for the plate: anchored at the device, but flipped to the
        // left / up when it would overflow the right / bottom of the Scene view, then clamped
        // so the WHOLE plate stays inside the view (no mid-air cropping near the edges).
        private static Vector2 ComputeTopLeft(Component device, Vector2 scaledSize)
        {
            Vector2 anchor = HandleUtility.WorldToGUIPoint(device.transform.position);
            SceneView sv = SceneView.currentDrawingSceneView;
            float viewW = sv != null ? sv.position.width : 4000f;
            float viewH = sv != null ? sv.position.height : 4000f;
            const float margin = 6f;
            const float toolbar = 22f; // Scene-view toolbar band at the top

            float x = anchor.x;
            if (x + scaledSize.x > viewW - margin)
            {
                x = anchor.x - scaledSize.x; // flip to the left of the device
            }
            float y = anchor.y;
            if (y + scaledSize.y > viewH - margin)
            {
                y = anchor.y - scaledSize.y; // flip above the device
            }
            x = Mathf.Clamp(x, margin, Mathf.Max(margin, viewW - scaledSize.x - margin));
            y = Mathf.Clamp(y, toolbar + margin, Mathf.Max(toolbar + margin, viewH - scaledSize.y - margin));
            return new Vector2(x, y);
        }

        // Runs a draw action inside a GUI.matrix scaled around the given top-left corner.
        private static void DrawScaledAt(Vector2 topLeft, float scale, System.Action draw)
        {
            Matrix4x4 previous = GUI.matrix;
            GUIUtility.ScaleAroundPivot(new Vector2(scale, scale), topLeft);
            draw();
            GUI.matrix = previous;
        }

        private static void HandleResizeGrip(Component device, Vector2 topLeft, Vector2 size, float scale)
        {
            // Bottom-right corner of the scaled plate, in screen coords (outside the matrix).
            Vector2 opposite = new Vector2(topLeft.x + size.x * scale, topLeft.y + size.y * scale);
            Rect grip = new Rect(opposite.x - 6f, opposite.y - 6f, 12f, 12f);
            GUI.Box(grip, GUIContent.none, gripStyle);
            EditorGUIUtility.AddCursorRect(grip, MouseCursor.ResizeUpLeft);

            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (grip.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        float diagonal = size.magnitude;
                        float newScale = diagonal > 0.001f
                            ? Vector2.Distance(e.mousePosition, topLeft) / diagonal
                            : scale;
                        SetFloat(device, "infoPlateScale", Mathf.Clamp(newScale, 0.4f, 2.5f));
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
            }
        }

        private static void SetFloat(Component device, string property, float value)
        {
            SerializedObject so = new SerializedObject(device);
            so.FindProperty(property).floatValue = value;
            so.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }

        private static void SetBool(Component device, string property, bool value)
        {
            SerializedObject so = new SerializedObject(device);
            so.FindProperty(property).boolValue = value;
            so.ApplyModifiedProperties();
            SceneView.RepaintAll();
        }
    }
}
