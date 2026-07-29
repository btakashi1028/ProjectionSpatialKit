using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §5.4 "Ideal" input for daily content development: the operator left-clicks the
    /// projected image (or a monitor panel) in the observer view, and the hit is mapped back
    /// to content UV by whichever <see cref="IContentUVSurface"/> (projector / monitor)
    /// covers that world point. Produces a single touch point while the button is held.
    /// </summary>
    public sealed class ObserverPointerTouchProvider : MonoBehaviour, ITouchPointProvider
    {
        [SerializeField] private Camera observerCamera;
        [SerializeField] private float maxRayDistance = 100f;
        [Tooltip("Output surfaces that can map a world point to content UV. Leave empty to " +
                 "auto-discover every IContentUVSurface in the scene.")]
        [SerializeField] private List<MonoBehaviour> surfaceBehaviours = new List<MonoBehaviour>();
        [Tooltip("Show a marker in the observer view at the exact world point currently being " +
                 "detected as a touch, so you can see where a touch lands vs where you clicked.")]
        [SerializeField] private bool showDebugMarker = true;

        public string LastStatus { get; private set; } = "idle";

        /// <summary>
        /// World position of the last successful surface hit (the point the current touch
        /// physically sits on). Lets a URG rig check its scan coverage against the click.
        /// </summary>
        public Vector3 LastWorldHit { get; private set; }

        /// <summary>Observer camera whose pointer is mapped (settable by SpatialKitSimulator).</summary>
        public Camera ObserverCamera
        {
            get => observerCamera;
            set => observerCamera = value;
        }

        /// <summary>Restrict mapping to these surfaces (each must implement IContentUVSurface).</summary>
        public void SetSurfaces(System.Collections.Generic.IEnumerable<MonoBehaviour> behaviours)
        {
            surfaceBehaviours.Clear();
            surfaceBehaviours.AddRange(behaviours);
#if ENABLE_INPUT_SYSTEM
            ResolveSurfaces();
#endif
        }

        private readonly List<SurfaceTouchPoint> points = new List<SurfaceTouchPoint>();
        private readonly List<IContentUVSurface> surfaces = new List<IContentUVSurface>();
        private int lastRefreshFrame = -1;
        private SurfaceTouchPoint lastPoint;
        private Transform debugMarker;

#if ENABLE_INPUT_SYSTEM
        private Mouse physicalMouse;

        private void OnEnable()
        {
            // Capture the operator's mouse before any virtual device can steal .current.
            physicalMouse = Mouse.current;
            ResolveSurfaces();
        }

        public IReadOnlyList<SurfaceTouchPoint> GetTouchPoints()
        {
            // Refresh lazily so the hub (which runs earlier in the frame) still gets
            // this frame's pointer state, not last frame's.
            if (Time.frameCount != lastRefreshFrame)
            {
                lastRefreshFrame = Time.frameCount;
                Refresh();
            }
            return points;
        }

        private void Refresh()
        {
            points.Clear();
            bool produced = TryProduceTouch();
            // Show the debug marker at the exact detected world point (or hide it when no
            // touch). Lets the operator SEE where a touch actually lands versus where they
            // clicked, so any surprising detection is immediately visible.
            UpdateDebugMarker(produced);
        }

        private bool TryProduceTouch()
        {
            if (observerCamera == null)
            {
                return false;
            }
            if (physicalMouse == null || !physicalMouse.added)
            {
                physicalMouse = Mouse.current;
                if (physicalMouse == null)
                {
                    return false;
                }
            }
            if (!physicalMouse.leftButton.isPressed)
            {
                return false;
            }

            // Only produce a touch while the pointer is actually on an image. A press that
            // starts outside never begins one, and a drag that slides off the projected
            // image LIFTS (like a finger leaving a touch surface / a point leaving the URG
            // scan area) rather than sticking to the last edge point. The touchable region
            // is thus exactly the visible image, with no phantom hold outside it.
            if (!TryGetContentUVUnderPointer(out Vector2 uv, out int displayIndex))
            {
                return false;
            }

            lastPoint = new SurfaceTouchPoint
            {
                id = 0,
                uv = uv,
                confidence = 1f,
                timestamp = Time.unscaledTimeAsDouble,
                displayIndex = displayIndex
            };
            LastStatus = $"uv={uv:F3} display={displayIndex}";
            points.Add(lastPoint);
            return true;
        }

        private void UpdateDebugMarker(bool visible)
        {
            if (!showDebugMarker)
            {
                if (debugMarker != null)
                {
                    debugMarker.gameObject.SetActive(false);
                }
                return;
            }
            if (debugMarker == null)
            {
                debugMarker = CreateDebugMarker();
            }
            debugMarker.gameObject.SetActive(visible);
            if (visible)
            {
                debugMarker.position = LastWorldHit;
            }
        }

        private Transform CreateDebugMarker()
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "SpatialKit Touch Debug Marker";
            marker.hideFlags = HideFlags.DontSave;
            Object.Destroy(marker.GetComponent<Collider>());
            marker.transform.localScale = Vector3.one * 0.06f;
            // Render on a layer the observer camera shows (its first culled-in layer).
            if (observerCamera != null)
            {
                marker.layer = FirstLayerInMask(observerCamera.cullingMask);
            }
            MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                Material material = new Material(unlit) { hideFlags = HideFlags.DontSave };
                material.SetColor("_BaseColor", new Color(1f, 0.25f, 0.1f));
                renderer.material = material;
            }
            return marker.transform;
        }

        private static int FirstLayerInMask(int mask)
        {
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    return i;
                }
            }
            return 0;
        }

        private void OnDisable()
        {
            if (debugMarker != null)
            {
                Object.Destroy(debugMarker.gameObject);
                debugMarker = null;
            }
        }

        private bool TryGetContentUVUnderPointer(out Vector2 contentUV, out int displayIndex)
        {
            contentUV = default;
            displayIndex = 0;
            Vector2 pointerPosition = physicalMouse.position.ReadValue();
            Ray ray = observerCamera.ScreenPointToRay(pointerPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
            {
                return false;
            }

            if (surfaces.Count == 0)
            {
                ResolveSurfaces();
            }
            foreach (IContentUVSurface surface in surfaces)
            {
                // Strict touchable area = the visible image: the UV must be inside the
                // content rectangle AND the image must actually be present at the point
                // (projector beam lands there / point is on the panel). Geometry that
                // merely lies inside a projection cone never becomes touchable.
                if (surface.TryWorldToContentUV(hit.point, out Vector2 uv)
                    && uv.x >= 0f && uv.x <= 1f
                    && uv.y >= 0f && uv.y <= 1f
                    && surface.IsImagePresentAt(hit.point))
                {
                    contentUV = uv;
                    displayIndex = surface.ContentDisplayIndex;
                    LastWorldHit = hit.point;
                    return true;
                }
            }
            return false;
        }

        private void ResolveSurfaces()
        {
            surfaces.Clear();
            if (surfaceBehaviours.Count > 0)
            {
                foreach (MonoBehaviour behaviour in surfaceBehaviours)
                {
                    if (behaviour is IContentUVSurface surface)
                    {
                        surfaces.Add(surface);
                    }
                }
                return;
            }
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.InstanceID))
            {
                if (behaviour is IContentUVSurface surface)
                {
                    surfaces.Add(surface);
                }
            }
        }
#else
        public IReadOnlyList<SurfaceTouchPoint> GetTouchPoints() => points;
#endif
    }
}
