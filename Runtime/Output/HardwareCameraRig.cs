using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §3 ObserverCameraRig / §12 spike: the physical capture camera (e.g. the
    /// QR-reading camera). Like the projector, it is hardware defined only by its
    /// pose and intrinsics (vertical FOV + sensor aspect). It captures whatever its
    /// frustum sees — including the projected wall — and has no knowledge of where
    /// any wall is. Moving it changes what it sees; it never re-aims at a surface.
    ///
    /// Renders its view to a RenderTexture (<see cref="Feed"/>) so a monitor can show
    /// the simulated camera feed.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class HardwareCameraRig : MonoBehaviour
    {
        [Header("Intrinsics (hardware spec)")]
        [Tooltip("Vertical field of view in degrees.")]
        [SerializeField] private float verticalFovDegrees = 50f;
        [Tooltip("Sensor aspect (width/height).")]
        [SerializeField] private float sensorAspect = 16f / 9f;
        [SerializeField] private Vector2Int feedResolution = new Vector2Int(1280, 720);

        private Camera rigCamera;
        private RenderTexture feed;

        public RenderTexture Feed => feed;
        public float VerticalFovDegrees => verticalFovDegrees;
        public float HorizontalFovDegrees =>
            2f * Mathf.Atan(Mathf.Tan(verticalFovDegrees * Mathf.Deg2Rad * 0.5f) * sensorAspect) * Mathf.Rad2Deg;

        private void Awake()
        {
            rigCamera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            if (feed == null)
            {
                feed = new RenderTexture(feedResolution.x, feedResolution.y, 24, RenderTextureFormat.ARGB32)
                {
                    name = "SpatialKit Camera Feed RT"
                };
                feed.Create();
            }
            ConfigureCamera();
        }

        private void OnDisable()
        {
            if (rigCamera != null && rigCamera.targetTexture == feed)
            {
                rigCamera.targetTexture = null;
            }
            if (feed != null)
            {
                feed.Release();
                Destroy(feed);
                feed = null;
            }
        }

        private void ConfigureCamera()
        {
            if (rigCamera == null)
            {
                return;
            }
            rigCamera.usePhysicalProperties = false;
            rigCamera.fieldOfView = verticalFovDegrees; // Unity fieldOfView is vertical
            rigCamera.aspect = sensorAspect;
            rigCamera.targetTexture = feed;
        }

        private void LateUpdate()
        {
            if (rigCamera == null)
            {
                return;
            }
            rigCamera.fieldOfView = verticalFovDegrees;
            rigCamera.aspect = sensorAspect;
            if (rigCamera.targetTexture != feed)
            {
                rigCamera.targetTexture = feed;
            }
        }

        // No custom frustum gizmo: the rig drives the Camera's fieldOfView, so Unity's
        // built-in camera frustum (shown when the camera is selected) already visualises
        // the view volume. Drawing our own as well just cluttered the Scene view.
    }
}
