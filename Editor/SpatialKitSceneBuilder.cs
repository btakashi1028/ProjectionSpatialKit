using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Adds the kit's venue rig to the scene the user ALREADY has open, rather than generating
    /// a whole sample scene. This is the integration path for an existing project: you keep
    /// your scene, and the kit adds a venue (room + projection set + observer + simulator)
    /// beside it, on its own layer and offset far from the origin so it never collides with
    /// your content.
    ///
    /// Everything it creates is additive and removable — it never edits the host's own objects.
    /// </summary>
    internal static class SpatialKitSceneBuilder
    {
        internal const string VenueRootName = "Projection Spatial Kit (Venue)";

        /// <summary>Venue origin: far below the world origin so host content never overlaps.</summary>
        internal static readonly Vector3 VenueOrigin = new Vector3(0f, -100f, 0f);

        /// <summary>
        /// THE onboarding path for an existing project: you have your content scene open, and
        /// this creates a SEPARATE venue scene that projects it. The venue must never live in
        /// the content scene itself — it would additively load its own scene forever.
        /// </summary>
        [MenuItem("Projection Spatial Kit/Create Venue Scene For Current Scene", priority = -1)]
        internal static void CreateVenueSceneForCurrentContent()
        {
            Scene content = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(content.path))
            {
                EditorUtility.DisplayDialog("Projection Spatial Kit",
                    "先に現在のシーン(投影したいコンテンツ)を保存してください。", "OK");
                return;
            }
            if (SceneHasVenue())
            {
                EditorUtility.DisplayDialog("Projection Spatial Kit",
                    "このシーンには既に会場があります。これは会場シーンなので、コンテンツシーンを開いてから実行してください。", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string contentPath = content.path;
            string venuePath = $"{Path.GetDirectoryName(contentPath).Replace('\\', '/')}/" +
                               $"{Path.GetFileNameWithoutExtension(contentPath)}_Venue.unity";

            // Empty scene: the venue brings its own observer camera and lighting, so the host's
            // Main Camera / Directional Light must NOT be carried over.
            Scene venue = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = AddVenueToCurrentScene();
            if (root == null)
            {
                return;
            }

            SpatialKitSimulator simulator = Object.FindFirstObjectByType<SpatialKitSimulator>();
            SerializedObject so = new SerializedObject(simulator);
            so.FindProperty("contentScenePath").stringValue = contentPath;
            so.ApplyModifiedPropertiesWithoutUndo();

            // The content scene is loaded additively at Play — register it in Build Settings so the
            // load is reliable (cold Editor open + builds), not dependent on the editor-only fallback.
            EnsureSceneInBuildSettings(contentPath);
            EnsureSceneInBuildSettings(venuePath);

            EditorSceneManager.SaveScene(venue, venuePath);
            Selection.activeGameObject = simulator.gameObject;
            Debug.Log($"[SpatialKit] 会場シーンを作成しました: {venuePath}\n" +
                      $"  投影するコンテンツ: {contentPath}\n" +
                      "  この会場シーンを Play すると、コンテンツが additive ロードされて投影されます。");
        }

        [MenuItem("Projection Spatial Kit/Add Venue To Current Scene", priority = 0)]
        internal static void AddVenueToCurrentSceneMenu()
        {
            AddVenueToCurrentScene();
        }

        internal static bool SceneHasVenue()
        {
            return Object.FindFirstObjectByType<SpatialKitSimulator>() != null;
        }

        /// <summary>
        /// Builds a minimal, working venue in the open scene: room, projection set
        /// (projector + URG), observer camera and the simulator façade. Returns the root.
        /// </summary>
        internal static GameObject AddVenueToCurrentScene()
        {
            if (SceneHasVenue())
            {
                EditorUtility.DisplayDialog("Projection Spatial Kit",
                    "このシーンには既にキットの構成があります。", "OK");
                return null;
            }

            int venueLayer = SpatialKitSetup.EnsureVenueLayer();
            int venueRenderer = SpatialKitSetup.EnsureVenueRenderer();
            SpatialKitSetup.EnsureCookieShaderAlwaysIncluded();
            if (venueLayer < 0)
            {
                return null;
            }
            int venueMask = 1 << venueLayer;

            GameObject root = new GameObject(VenueRootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Projection Spatial Kit venue");
            root.transform.position = Vector3.zero;

            // ---- room (inside-out cube) ----
            GameObject room = new GameObject("Room (inside-out cube)");
            room.transform.SetParent(root.transform, false);
            room.transform.position = VenueOrigin + new Vector3(0f, 1.5f, -0.25f);
            room.transform.localScale = new Vector3(5f, 3f, 4.5f);
            room.AddComponent<MeshFilter>();
            MeshRenderer roomRenderer = room.AddComponent<MeshRenderer>();
            roomRenderer.sharedMaterial = SpatialKitMaterials.Room();
            roomRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            room.AddComponent<RoomBox>(); // adds the MeshCollider on the inverted mesh
            SetLayerRecursive(room, venueLayer);

            // ---- projection set: projector + URG, aimed at the +Z wall by default ----
            GameObject projectionSet = new GameObject("Projection Set (投影セット / プロジェクタ+URG)");
            projectionSet.transform.SetParent(root.transform, false);

            GameObject projectorGo = new GameObject("Projector");
            projectorGo.transform.SetParent(projectionSet.transform, false);
            Light light = projectorGo.AddComponent<Light>();
            light.cullingMask = venueMask;
            VirtualProjectorLight projector = projectorGo.AddComponent<VirtualProjectorLight>();
            AddBody(projectorGo, new Vector3(0.3f, 0.12f, 0.4f));

            GameObject urgGo = new GameObject("URG Rig");
            urgGo.transform.SetParent(projectionSet.transform, false);
            UrgRig urg = urgGo.AddComponent<UrgRig>();
            AddBody(urgGo, new Vector3(0.06f, 0.06f, 0.08f));

            ProjectionRig rig = projectionSet.AddComponent<ProjectionRig>();
            SerializedObject rigSo = new SerializedObject(rig);
            rigSo.FindProperty("projector").objectReferenceValue = projector;
            rigSo.FindProperty("urg").objectReferenceValue = urg;
            rigSo.ApplyModifiedPropertiesWithoutUndo();
            // Aim at the far (+Z) wall so the venue is usable immediately; the user can
            // re-aim by clicking any surface from the Scene-view overlay.
            rig.AimAtSurface(VenueOrigin + new Vector3(0f, 1.5f, 2f), Vector3.back);
            SetLayerRecursive(projectionSet, venueLayer);

            // ---- observer camera (its own display; the content keeps Display 1) ----
            GameObject observerGo = new GameObject("Observer Camera");
            observerGo.transform.SetParent(root.transform, false);
            observerGo.transform.position = VenueOrigin + new Vector3(2.2f, 1.7f, -2.8f);
            observerGo.transform.LookAt(VenueOrigin + new Vector3(0f, 1.5f, 2f));
            Camera observer = observerGo.AddComponent<Camera>();
            observer.targetDisplay = 2; // Display 3
            observer.nearClipPlane = 0.05f;
            observer.farClipPlane = 100f;
            observer.clearFlags = CameraClearFlags.SolidColor;
            observer.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            observer.cullingMask = venueMask;
            UniversalAdditionalCameraData observerData = observer.GetUniversalAdditionalCameraData();
            observerData.volumeLayerMask = 0; // host content's global Volumes must not tint the venue
            if (venueRenderer >= 0)
            {
                observerData.SetRenderer(venueRenderer);
            }
            observerGo.AddComponent<ObserverFlyCamera>();

            // ---- ceiling light so the venue is not pitch black ----
            GameObject lightGo = new GameObject("Ceiling Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.position = VenueOrigin + new Vector3(0f, 2.7f, -0.5f);
            Light fill = lightGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.intensity = 0.6f;
            fill.range = 7f;
            fill.shadows = LightShadows.Soft;
            fill.cullingMask = venueMask;
            SetLayerRecursive(lightGo, venueLayer);

            // ---- the simulator façade (owns loader / router / input hub at Play start) ----
            GameObject simGo = new GameObject("Simulator Control");
            simGo.transform.SetParent(root.transform, false);
            SpatialKitSimulator simulator = simGo.AddComponent<SpatialKitSimulator>();
            SerializedObject simSo = new SerializedObject(simulator);
            simSo.FindProperty("observerCamera").objectReferenceValue = observer;
            simSo.FindProperty("venueLayerName").stringValue = SpatialKitSetup.VenueLayerName;
            simSo.FindProperty("contentCameraRendererIndex").intValue = -1; // host default renderer
            simSo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = simGo;
            Debug.Log($"[SpatialKit] 現在のシーンに会場を追加しました (layer={venueLayer}, venueRenderer={venueRenderer})。" +
                      " Simulator の Content Scene に投影したいシーンを設定してください。");
            return root;
        }

        /// <summary>
        /// Adds a standalone monitor to the venue. Display-only by default; assign a device
        /// profile whose "Is Touch Panel" is on (or tick Force Touch Enabled) to make it sense
        /// touch. Set Content Display to choose which content screen it shows.
        /// </summary>
        [MenuItem("Projection Spatial Kit/Add Monitor", priority = 20)]
        private static void AddMonitorMenu() => AddMonitor();

        [MenuItem("Projection Spatial Kit/Add Projector (display-only)", priority = 21)]
        private static void AddProjectorMenu() => AddProjector();

        internal static MonitorSurface AddMonitor()
        {
            GameObject root = EnsureVenueRoot(out int venueLayer);
            if (root == null)
            {
                return null;
            }

            int monitorCount = Object.FindObjectsByType<MonitorSurface>(FindObjectsSortMode.None).Length;
            GameObject go = new GameObject($"Monitor {monitorCount + 1}");
            Undo.RegisterCreatedObjectUndo(go, "Add monitor");
            go.transform.SetParent(root.transform, false);
            // Fan successive monitors along the left wall so a second one is not hidden inside the first.
            go.transform.position = VenueOrigin + new Vector3(-2.35f, 1.35f, 0.4f + monitorCount * 1.4f);
            go.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // panel faces the room centre
            MonitorSurface monitor = go.AddComponent<MonitorSurface>();
            SpatialKitSceneBuilder.SetLayerRecursive(go, venueLayer);

            Selection.activeGameObject = go;
            Debug.Log("[SpatialKit] モニタを追加しました(初期状態は表示専用)。" +
                      "タッチさせるには Scene オーバーレイ『出力機材』でこのモニタ行の「タッチ」を ON にするか、" +
                      "タッチ対応の Monitor Device Profile を割り当ててください。映す画面は同じ行の「→ 画面 N」で選べます。");
            return monitor;
        }

        /// <summary>
        /// Adds a DISPLAY-ONLY projector (no URG): it projects a content screen onto whatever
        /// its frustum hits, and is not touch-interactive. Add a URG aimed at its image only if
        /// that projection should sense touch.
        /// </summary>
        internal static VirtualProjectorLight AddProjector()
        {
            GameObject root = EnsureVenueRoot(out int venueLayer);
            if (root == null)
            {
                return null;
            }
            int venueMask = 1 << venueLayer;

            int projectorCount = Object.FindObjectsByType<VirtualProjectorLight>(FindObjectsSortMode.None).Length;
            GameObject go = new GameObject($"Projector {projectorCount + 1} (display-only)");
            Undo.RegisterCreatedObjectUndo(go, "Add projector");
            go.transform.SetParent(root.transform, false);
            go.transform.position = VenueOrigin + new Vector3(projectorCount * 0.6f, 1.5f, -1f);
            go.transform.rotation = Quaternion.identity;
            Light light = go.AddComponent<Light>();
            light.cullingMask = venueMask;
            VirtualProjectorLight projector = go.AddComponent<VirtualProjectorLight>();
            AddBody(go, new Vector3(0.3f, 0.12f, 0.4f));
            SetLayerRecursive(go, venueLayer);

            Selection.activeGameObject = go;
            Debug.Log("[SpatialKit] 表示専用のプロジェクタを追加しました(URG なし = タッチ非対応)。" +
                      "Content Display で映す画面を設定してください。");
            return projector;
        }

        private static GameObject EnsureVenueRoot(out int venueLayer)
        {
            venueLayer = SpatialKitSetup.EnsureVenueLayer();
            if (venueLayer < 0)
            {
                return null;
            }
            GameObject root = GameObject.Find(VenueRootName);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Projection Spatial Kit",
                    "先に「このシーンに会場を追加」を実行してください。", "OK");
            }
            return root;
        }

        private static void AddBody(GameObject parent, Vector3 scale)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(parent.transform, false);
            body.transform.localScale = scale;
            body.GetComponent<MeshRenderer>().sharedMaterial = SpatialKitMaterials.Device();
        }

        internal static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Registers a scene in the project's Build Settings (enabled) if it is not already
        /// there. The venue loads its content scene ADDITIVELY, and additive loading only works
        /// reliably — in the Editor on a cold first open, and at all in a build — when the scene
        /// is in Build Settings so <see cref="UnityEngine.SceneManagement.SceneManager.LoadSceneAsync"/>
        /// can find it. Without this the loader falls back to an editor-only path that can leave
        /// the projection blank until the content scene has been opened once.
        /// </summary>
        internal static void EnsureSceneInBuildSettings(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return;
            }
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene s in scenes)
            {
                if (s.path == scenePath)
                {
                    if (!s.enabled)
                    {
                        s.enabled = true;
                        EditorBuildSettings.scenes = scenes.ToArray();
                    }
                    return;
                }
            }
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[SpatialKit] Build Settings に追加しました: {scenePath}\n" +
                      "(会場はコンテンツを additive ロードするため、確実な読み込みには Build Settings 登録が必要です)");
        }
    }
}
