using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Displays the raw captured canvas on a mesh (a "monitor" in the venue).
    /// Serves as the capture-path ground truth independent of the projector light.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class CanvasMonitorDisplay : MonoBehaviour
    {
        [SerializeField] private ContentScreenCaptureSource captureSource;
        [Tooltip("The cube face that points at the viewer maps the texture rotated 180 degrees. " +
                 "Correct it in the material UV here so the transform stays clean (no rotZ=180 needed).")]
        [SerializeField] private bool correctCubeFaceOrientation = true;

        private MeshRenderer meshRenderer;
        private Material runtimeMaterial;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            captureSource = captureSource != null ? captureSource : FindFirstObjectByType<ContentScreenCaptureSource>();
        }

        private void Start()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            runtimeMaterial = new Material(shader) { name = "SpatialKit Monitor (runtime)" };
            if (correctCubeFaceOrientation)
            {
                // Flip both U and V (= 180 degree rotation) to cancel the cube-face mapping.
                runtimeMaterial.SetTextureScale("_BaseMap", new Vector2(-1f, -1f));
                runtimeMaterial.SetTextureOffset("_BaseMap", new Vector2(1f, 1f));
            }
            meshRenderer.material = runtimeMaterial;
        }

        private void Update()
        {
            if (runtimeMaterial == null || captureSource == null)
            {
                return;
            }

            if (captureSource.TryGetFrameTexture(out Texture canvas, out _) && canvas != null)
            {
                runtimeMaterial.SetTexture("_BaseMap", canvas);
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
