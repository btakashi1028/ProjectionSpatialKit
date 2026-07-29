using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Displays a HardwareCameraRig's simulated feed on a mesh (a preview monitor in
    /// the venue). Shows what the physical camera would capture of the room.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class CameraFeedMonitor : MonoBehaviour
    {
        [SerializeField] private HardwareCameraRig cameraRig;
        [Tooltip("The cube face that points at the viewer maps the texture rotated 180 degrees. " +
                 "Correct it in the material UV here so the transform stays clean (no rotZ=180 needed).")]
        [SerializeField] private bool correctCubeFaceOrientation = true;

        private MeshRenderer meshRenderer;
        private Material runtimeMaterial;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            cameraRig = cameraRig != null ? cameraRig : FindFirstObjectByType<HardwareCameraRig>();
        }

        private void Start()
        {
            runtimeMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
            {
                name = "SpatialKit Camera Feed (runtime)"
            };
            if (correctCubeFaceOrientation)
            {
                runtimeMaterial.SetTextureScale("_BaseMap", new Vector2(-1f, -1f));
                runtimeMaterial.SetTextureOffset("_BaseMap", new Vector2(1f, 1f));
            }
            meshRenderer.material = runtimeMaterial;
        }

        private void Update()
        {
            if (runtimeMaterial != null && cameraRig != null && cameraRig.Feed != null)
            {
                runtimeMaterial.SetTexture("_BaseMap", cameraRig.Feed);
            }
        }

        private void OnDestroy()
        {
            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
            }
        }
    }
}
