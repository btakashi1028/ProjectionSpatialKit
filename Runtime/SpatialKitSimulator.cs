using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// THE single component a kit user operates: the simulator façade. All working
    /// settings live HERE, ordered the way you set them up (content → channels →
    /// separation → observer/input). The internal machinery (ContentSceneLoader,
    /// OutputRouter, TouchInjectionHub, pointer providers) is created and wired
    /// automatically at Play start, so in Edit mode the venue scene shows exactly one
    /// settings component plus the placed devices.
    ///
    /// The optional <see cref="SpatialKitSimulationProfile"/> is a PRESET FILE: nothing
    /// reads it automatically. Use the inspector's 新規保存 / 上書き保存 / 読み込み
    /// buttons to move settings between this component and the asset explicitly.
    /// </summary>
    [DefaultExecutionOrder(-200)] // configure before TouchInjectionHub (-100) and content Start
    public sealed class SpatialKitSimulator : MonoBehaviour
    {
        [Header("1. Content")]
        [Tooltip("Scene loaded additively as the unmodified content (project path).")]
        [SerializeField] private string contentScenePath = "";
        [Tooltip("URP renderer index the content camera is routed to (the renderer that " +
                 "carries the content's own features). -1 = leave as-is.")]
        [SerializeField] private int contentCameraRendererIndex = -1;

        [Header("2. Output channels (one per content display)")]
        [Tooltip("Which content displays the venue reproduces. Devices pick a channel by " +
                 "their own Content Display number.")]
        [SerializeField] private List<OutputRouter.ChannelConfig> channels =
            new List<OutputRouter.ChannelConfig>
            {
                new OutputRouter.ChannelConfig { displayIndex = 0, resolution = new Vector2Int(1920, 1080) }
            };

        [Header("3. Venue separation")]
        [Tooltip("Layer the venue objects live on; excluded from the content camera.")]
        [SerializeField] private string venueLayerName = "SpatialKitVenue";

        [Header("4. Observer / input")]
        [Tooltip("Camera whose pointer maps clicks onto surfaces. Auto: the ObserverFlyCamera in the scene.")]
        [SerializeField] private Camera observerCamera;
        [Tooltip("Create a ScriptedDemoTouchProvider at Play start for unattended verification " +
                 "(run it from this component's context menu: Play Scripted Demo).")]
        [SerializeField] private bool includeScriptedDemo = true;

        [Header("5. Preset file (explicit save/load only)")]
        [Tooltip("Settings preset asset. Assigning it changes NOTHING by itself — use the " +
                 "save/load buttons below to copy settings between this component and the asset.")]
        [SerializeField] private SpatialKitSimulationProfile preset;

        private ContentSceneLoader loader;
        private OutputRouter router;
        private TouchInjectionHub hub;
        private ObserverPointerTouchProvider monitorPointer;

        public OutputRouter Router => router;
        public TouchInjectionHub Hub => hub;

        /// <summary>Linked preset asset (save/load target for the inspector buttons).</summary>
        public SpatialKitSimulationProfile Preset
        {
            get => preset;
            set => preset = value;
        }

        /// <summary>Output channels this venue reproduces (read-only view for tooling).</summary>
        public IReadOnlyList<OutputRouter.ChannelConfig> Channels => channels;

        /// <summary>
        /// The pixel resolution configured for a content display, or (0,0) if that display has
        /// no channel. Lets devices know their content's native aspect (e.g. portrait 1080×1920)
        /// even in Edit mode, before any capture source exists.
        /// </summary>
        public Vector2Int GetChannelResolution(int display)
        {
            foreach (OutputRouter.ChannelConfig config in channels)
            {
                if (config.displayIndex == display)
                {
                    return config.resolution;
                }
            }
            return Vector2Int.zero;
        }

        // ------------------------------------------------------------- preset save/load

        /// <summary>Copies this component's working settings INTO a preset asset.</summary>
        public void SaveTo(SpatialKitSimulationProfile target)
        {
            target.contentScenePath = contentScenePath;
            target.contentCameraRendererIndex = contentCameraRendererIndex;
            target.channels = new List<OutputRouter.ChannelConfig>(channels);
            target.venueLayerName = venueLayerName;
        }

        /// <summary>Replaces this component's working settings WITH a preset asset's.</summary>
        public void LoadFrom(SpatialKitSimulationProfile source)
        {
            contentScenePath = source.contentScenePath;
            contentCameraRendererIndex = source.contentCameraRendererIndex;
            channels = new List<OutputRouter.ChannelConfig>(source.channels);
            venueLayerName = source.venueLayerName;
        }

        // ------------------------------------------------------------------ runtime rig

        private void Awake()
        {
            // The machinery is runtime-only: created here so the Edit-mode inspector shows
            // exactly one component. Settings are applied while creating each piece.
            loader = gameObject.AddComponent<ContentSceneLoader>();
            loader.ContentScenePath = contentScenePath;
            loader.ContentCameraRendererIndex = contentCameraRendererIndex;
            loader.VenueLayerToExcludeFromContentCamera = LayerMask.NameToLayer(venueLayerName);

            router = gameObject.AddComponent<OutputRouter>();
            foreach (OutputRouter.ChannelConfig config in channels)
            {
                router.GetOrCreateChannel(config.displayIndex, config.resolution);
            }

            hub = gameObject.AddComponent<TouchInjectionHub>();
            ConfigureInput();
        }

        /// <summary>
        /// Builds the Tier 0 input chain from what is PLACED in the scene. Touch comes from
        /// DETECTORS, never from a projector itself — a projector is only a light, so a
        /// projected image is touchable exactly when a URG is installed on that surface:
        /// - each URG senses the image of the projector it targets (its own Projection Set's
        ///   projector, or the nearest one when unassigned);
        /// - each touch-capable monitor senses itself (no URG involved);
        /// - a projector with NO URG is DISPLAY-ONLY (e.g. a second screen that just shows
        ///   content) and is never touch-interactive;
        /// - a ScriptedDemoTouchProvider is created for unattended verification when enabled.
        /// </summary>
        private void ConfigureInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (observerCamera == null)
            {
                ObserverFlyCamera fly = FindFirstObjectByType<ObserverFlyCamera>();
                observerCamera = fly != null ? fly.GetComponent<Camera>() : Camera.main;
            }

            VirtualProjectorLight[] projectors =
                FindObjectsByType<VirtualProjectorLight>(FindObjectsSortMode.InstanceID);

            // Hand the monitor pointer EVERY monitor, not just the ones touch-enabled right now.
            // MonitorSurface.TryWorldToContentUV already refuses to map a non-touch monitor, so
            // the enable/disable decision is re-evaluated on every click. That means toggling a
            // monitor's タッチ switch takes effect live in Play mode — no need to re-enter Play,
            // which was a silent trap (input was wired once at Awake).
            var allMonitors = new List<MonoBehaviour>();
            foreach (MonitorSurface monitor in FindObjectsByType<MonitorSurface>(FindObjectsSortMode.InstanceID))
            {
                allMonitors.Add(monitor);
            }

            var providers = new List<MonoBehaviour>();

            // One touch source per URG, each mapped to the surface IT senses.
            foreach (UrgRig urg in FindObjectsByType<UrgRig>(FindObjectsSortMode.InstanceID))
            {
                if (urg.TargetSurfaceBehaviour == null)
                {
                    urg.TargetSurfaceBehaviour = ResolveProjectorFor(urg, projectors);
                }
                if (urg.TargetSurfaceBehaviour == null)
                {
                    continue; // nothing for this URG to sense
                }
                if (urg.IdealProvider == null)
                {
                    urg.IdealProvider = CreateProvider(
                        new List<MonoBehaviour> { urg.TargetSurfaceBehaviour });
                }
                providers.Add(urg);
            }

            if (allMonitors.Count > 0)
            {
                monitorPointer = CreateProvider(allMonitors);
                providers.Add(monitorPointer);
            }

            if (includeScriptedDemo)
            {
                providers.Add(gameObject.AddComponent<ScriptedDemoTouchProvider>());
            }

            hub.SetProviders(providers);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary>
        /// The projector a URG senses: the one in its own Projection Set when it is part of a
        /// rig (so several projector+URG sets each stay paired), else the nearest projector.
        /// </summary>
        private static VirtualProjectorLight ResolveProjectorFor(
            UrgRig urg, VirtualProjectorLight[] projectors)
        {
            ProjectionRig rig = urg.GetComponentInParent<ProjectionRig>();
            if (rig != null && rig.Projector != null)
            {
                return rig.Projector;
            }

            VirtualProjectorLight nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (VirtualProjectorLight projector in projectors)
            {
                float distance = Vector3.Distance(urg.transform.position, projector.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = projector;
                    nearestDistance = distance;
                }
            }
            return nearest;
        }

        private ObserverPointerTouchProvider CreateProvider(List<MonoBehaviour> surfaces)
        {
            ObserverPointerTouchProvider provider = gameObject.AddComponent<ObserverPointerTouchProvider>();
            provider.ObserverCamera = observerCamera;
            provider.SetSurfaces(surfaces);
            return provider;
        }
#endif

        [ContextMenu("Play Scripted Demo")]
        private void PlayScriptedDemo()
        {
            ScriptedDemoTouchProvider demo = FindFirstObjectByType<ScriptedDemoTouchProvider>();
            if (demo != null)
            {
                demo.Play();
            }
            else
            {
                Debug.LogWarning("[SpatialKit] no ScriptedDemoTouchProvider (enable Include Scripted Demo and enter Play)", this);
            }
        }
    }
}
