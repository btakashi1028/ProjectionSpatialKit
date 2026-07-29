using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §5 Tier 0 routing: gathers <see cref="SurfaceTouchPoint"/>s from every
    /// registered <see cref="ITouchPointProvider"/> and injects them into the unmodified
    /// content as Input System state events:
    /// - ALL points go to a virtual Touchscreen (multi-touch, e.g. URG),
    /// - the primary point is mirrored to a virtual Mouse so content that only reads
    ///   Mouse.current still reacts.
    /// The virtual mouse forces itself to be Mouse.current after each injection and this
    /// component runs before content scripts (DefaultExecutionOrder), so the content reads
    /// the injected pointer rather than alternating with the operator's physical mouse.
    /// Points are re-published with hub-wide unique ids to <see cref="TouchPointBus"/> for
    /// Tier 1 receivers.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TouchInjectionHub : MonoBehaviour
    {
        [Tooltip("Touch point providers, in priority order. Each entry must implement " +
                 "ITouchPointProvider. Leave empty to auto-discover providers in the scene.")]
        [SerializeField] private List<MonoBehaviour> providerBehaviours = new List<MonoBehaviour>();
        [Tooltip("Mirror the primary (first) touch to a virtual Mouse for Mouse.current-reading content.")]
        [SerializeField] private bool mirrorPrimaryToMouse = true;

        public string LastStatus { get; private set; } = "no injection yet";
        public int ActiveTouchCount { get; private set; }

#if ENABLE_INPUT_SYSTEM
        private sealed class ActiveTouch
        {
            public int touchId;
            public Vector2 pixel;
            public int displayIndex;   // Unity display this touch targets (0 = Display 1)
            public int slot = -1;      // virtual Touchscreen touch slot while injected
            public bool injected;      // Began was sent (pixel conversion succeeded at least once)
            public bool seenThisFrame;
        }

        private readonly List<ITouchPointProvider> providers = new List<ITouchPointProvider>();
        private readonly Dictionary<long, ActiveTouch> activeTouches = new Dictionary<long, ActiveTouch>();
        private readonly List<long> endedKeys = new List<long>();
        private readonly List<SurfaceTouchPoint> framePoints = new List<SurfaceTouchPoint>();
        private Touchscreen virtualTouchscreen;
        private Mouse virtualMouse;
        private int nextTouchId = 1;
        private Vector2 lastMousePixel;
        private int lastMouseDisplay;   // keep the display on the release frame (see MirrorPrimaryToMouse)

        private void OnEnable()
        {
            ResolveProviders();
            // NOTE: the kit deliberately does NOT touch InputSystem.settings. An earlier version
            // forced editorInputBehaviorInPlayMode = AllDeviceInputAlwaysGoesToGameView so queued
            // input events would be delivered while the Game View was unfocused. That (a) mutates
            // the HOST project's Input System settings asset, and (b) makes the Game View grab all
            // device input, which yanks the Editor's focus back to Display 1 when you click.
            // Injection now writes device state directly (InputState.Change) from inside the player
            // loop, so it never depends on the editor's event routing and the hack is unnecessary.
        }

        private void OnDisable()
        {
            if (virtualTouchscreen != null)
            {
                InputSystem.RemoveDevice(virtualTouchscreen);
                virtualTouchscreen = null;
            }
            if (virtualMouse != null)
            {
                InputSystem.RemoveDevice(virtualMouse);
                virtualMouse = null;
            }
            activeTouches.Clear();
            TouchPointBus.Publish(System.Array.Empty<SurfaceTouchPoint>());
        }

        /// <summary>Explicit provider set (used by SpatialKitSimulator's auto-wiring).</summary>
        public void SetProviders(System.Collections.Generic.IEnumerable<MonoBehaviour> behaviours)
        {
            providerBehaviours.Clear();
            providerBehaviours.AddRange(behaviours);
            ResolveProviders();
        }

        /// <summary>Re-resolve providers (call after spawning providers at runtime).</summary>
        public void ResolveProviders()
        {
            providers.Clear();
            if (providerBehaviours.Count > 0)
            {
                foreach (MonoBehaviour behaviour in providerBehaviours)
                {
                    if (behaviour is ITouchPointProvider provider)
                    {
                        providers.Add(provider);
                    }
                    else if (behaviour != null)
                    {
                        Debug.LogWarning($"[SpatialKit] {behaviour.name} does not implement ITouchPointProvider", behaviour);
                    }
                }
                return;
            }
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.InstanceID))
            {
                if (behaviour is ITouchPointProvider provider)
                {
                    providers.Add(provider);
                }
            }
        }

        private void Update()
        {
            framePoints.Clear();
            for (int p = 0; p < providers.Count; p++)
            {
                IReadOnlyList<SurfaceTouchPoint> points = providers[p].GetTouchPoints();
                for (int i = 0; i < points.Count; i++)
                {
                    Process(ProviderKey(p, points[i].id), points[i]);
                }
            }

            EndMissingTouches();
            MirrorPrimaryToMouse();
            ActiveTouchCount = activeTouches.Count;
            TouchPointBus.Publish(framePoints);
        }

        private static long ProviderKey(int providerIndex, int pointId)
        {
            return ((long)providerIndex << 32) | (uint)pointId;
        }

        private void Process(long key, SurfaceTouchPoint point)
        {
            if (!activeTouches.TryGetValue(key, out ActiveTouch touch))
            {
                touch = new ActiveTouch { touchId = nextTouchId++ };
                if (nextTouchId > 100000)
                {
                    nextTouchId = 1;
                }
                activeTouches.Add(key, touch);
            }
            touch.seenThisFrame = true;
            touch.displayIndex = point.displayIndex;

            // Publish with the hub-wide unique id (provider-local ids may collide between providers).
            point.id = touch.touchId;
            framePoints.Add(point);

            if (!TryGetContentPixel(point, out Vector2 pixel))
            {
                return; // keep the touch alive; inject once a content camera exists
            }
            touch.pixel = pixel;

            if (touch.injected)
            {
                InjectTouch(touch, pixel, UnityEngine.InputSystem.TouchPhase.Moved);
            }
            else
            {
                touch.injected = true;
                InjectTouch(touch, pixel, UnityEngine.InputSystem.TouchPhase.Began);
                LastStatus = $"began touch {touch.touchId} uv={point.uv:F3} px={pixel:F0} display={point.displayIndex}";
            }
        }

        private void EndMissingTouches()
        {
            endedKeys.Clear();
            foreach (KeyValuePair<long, ActiveTouch> pair in activeTouches)
            {
                if (pair.Value.seenThisFrame)
                {
                    pair.Value.seenThisFrame = false;
                    continue;
                }
                if (pair.Value.injected)
                {
                    InjectTouch(pair.Value, pair.Value.pixel, UnityEngine.InputSystem.TouchPhase.Ended);
                    LastStatus = $"ended touch {pair.Value.touchId} px={pair.Value.pixel:F0}";
                }
                endedKeys.Add(pair.Key);
            }
            foreach (long key in endedKeys)
            {
                activeTouches.Remove(key);
            }
        }

        private bool TryGetContentPixel(SurfaceTouchPoint point, out Vector2 pixel)
        {
            pixel = default;

            // Convert content UV to pixels using the CHANNEL resolution (the logical display
            // size the content renders at, e.g. 1920×1080), NOT the content camera's pixelWidth/
            // pixelHeight. In the Editor the camera reports the Game View's current pixel size,
            // which need not match the channel — so basing coordinates on it made the injected
            // touch land at the wrong spot when the Game View aspect/size differed from the
            // channel. The content interprets pointer coordinates in its display's own space,
            // which IS the channel resolution.
            Vector2Int resolution = Vector2Int.zero;
            ContentScreenCaptureSource channel = OutputRouter.FindChannel(point.displayIndex);
            if (channel != null)
            {
                resolution = channel.CanvasResolution;
            }
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                // No channel configured for this display: fall back to the content camera's
                // pixel size so a channel-less setup still injects something usable.
                Camera contentCamera = ContentCameraResolver.Find(point.displayIndex, gameObject.scene);
                if (contentCamera == null)
                {
                    LastStatus = $"no channel/content camera for display {point.displayIndex}";
                    return false;
                }
                resolution = new Vector2Int(contentCamera.pixelWidth, contentCamera.pixelHeight);
            }

            pixel = new Vector2(
                Mathf.Clamp01(point.uv.x) * resolution.x,
                Mathf.Clamp01(point.uv.y) * resolution.y);
            return true;
        }

        // Direct state writes (InputState.Change) instead of QueueStateEvent: queued events
        // are processed by whichever input update runs next, and in the Editor that can be
        // the EDITOR update (Game View unfocused / unattended runs), which silently swallows
        // them (§12.4). A direct change from inside the player loop lands in the play-mode
        // state buffers immediately and deterministically.
        private readonly bool[] slotUsed = new bool[10];

        private void InjectTouch(ActiveTouch touch, Vector2 pixel, UnityEngine.InputSystem.TouchPhase phase)
        {
            virtualTouchscreen ??= InputSystem.AddDevice<Touchscreen>("SpatialKitVirtualTouch");

            if (touch.slot < 0)
            {
                touch.slot = System.Array.IndexOf(slotUsed, false);
                if (touch.slot < 0)
                {
                    return; // more concurrent touches than the Touchscreen has slots
                }
                slotUsed[touch.slot] = true;
            }

            InputState.Change(virtualTouchscreen.touches[touch.slot], new TouchState
            {
                touchId = touch.touchId,
                phase = phase,
                position = pixel,
                // Route to the display this touch belongs to. Without it every touch defaults to
                // display 0 (Display 1), so a Display 2 monitor's touches were delivered to the
                // Display 1 content.
                displayIndex = (byte)touch.displayIndex
            });
            virtualTouchscreen.MakeCurrent();

            if (phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                slotUsed[touch.slot] = false;
                touch.slot = -1;
            }
        }

        private void MirrorPrimaryToMouse()
        {
            if (!mirrorPrimaryToMouse)
            {
                return;
            }

            ActiveTouch primary = null;
            foreach (ActiveTouch touch in activeTouches.Values)
            {
                if (touch.injected && (primary == null || touch.touchId < primary.touchId))
                {
                    primary = touch;
                }
            }

            bool pressed = primary != null;

            // Keep the display number from the press through to the RELEASE frame. On the frame
            // the touch lifts, `primary` is null, so without this the mouse's displayIndex would
            // snap back to 0 (Display 1) exactly when the button-up is sent — and the content on
            // Display 2 would never see the click COMPLETE (press on display 2, release on
            // display 1 = no click). Hold the last display until the next press.
            int display = primary != null ? primary.displayIndex : lastMouseDisplay;

            virtualMouse ??= InputSystem.AddDevice<Mouse>("SpatialKitVirtualMouse");
            MouseState state = new MouseState
            {
                position = primary != null ? primary.pixel : lastMousePixel,
                delta = Vector2.zero,
                displayIndex = (ushort)display
            };
            state = state.WithButton(MouseButton.Left, pressed);
            InputState.Change(virtualMouse, state); // direct write, same reason as InjectTouch
            // Keep the virtual mouse as Mouse.current EVERY frame — not only while a touch is
            // active. Content that reads Mouse.current must never see the operator's REAL
            // mouse: otherwise a real click OFF the projected image (e.g. on a side wall) would
            // reach the content directly, bypassing the projection gating and spawning input at
            // the raw cursor position (which the content camera then renders mirrored on the
            // projection). With the virtual mouse always current and released when idle, only
            // gated touches reach the content.
            virtualMouse.MakeCurrent();
            lastMousePixel = state.position;
            lastMouseDisplay = display;
        }
#endif
    }
}
