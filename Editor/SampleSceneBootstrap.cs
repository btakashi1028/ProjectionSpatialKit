using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ProjectionSpatialKit.Editor
{
    /// <summary>
    /// Builds the kit's SAMPLE scenes (the pair a package consumer would import):
    ///
    /// 1. Sample content (Touch Ripple): a self-contained, kit-unaware touch game with two
    ///    display outputs (Display 1 = play field, Display 2 = scoreboard). It proves the
    ///    Tier 0 promise — the kit drives it unmodified.
    /// 2. Sample venue: room + projector (content Display 1 on the wall) + touch monitor
    ///    (content Display 2 on a panel) + URG simulator + observer camera + HUD, all wired
    ///    through OutputRouter / TouchInjectionHub.
    ///
    /// Spatial separation: the venue sits at y = -100 while the content scene stays at its
    /// own origin (content may spawn absolute-world objects at runtime; the venue is ours to
    /// move). Renderer separation: content cameras keep renderer index 0 (with any content
    /// features); venue cameras use a clean venue renderer (SSAO only).
    /// </summary>
    public static class SampleSceneBootstrap
    {
        private const string ContentScenePath = "Assets/ProjectionSpatialKit/Samples/Scenes/SampleContent_TouchRipple.unity";
        private const string VenueScenePath = "Assets/ProjectionSpatialKit/Samples/Scenes/910_SampleVenue.unity";
        private const string VenueLayerName = SpatialKitSetup.VenueLayerName;
        private const string DeviceProfileFolder = "Assets/ProjectionSpatialKit/Data/DeviceProfiles";

        private static readonly Vector3 VenueOrigin = new Vector3(0f, -100f, 0f);
        private static readonly Vector3 WallCenter = VenueOrigin + new Vector3(0f, 1.5f, 2f);

        private static int venueLayer;
        private static int venueRendererIndex = -1;
        private static Material deviceMaterial;
        private static int VenueMask => 1 << venueLayer;

        // ---------------------------------------------------------------- content scene

        [MenuItem("Projection Spatial Kit/Bootstrap Sample Content Scene (Touch Ripple)")]
        public static void CreateOrUpdateContentScene()
        {
            Directory.CreateDirectory("Assets/ProjectionSpatialKit/Samples/Scenes");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.45f);

            // ---- Display 1: play field ----
            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.targetDisplay = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.06f, 0.10f);

            // Boundary the balls bounce off (the visible 16:9 frame of the ortho camera).
            GameObject field = new GameObject("Play Field Boundary");
            EdgeCollider2D edge = field.AddComponent<EdgeCollider2D>();
            float halfH = 5f;
            float halfW = halfH * (16f / 9f);
            edge.points = new[]
            {
                new Vector2(-halfW, halfH), new Vector2(-halfW, -halfH),
                new Vector2(halfW, -halfH), new Vector2(halfW, halfH),
                new Vector2(-halfW, halfH)
            };

            // ---- Display 2: scoreboard ----
            GameObject scoreCameraGo = new GameObject("Score Camera (Display 2)");
            scoreCameraGo.transform.position = new Vector3(100f, 0f, -10f);
            Camera scoreCamera = scoreCameraGo.AddComponent<Camera>();
            scoreCamera.orthographic = true;
            scoreCamera.orthographicSize = 5f;
            scoreCamera.targetDisplay = 1;
            scoreCamera.clearFlags = CameraClearFlags.SolidColor;
            scoreCamera.backgroundColor = new Color(0.10f, 0.045f, 0.06f);

            GameObject scoreTextGo = new GameObject("Score Text");
            scoreTextGo.transform.position = new Vector3(100f, 0f, 0f);
            TextMesh scoreText = scoreTextGo.AddComponent<TextMesh>();
            scoreText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            scoreTextGo.GetComponent<MeshRenderer>().sharedMaterial = scoreText.font.material;
            scoreText.text = "TOUCH RIPPLES\ntouches  0";
            scoreText.anchor = TextAnchor.MiddleCenter;
            scoreText.alignment = TextAlignment.Left;
            scoreText.characterSize = 0.35f;
            scoreText.fontSize = 32;
            scoreText.color = new Color(1f, 0.9f, 0.85f);

            // ---- controller ----
            GameObject controllerGo = new GameObject("Touch Ripple Controller");
            var controller = controllerGo.AddComponent<Samples.TouchRippleContent.TouchRippleController>();
            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("mainCamera").objectReferenceValue = camera;
            so.FindProperty("scoreText").objectReferenceValue = scoreText;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ContentScenePath);
            AssetDatabase.SaveAssets();
            // Register so the venue can additively load it reliably (cold Editor open + builds).
            SpatialKitSceneBuilder.EnsureSceneInBuildSettings(ContentScenePath);
            Debug.Log("[SpatialKit] sample content scene created: " + ContentScenePath);
        }

        // ------------------------------------------------------------------ venue scene

        [MenuItem("Projection Spatial Kit/Bootstrap Sample Venue Scene")]
        public static void CreateOrUpdateVenueScene()
        {
            EnsureFolders();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ContentScenePath) == null)
            {
                CreateOrUpdateContentScene();
            }
            // Project setup is shared with the Setup panel and is project-agnostic (no
            // hard-coded URP paths, and the host project's default renderer is left alone).
            SpatialKitSetup.EnsureCookieShaderAlwaysIncluded();
            venueLayer = SpatialKitSetup.EnsureVenueLayer();
            venueRendererIndex = SpatialKitSetup.EnsureVenueRenderer();
            EnsureProjectorProfiles();
            EnsureMonitorProfiles();
            Material roomMaterial = SpatialKitMaterials.Room();
            deviceMaterial = SpatialKitMaterials.Device();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.11f, 0.12f, 0.14f);
            RenderSettings.ambientEquatorColor = new Color(0.06f, 0.06f, 0.07f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.035f);

            GameObject venueModel = CreateGroup("Venue Model (会場3Dモデル)");
            GameObject simControl = CreateGroup("Simulator Control (中央管理)");
            GameObject projectionSet = CreateGroup("Projection Set (投影セット / プロジェクタ+URG)");
            GameObject cameraGroup = CreateGroup("Camera (カメラ)");
            GameObject otherHardware = CreateGroup("Other Hardware (その他ハード)");
            GameObject lighting = CreateGroup("Lighting (照明セット)");

            // ---- room ----
            GameObject room = CreateRoom(roomMaterial);
            Reparent(room, venueModel);
            GameObject marker = CreateDimensionMarker(room.GetComponent<RoomBox>());
            Reparent(marker, venueModel);

            // ---- projection set: projector + URG as one unit, click-to-aim on any surface ----
            VirtualProjectorLight projector = CreateProjector(); // contentDisplay 0 (default)
            Reparent(projector.gameObject, projectionSet);
            UrgRig urg = CreateUrgRig(); // Ideal mode; wiring auto-resolved by the simulator
            Reparent(urg.gameObject, projectionSet);
            GameObject occluder = CreateUrgDemoOccluder();
            Reparent(occluder, projectionSet);
            ProjectionRig rig = projectionSet.AddComponent<ProjectionRig>();
            SerializedObject rigSo = new SerializedObject(rig);
            rigSo.FindProperty("projector").objectReferenceValue = projector;
            rigSo.FindProperty("urg").objectReferenceValue = urg;
            rigSo.ApplyModifiedPropertiesWithoutUndo();

            // ---- other hardware ----
            HardwareCameraRig cameraRig = CreateHardwareCamera();
            Reparent(cameraRig.gameObject, cameraGroup);

            MonitorSurface monitor = CreateMonitor(); // contentDisplay 1
            Reparent(monitor.gameObject, otherHardware);

            // ---- observer (Display 3) ----
            Camera observer = CreateObserverCamera();
            Reparent(observer.gameObject, simControl);

            // ---- the simulator façade: THE (only) component the user operates ----
            // Working settings live on the component; the preset asset is linked as an
            // explicit save/load file. Loader / router / hub / providers are created and
            // wired automatically at Play start from the placed devices.
            SpatialKitSimulator simulator = simControl.AddComponent<SpatialKitSimulator>();
            SpatialKitSimulationProfile presetAsset = EnsureSimulationProfile();
            simulator.LoadFrom(presetAsset); // working settings = the sample preset
            simulator.Preset = presetAsset;  // linked for save/load round-trips
            SerializedObject simulatorSo = new SerializedObject(simulator);
            simulatorSo.FindProperty("observerCamera").objectReferenceValue = observer;
            simulatorSo.ApplyModifiedPropertiesWithoutUndo();

            // ---- HUD (resolves the runtime-created machinery lazily) ----
            CreateStatusOverlay(simControl);

            CreateFillLight(lighting);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), VenueScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            // Both scenes in Build Settings: the venue as the played/built scene, the content so
            // the additive load resolves reliably (cold Editor open + builds).
            SpatialKitSceneBuilder.EnsureSceneInBuildSettings(VenueScenePath);
            SpatialKitSceneBuilder.EnsureSceneInBuildSettings(ContentScenePath);
            Debug.Log("[SpatialKit] sample venue scene created: " + VenueScenePath +
                      " (venueLayer=" + venueLayer + ", venueRendererIndex=" + venueRendererIndex + ")");
        }

        // ------------------------------------------------------------------- builders

        private static SpatialKitSimulationProfile EnsureSimulationProfile()
        {
            const string path = "Assets/ProjectionSpatialKit/Data/SampleSimulationProfile.asset";
            SpatialKitSimulationProfile profileAsset = AssetDatabase.LoadAssetAtPath<SpatialKitSimulationProfile>(path);
            if (profileAsset == null)
            {
                profileAsset = ScriptableObject.CreateInstance<SpatialKitSimulationProfile>();
                AssetDatabase.CreateAsset(profileAsset, path);
            }
            profileAsset.contentScenePath = ContentScenePath;
            // -1 = leave the content camera on the project's DEFAULT renderer (keeping the
            // host's own renderer features). Only venue cameras use the venue renderer.
            profileAsset.contentCameraRendererIndex = -1;
            profileAsset.venueLayerName = VenueLayerName;
            profileAsset.channels = new System.Collections.Generic.List<OutputRouter.ChannelConfig>
            {
                new OutputRouter.ChannelConfig { displayIndex = 0, resolution = new Vector2Int(1920, 1080) },
                new OutputRouter.ChannelConfig { displayIndex = 1, resolution = new Vector2Int(1920, 1080) }
            };
            EditorUtility.SetDirty(profileAsset);
            AssetDatabase.SaveAssets();
            // Reload by path so the canonical persisted asset reference serializes into the scene.
            return AssetDatabase.LoadAssetAtPath<SpatialKitSimulationProfile>(path);
        }

        private static GameObject CreateRoom(Material roomMaterial)
        {
            GameObject room = new GameObject("Room (inside-out cube)");
            room.transform.position = VenueOrigin + new Vector3(0f, 1.5f, -0.25f);
            room.transform.localScale = new Vector3(5f, 3f, 4.5f);
            room.AddComponent<MeshFilter>();
            MeshRenderer renderer = room.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = roomMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            room.AddComponent<RoomBox>(); // also adds a MeshCollider on the inverted mesh
            SetLayerRecursive(room, venueLayer);
            room.isStatic = true;
            return room;
        }

        private static GameObject CreateDimensionMarker(RoomBox room)
        {
            GameObject markerGo = new GameObject("Dimension Marker (Star)");
            markerGo.transform.position = VenueOrigin + new Vector3(0f, 1.5f, -0.25f);
            RoomDimensionMarker marker = markerGo.AddComponent<RoomDimensionMarker>();
            SerializedObject markerSo = new SerializedObject(marker);
            markerSo.FindProperty("room").objectReferenceValue = room;
            markerSo.ApplyModifiedPropertiesWithoutUndo();
            SetLayerRecursive(markerGo, venueLayer);
            return markerGo;
        }

        private static VirtualProjectorLight CreateProjector()
        {
            GameObject projectorGo = new GameObject("Projector");
            projectorGo.transform.position = VenueOrigin + new Vector3(0f, 1.5f, -1.0f);
            projectorGo.transform.LookAt(WallCenter);
            Light light = projectorGo.AddComponent<Light>();
            light.cullingMask = VenueMask;
            VirtualProjectorLight projector = projectorGo.AddComponent<VirtualProjectorLight>();

            SerializedObject so = new SerializedObject(projector);
            // contentDisplay stays 0 (the content's Display 1); the channel resolves itself.
            so.FindProperty("deviceProfile").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<ProjectorDeviceProfile>(DeviceProfileFolder + "/Standard 1080p.asset");
            so.FindProperty("intensity").floatValue = 15f;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject body = CreateDeviceBody("Body", new Vector3(0.3f, 0.12f, 0.4f));
            body.transform.SetParent(projectorGo.transform, false);
            SetLayerRecursive(projectorGo, venueLayer);
            return projector;
        }

        private static MonitorSurface CreateMonitor()
        {
            GameObject monitorGo = new GameObject("Scoreboard Monitor");
            // Against the left wall, panel facing the room centre (+x). Display-only, so it
            // is a SIGNAGE (non-touch) panel: the content's Display 2 is a passive scoreboard,
            // and touch on it would only bleed onto Display 1 through the shared virtual
            // Touchscreen (a documented multi-display limitation). Touch panels are still a
            // supported feature — use the "55in Touch Panel" profile for an interactive one.
            monitorGo.transform.position = VenueOrigin + new Vector3(-2.35f, 1.35f, 0.4f);
            monitorGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            MonitorSurface monitor = monitorGo.AddComponent<MonitorSurface>();

            SerializedObject so = new SerializedObject(monitor);
            so.FindProperty("contentDisplay").intValue = 1; // the content's Display 2 (scoreboard)
            so.FindProperty("deviceProfile").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<MonitorDeviceProfile>(DeviceProfileFolder + "/32in Signage.asset");
            so.ApplyModifiedPropertiesWithoutUndo();

            SetLayerRecursive(monitorGo, venueLayer);
            return monitor;
        }

        private static UrgRig CreateUrgRig()
        {
            GameObject urgGo = new GameObject("URG Rig");
            // Wall-bottom mount: scan plane parallel to the projection wall (z ≈ +2.0 inner
            // face), a few cm in front of it. Forward = up along the wall, plane normal = -z.
            urgGo.transform.position = VenueOrigin + new Vector3(0f, 0.08f, 1.92f);
            urgGo.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
            UrgRig urg = urgGo.AddComponent<UrgRig>();

            GameObject body = CreateDeviceBody("Body", new Vector3(0.06f, 0.06f, 0.08f));
            body.transform.SetParent(urgGo.transform, false);
            SetLayerRecursive(urgGo, venueLayer);
            return urg;
        }

        private static GameObject CreateUrgDemoOccluder()
        {
            GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            occluder.name = "URG Demo Occluder (hand)";
            // Crosses the scan plane; the oscillation sweeps it along the wall.
            occluder.transform.position = VenueOrigin + new Vector3(0.4f, 1.4f, 1.92f);
            occluder.transform.localScale = new Vector3(0.08f, 0.12f, 0.08f);
            if (deviceMaterial != null)
            {
                occluder.GetComponent<MeshRenderer>().sharedMaterial = deviceMaterial;
            }
            occluder.AddComponent<UrgDemoOccluder>();
            SetLayerRecursive(occluder, venueLayer);
            occluder.SetActive(false); // opt-in: enable to demo/verify Physical mode
            return occluder;
        }

        private static Camera CreateObserverCamera()
        {
            GameObject cameraGo = new GameObject("Observer Camera");
            cameraGo.transform.position = VenueOrigin + new Vector3(2.2f, 1.7f, -2.8f);
            cameraGo.transform.LookAt(WallCenter);
            Camera camera = cameraGo.AddComponent<Camera>();
            // Display 3: the content itself owns Display 1 (play field) and Display 2
            // (scoreboard), so the venue's observer view moves out of their way.
            camera.targetDisplay = 2;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            camera.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = VenueMask;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.volumeLayerMask = 0;
            AssignVenueRenderer(camera);
            cameraGo.AddComponent<ObserverFlyCamera>();
            return camera;
        }

        private static HardwareCameraRig CreateHardwareCamera()
        {
            GameObject cameraGo = new GameObject("Hardware Camera");
            cameraGo.transform.position = VenueOrigin + new Vector3(-1.2f, 1.3f, -1.0f);
            cameraGo.transform.LookAt(WallCenter);
            Camera cam = cameraGo.AddComponent<Camera>();
            cam.targetDisplay = 7;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;
            cam.backgroundColor = new Color(0.01f, 0.01f, 0.015f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.cullingMask = VenueMask;
            UniversalAdditionalCameraData camData = cam.GetUniversalAdditionalCameraData();
            camData.volumeLayerMask = 0;
            AssignVenueRenderer(cam);
            HardwareCameraRig rig = cameraGo.AddComponent<HardwareCameraRig>();

            GameObject body = CreateDeviceBody("Body", new Vector3(0.12f, 0.12f, 0.2f));
            body.transform.SetParent(cameraGo.transform, false);
            SetLayerRecursive(cameraGo, venueLayer);
            return rig;
        }

        private static GameObject CreateDeviceBody(string name, Vector3 scale)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = name;
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.localScale = scale;
            if (deviceMaterial != null)
            {
                body.GetComponent<MeshRenderer>().sharedMaterial = deviceMaterial;
            }
            return body;
        }

        private static void CreateFillLight(GameObject parent)
        {
            GameObject lightGo = new GameObject("Ceiling Light");
            lightGo.transform.position = VenueOrigin + new Vector3(0f, 2.7f, -0.5f);
            lightGo.isStatic = true;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = 0.6f;
            light.range = 7f;
            light.color = new Color(1f, 0.97f, 0.92f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.6f;
            light.cullingMask = VenueMask;

            GameObject bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb (emissive)";
            Object.DestroyImmediate(bulb.GetComponent<Collider>());
            bulb.transform.SetParent(lightGo.transform, false);
            bulb.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
            MeshRenderer bulbRenderer = bulb.GetComponent<MeshRenderer>();
            bulbRenderer.sharedMaterial = SpatialKitMaterials.Bulb();
            bulbRenderer.shadowCastingMode = ShadowCastingMode.Off;

            SetLayerRecursive(lightGo, venueLayer);
            Reparent(lightGo, parent);
        }

        private static VenueStatusBoard CreateStatusOverlay(GameObject parent)
        {
            // "Pro tool" HUD styled after the Rector reference: monospace, small low-opacity
            // text on a quiet translucent panel with a hairline border.
            GameObject canvasGo = new GameObject("Status Overlay");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 2; // observer display
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);
            Image panel = panelGo.AddComponent<Image>();
            panel.color = new Color(0.02f, 0.025f, 0.03f, 0.55f);
            Outline outline = panelGo.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
            outline.effectDistance = new Vector2(1f, -1f);
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(18f, -18f);
            panelRect.sizeDelta = new Vector2(500f, 138f);

            GameObject textGo = new GameObject("Status Text");
            textGo.transform.SetParent(panelGo.transform, false);
            Text text = textGo.AddComponent<Text>();
            text.font = EnsureMonoFont();
            text.fontSize = 14;
            text.lineSpacing = 1.15f;
            text.color = new Color(1f, 1f, 1f, 0.55f);
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = "status";
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 10f);
            textRect.offsetMax = new Vector2(-12f, -10f);

            VenueStatusBoard board = canvasGo.AddComponent<VenueStatusBoard>();
            SerializedObject boardSo = new SerializedObject(board);
            boardSo.FindProperty("statusText").objectReferenceValue = text;
            boardSo.ApplyModifiedPropertiesWithoutUndo();

            Reparent(canvasGo, parent);
            return board;
        }

        // ------------------------------------------------------------------- profiles

        private static void EnsureProjectorProfiles()
        {
            EnsureProjectorProfile("Standard 1080p", new Vector2Int(1920, 1080), 3500f, 1.2f, 1.6f, 0.3f, 0.6f,
                "Generic standard-throw 1080p projector with a modest zoom.");
            EnsureProjectorProfile("Short Throw 1080p", new Vector2Int(1920, 1080), 3000f, 0.45f, 0.55f, 0.0f, 0.12f,
                "Short-throw, near-fixed lens, minimal lens shift.");
            EnsureProjectorProfile("WUXGA Long Zoom", new Vector2Int(1920, 1200), 5000f, 1.3f, 2.2f, 0.4f, 0.6f,
                "WUXGA installation projector with a wide zoom range and generous lens shift.");
            AssetDatabase.SaveAssets();
        }

        private static void EnsureProjectorProfile(
            string name, Vector2Int resolution, float lumens,
            float throwMin, float throwMax, float shiftH, float shiftV, string notes)
        {
            string path = DeviceProfileFolder + "/" + name + ".asset";
            ProjectorDeviceProfile profile = AssetDatabase.LoadAssetAtPath<ProjectorDeviceProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<ProjectorDeviceProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.modelName = name;
            profile.resolution = resolution;
            profile.brightnessLumens = lumens;
            profile.throwRatioMin = throwMin;
            profile.throwRatioMax = throwMax;
            profile.lensShiftMaxHorizontal = shiftH;
            profile.lensShiftMaxVertical = shiftV;
            profile.notes = notes;
            EditorUtility.SetDirty(profile);
        }

        private static void EnsureMonitorProfiles()
        {
            EnsureMonitorProfile("55in Touch Panel", new Vector2Int(1920, 1080), 55f, 450f, 14f, true,
                "55-inch interactive flat panel with a touch overlay.");
            EnsureMonitorProfile("32in Signage", new Vector2Int(1920, 1080), 32f, 350f, 10f, false,
                "32-inch signage display, no touch.");
            AssetDatabase.SaveAssets();
        }

        private static void EnsureMonitorProfile(
            string name, Vector2Int resolution, float inches, float nits, float bezelMm, bool touch, string notes)
        {
            string path = DeviceProfileFolder + "/" + name + ".asset";
            MonitorDeviceProfile profile = AssetDatabase.LoadAssetAtPath<MonitorDeviceProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<MonitorDeviceProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            profile.modelName = name;
            profile.resolution = resolution;
            profile.diagonalInches = inches;
            profile.brightnessNits = nits;
            profile.bezelMillimeters = bezelMm;
            profile.isTouchPanel = touch;
            profile.notes = notes;
            EditorUtility.SetDirty(profile);
        }

        // -------------------------------------------------------------- infrastructure

        private static GameObject CreateGroup(string name)
        {
            GameObject group = new GameObject(name);
            group.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return group;
        }

        private static void Reparent(GameObject child, GameObject parent)
        {
            child.transform.SetParent(parent.transform, true);
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory("Assets/ProjectionSpatialKit/Samples/Scenes");
            Directory.CreateDirectory(DeviceProfileFolder);
        }

        private static void AssignVenueRenderer(Camera camera)
        {
            if (venueRendererIndex < 0)
            {
                return;
            }
            camera.GetUniversalAdditionalCameraData().SetRenderer(venueRendererIndex);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        private static Font EnsureMonoFont()
        {
            return SpatialKitPaths.LoadMonoFont();
        }
    }
}
