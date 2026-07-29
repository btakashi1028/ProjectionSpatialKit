using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Catalogue base for any image-output device model (projector, LCD monitor, ...):
    /// the immutable, model-specific specs reusable across venues. Install state
    /// (pose / surface), correction and environment approximation deliberately live
    /// elsewhere (per-venue placement profile, a later topic).
    /// </summary>
    public abstract class DisplayDeviceProfile : ScriptableObject
    {
        [Header("Model")]
        public string modelName = "Generic Device";
        [Tooltip("Free notes: housing, supported features, etc. (catalogue metadata)")]
        [TextArea] public string notes;

        [Header("Panel")]
        public Vector2Int resolution = new Vector2Int(1920, 1080);

        /// <summary>Native panel aspect (width/height).</summary>
        public float ImageAspect => resolution.y > 0 ? (float)resolution.x / resolution.y : 16f / 9f;
    }
}
