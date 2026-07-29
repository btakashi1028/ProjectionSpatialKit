using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Persistent Scene-view panel for the kit: always available (no selection needed, unlike
    /// an Inspector), so the package's features are operable straight from the Scene view.
    /// Toggle it from the Scene view's Overlays menu (` key) if you ever hide it.
    /// </summary>
    [Overlay(typeof(SceneView), OverlayId, "Projection Spatial Kit", defaultDisplay: true)]
    public sealed class SpatialKitSceneOverlay : Overlay
    {
        private const string OverlayId = "projection-spatial-kit";

        public override VisualElement CreatePanelContent()
        {
            IMGUIContainer content = new IMGUIContainer(DrawContent);
            content.style.minWidth = 268f;
            return content;
        }

        private static bool setupExpanded;
        private static bool advancedSetupExpanded;

        private static void DrawContent()
        {
            ProjectionRig rig = Object.FindFirstObjectByType<ProjectionRig>();
            UrgRig urg = Object.FindFirstObjectByType<UrgRig>();
            SpatialKitSimulator simulator = Object.FindFirstObjectByType<SpatialKitSimulator>();

            DrawSetupSection();

            if (rig == null && simulator == null)
            {
                EditorGUILayout.HelpBox(
                    "このシーンにキットの構成がありません。\n" +
                    "上の「この開いているシーンから会場を作る」から始めてください。",
                    MessageType.Info);
                return;
            }

            DrawOutputsSection(simulator);
            DrawChannelsSection(simulator);
            DrawProjectionSection(rig);
            DrawUrgSection(urg);
            DrawSimulationSection(simulator);
            DrawDisplaySection();
        }

        private static readonly string[] DisplayNames =
        {
            "画面 1", "画面 2", "画面 3", "画面 4", "画面 5", "画面 6", "画面 7", "画面 8"
        };

        // ---- 出力機材: each row is one PLACED device (by its object name); pick which content
        //      screen it shows inline, so the device's identity and its screen never blur together.
        private static void DrawOutputsSection(SpatialKitSimulator simulator)
        {
            GUILayout.Label("出力機材", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "各行 = 置いた機材。右の「映す画面」で、その機材がどのコンテンツ画面を出すか選びます。",
                EditorStyles.wordWrappedMiniLabel);

            UrgRig[] urgs = Object.FindObjectsByType<UrgRig>(FindObjectsSortMode.InstanceID);

            foreach (VirtualProjectorLight projector in
                     Object.FindObjectsByType<VirtualProjectorLight>(FindObjectsSortMode.InstanceID))
            {
                bool sensed = false;
                foreach (UrgRig u in urgs)
                {
                    if (SensesProjector(u, projector))
                    {
                        sensed = true;
                        break;
                    }
                }
                DrawProjectorRow(projector, sensed);
            }

            foreach (MonitorSurface monitor in
                     Object.FindObjectsByType<MonitorSurface>(FindObjectsSortMode.InstanceID))
            {
                DrawMonitorRow(monitor);
            }

            using (new GUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(simulator == null))
                {
                    if (GUILayout.Button("モニタを追加"))
                    {
                        SpatialKitSceneBuilder.AddMonitor();
                    }
                    if (GUILayout.Button("プロジェクタを追加"))
                    {
                        SpatialKitSceneBuilder.AddProjector();
                    }
                }
            }
            EditorGUILayout.LabelField(
                "「映す画面」の解像度など、画面自体の設定は Simulator で行います。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        // The device's own name (Monitor 1 / Projector 2 …) is the identity; the "→ 画面 N" popup
        // is a separate, editable mapping. Keeping both visible stops the object index and the
        // content-display index from being mistaken for each other.
        private static int DrawDeviceHead(Component device, int contentDisplay, out bool displayChanged)
        {
            if (GUILayout.Button(new GUIContent(device.gameObject.name, "この機材を選択"),
                    EditorStyles.miniButton, GUILayout.MinWidth(58f), GUILayout.ExpandWidth(true)))
            {
                Selection.activeGameObject = device.gameObject;
            }
            GUILayout.Label("→", GUILayout.Width(12f));
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(
                Mathf.Clamp(contentDisplay, 0, DisplayNames.Length - 1), DisplayNames, GUILayout.Width(62f));
            displayChanged = EditorGUI.EndChangeCheck();
            return picked;
        }

        private static void DrawProjectorRow(VirtualProjectorLight projector, bool sensed)
        {
            using (new GUILayout.HorizontalScope())
            {
                int picked = DrawDeviceHead(projector, projector.ContentDisplay, out bool changed);
                if (changed)
                {
                    Undo.RecordObject(projector, "Set content display");
                    projector.ContentDisplay = picked;
                    EditorUtility.SetDirty(projector);
                }
                GUILayout.Label(new GUIContent(sensed ? "タッチ:URG" : "表示のみ",
                        sensed ? "この投影面には URG があるのでタッチできます。"
                               : "URG が無いので表示専用です(タッチ不可)。"),
                    EditorStyles.miniLabel, GUILayout.Width(56f));
                if (GUILayout.Button("削除", EditorStyles.miniButton, GUILayout.Width(32f)))
                {
                    // Delete the whole Projection Set when the projector lives in a rig.
                    ProjectionRig rig = projector.GetComponentInParent<ProjectionRig>();
                    GameObject target = rig != null && rig.gameObject != projector.gameObject
                        ? rig.gameObject
                        : projector.gameObject;
                    Undo.DestroyObjectImmediate(target);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // ---- 画面(チャネル): the logical content displays the venue reproduces, and their
        //      resolution. Kept next to 出力機材 so display / resolution / touch are read in one
        //      place instead of split between the overlay and the Simulator inspector.
        private static void DrawChannelsSection(SpatialKitSimulator simulator)
        {
            if (simulator == null)
            {
                return;
            }
            GUILayout.Label("画面 (チャネル)", EditorStyles.boldLabel);

            SerializedObject so = new SerializedObject(simulator);
            SerializedProperty channels = so.FindProperty("channels");
            EditorGUI.BeginChangeCheck();

            int removeAt = -1;
            for (int i = 0; i < channels.arraySize; i++)
            {
                SerializedProperty element = channels.GetArrayElementAtIndex(i);
                SerializedProperty disp = element.FindPropertyRelative("displayIndex");
                SerializedProperty res = element.FindPropertyRelative("resolution");
                using (new GUILayout.HorizontalScope())
                {
                    disp.intValue = EditorGUILayout.Popup(
                        Mathf.Clamp(disp.intValue, 0, DisplayNames.Length - 1), DisplayNames, GUILayout.Width(62f));
                    Vector2Int v = res.vector2IntValue;
                    v.x = Mathf.Max(1, EditorGUILayout.DelayedIntField(v.x, GUILayout.Width(52f)));
                    GUILayout.Label("×", GUILayout.Width(12f));
                    v.y = Mathf.Max(1, EditorGUILayout.DelayedIntField(v.y, GUILayout.Width(52f)));
                    res.vector2IntValue = v;
                    GUILayout.Label(v.x >= v.y ? "横" : "縦", EditorStyles.miniLabel, GUILayout.Width(20f));
                    if (GUILayout.Button("削除", EditorStyles.miniButton, GUILayout.Width(32f)))
                    {
                        removeAt = i;
                    }
                }
            }
            if (removeAt >= 0)
            {
                channels.DeleteArrayElementAtIndex(removeAt);
            }

            if (GUILayout.Button("＋ 画面を追加"))
            {
                int n = channels.arraySize;
                channels.arraySize = n + 1;
                SerializedProperty element = channels.GetArrayElementAtIndex(n);
                element.FindPropertyRelative("displayIndex").intValue = n;
                element.FindPropertyRelative("resolution").vector2IntValue = new Vector2Int(1920, 1080);
            }

            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
            }
            EditorGUILayout.LabelField(
                "解像度は実機ディスプレイ / Game View と一致させてください(縦画面は例: 1080×1920)。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        private static bool SensesProjector(UrgRig urg, VirtualProjectorLight projector)
        {
            if (urg.TargetSurfaceBehaviour == (MonoBehaviour)projector)
            {
                return true;
            }
            // Unassigned URGs are paired with their own Projection Set's projector at Play start.
            ProjectionRig rig = urg.GetComponentInParent<ProjectionRig>();
            return urg.TargetSurfaceBehaviour == null && rig != null && rig.Projector == projector;
        }

        // A monitor is display-only until you enable touch. If a device profile already marks it
        // a touch panel that is locked on; otherwise this is the ONE switch that makes clicks on
        // the monitor register as touches (it toggles Force Touch Enabled).
        // A monitor is display-only until you enable touch. If a device profile already marks it a
        // touch panel that is locked on; otherwise the "タッチ" toggle (Force Touch Enabled) is the
        // one switch that makes clicks on it register as touches.
        private static void DrawMonitorRow(MonitorSurface monitor)
        {
            using (new GUILayout.HorizontalScope())
            {
                int picked = DrawDeviceHead(monitor, monitor.ContentDisplay, out bool changed);
                if (changed)
                {
                    Undo.RecordObject(monitor, "Set content display");
                    monitor.ContentDisplay = picked;
                    EditorUtility.SetDirty(monitor);
                }

                bool lockedByProfile = monitor.IsTouchEnabled && !monitor.ForceTouchEnabled;
                using (new EditorGUI.DisabledScope(lockedByProfile))
                {
                    EditorGUI.BeginChangeCheck();
                    bool touch = EditorGUILayout.ToggleLeft(
                        new GUIContent("タッチ", lockedByProfile
                            ? "この機材のデバイスプロファイルがタッチ対応なので常に有効です。"
                            : "ON にするとこのモニタへのクリックがタッチ入力になります (Force Touch Enabled)。"),
                        monitor.IsTouchEnabled, GUILayout.Width(56f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(monitor, "Toggle monitor touch");
                        monitor.ForceTouchEnabled = touch;
                        EditorUtility.SetDirty(monitor);
                    }
                }

                if (GUILayout.Button("削除", EditorStyles.miniButton, GUILayout.Width(32f)))
                {
                    Undo.DestroyObjectImmediate(monitor.gameObject);
                    GUIUtility.ExitGUI();
                }
            }
        }

        // ---- セットアップ: project compatibility + integrating into the open scene ----
        private static void DrawSetupSection()
        {
            System.Collections.Generic.List<SpatialKitSetup.Check> checks = SpatialKitSetup.Diagnose();
            int problems = 0;
            foreach (SpatialKitSetup.Check c in checks)
            {
                if (!c.Ok)
                {
                    problems++;
                }
            }

            string title = problems == 0 ? "セットアップ ✔ 問題なし" : $"セットアップ ⚠ {problems} 件の要対応";
            setupExpanded = EditorGUILayout.Foldout(setupExpanded || problems > 0, title, true, EditorStyles.foldoutHeader);

            if (setupExpanded || problems > 0)
            {
                foreach (SpatialKitSetup.Check check in checks)
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        GUILayout.Label(check.Ok ? "✔" : (check.Blocking ? "✖" : "⚠"), GUILayout.Width(16f));
                        GUILayout.Label(new GUIContent(check.Label, check.Detail), GUILayout.ExpandWidth(true));
                        using (new EditorGUI.DisabledScope(check.Ok || check.Fix == null))
                        {
                            if (GUILayout.Button("修復", GUILayout.Width(48f)))
                            {
                                check.Fix?.Invoke();
                            }
                        }
                    }
                    if (!check.Ok)
                    {
                        EditorGUILayout.LabelField(check.Detail, EditorStyles.wordWrappedMiniLabel);
                    }
                }

                using (new EditorGUI.DisabledScope(problems == 0))
                {
                    if (GUILayout.Button("すべて修復"))
                    {
                        SpatialKitSetup.RunAll();
                    }
                }

                // Opt-in: keeps the host project's fullscreen renderer features out of the
                // Scene view / venue. Changes the host's default rendering, so never automatic.
                EditorGUI.BeginChangeCheck();
                bool venueDefault = EditorGUILayout.ToggleLeft(
                    new GUIContent("Scene ビューを会場レンダラーで描画 (任意)",
                        "ON にすると URP の既定レンダラーを会場用に変更し、ホストのフルスクリーン描画機能が" +
                        "Scene ビューや会場に載らなくなります。ホストプロジェクト自身の描画も変わります。"),
                    SpatialKitSetup.DefaultRendererIsVenue());
                if (EditorGUI.EndChangeCheck())
                {
                    if (venueDefault)
                    {
                        SpatialKitSetup.SetVenueAsDefaultRenderer();
                    }
                    else
                    {
                        SpatialKitSetup.RestoreHostDefaultRenderer();
                    }
                }
            }

            // Integration entry point. There is ONE recommended path: open your content scene,
            // press this, and the kit builds a SEPARATE venue scene that projects it (the venue
            // can never live in the content scene — it would additively load itself forever).
            using (new EditorGUI.DisabledScope(SpatialKitSceneBuilder.SceneHasVenue()))
            {
                if (GUILayout.Button(
                        new GUIContent("この開いているシーンから会場を作る",
                            "いま開いているシーンを「投影したいコンテンツ」とみなし、それを投影する会場シーンを" +
                            "新規作成します。コンテンツはそのまま、会場は別ファイルになります。"),
                        GUILayout.Height(26f)))
                {
                    SpatialKitSceneBuilder.CreateVenueSceneForCurrentContent();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.LabelField(
                    "→ 開いているシーンを投影対象にして、会場シーンを別途作成します。",
                    EditorStyles.wordWrappedMiniLabel);

                // The other entry point: drop the venue rig into the CURRENT scene without creating
                // a new scene file. Only makes sense when this scene is already empty / a venue.
                advancedSetupExpanded = EditorGUILayout.Foldout(advancedSetupExpanded,
                    "この空シーンに会場だけ追加", true);
                if (advancedSetupExpanded)
                {
                    EditorGUILayout.LabelField(
                        "いま開いているシーンが空(または会場側)のときに、新規シーンを作らず会場をこの場に追加します。" +
                        "投影したいコンテンツシーンは後で Simulator の Content Scene に指定してください。",
                        EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button(
                            new GUIContent("この空シーンに会場だけ追加",
                                "いま開いているシーンが空(または会場側)のときだけ使います。")))
                    {
                        SpatialKitSceneBuilder.AddVenueToCurrentScene();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUILayout.Space(6f);
        }

        // ---- 投影面: click a surface in the Scene view to place projector + URG ----
        private static void DrawProjectionSection(ProjectionRig rig)
        {
            GUILayout.Label("投影面", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(rig == null))
            {
                GUI.backgroundColor = ProjectionAimTool.Active
                    ? new Color(1f, 0.6f, 0.25f)
                    : new Color(0.5f, 0.8f, 1f);
                if (GUILayout.Button(
                        ProjectionAimTool.Active ? "クリック待機中… (Esc で解除)" : "壁をクリックして投影面を設定",
                        GUILayout.Height(26f)))
                {
                    ProjectionAimTool.Active = !ProjectionAimTool.Active;
                }
                GUI.backgroundColor = Color.white;

                if (rig != null)
                {
                    SerializedObject so = new SerializedObject(rig);
                    SerializedProperty width = so.FindProperty("targetImageWidth");
                    EditorGUI.BeginChangeCheck();
                    float value = EditorGUILayout.Slider("画像幅 (m)", width.floatValue, 0.5f, 4f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        width.floatValue = value;
                        so.ApplyModifiedProperties();
                    }
                }
            }
            EditorGUILayout.Space(4f);
        }

        // ---- URG: detection mode (Ideal clicks vs Physical fan raycasts) ----
        private static void DrawUrgSection(UrgRig urg)
        {
            GUILayout.Label("URG (タッチ検出)", EditorStyles.boldLabel);
            if (urg == null)
            {
                EditorGUILayout.LabelField("シーンに URG がありません", EditorStyles.miniLabel);
                EditorGUILayout.Space(4f);
                return;
            }
            SerializedObject so = new SerializedObject(urg);
            SerializedProperty mode = so.FindProperty("mode");
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(mode, new GUIContent("検出モード"));
            if (EditorGUI.EndChangeCheck())
            {
                so.ApplyModifiedProperties();
            }
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("状態", urg.LastStatus, EditorStyles.miniLabel);
            }
            EditorGUILayout.Space(4f);
        }

        // ---- シミュレーション: play / stop and the unattended demo ----
        private static void DrawSimulationSection(SpatialKitSimulator simulator)
        {
            GUILayout.Label("シミュレーション", EditorStyles.boldLabel);
            if (simulator == null)
            {
                EditorGUILayout.LabelField("シーンに Simulator がありません", EditorStyles.miniLabel);
                EditorGUILayout.Space(4f);
                return;
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button(EditorApplication.isPlaying ? "停止" : "再生", GUILayout.Height(22f)))
                {
                    EditorApplication.isPlaying = !EditorApplication.isPlaying;
                }
                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("デモ入力を再生", GUILayout.Height(22f)))
                    {
                        ScriptedDemoTouchProvider demo = Object.FindFirstObjectByType<ScriptedDemoTouchProvider>();
                        if (demo != null)
                        {
                            demo.Play();
                        }
                    }
                }
            }
            if (GUILayout.Button("Simulator を選択 (詳細設定)"))
            {
                Selection.activeGameObject = simulator.gameObject;
            }
            // Preflight: answers "what breaks on site?" by calculation, without needing to
            // preview every display — the part previewing inside the Editor cannot do.
            if (GUILayout.Button(new GUIContent("Preflight 検査 (投影配置・画面構成)",
                    "投影サイズ・スローレシオ・入射角・遮蔽、チャネルと機材の対応などを計算で検査し、" +
                    "対処つきのレポートを出します。")))
            {
                SpatialKitPreflightWindow.Open();
            }
            EditorGUILayout.Space(4f);
        }

        // ---- 表示: hover spec plates on/off ----
        private static void DrawDisplaySection()
        {
            GUILayout.Label("表示", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            bool hover = EditorGUILayout.Toggle("機材スペックをホバー表示", DeviceInfoPlateHover.Enabled);
            if (EditorGUI.EndChangeCheck())
            {
                DeviceInfoPlateHover.Enabled = hover;
                SceneView.RepaintAll();
            }
        }
    }
}
