namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §5.2: one detected touch point on the logical content canvas, in canvas UV
    /// (0..1, origin bottom-left, matching the content camera's viewport). This is the
    /// kit-internal contract between detection sources (URG sim / real URG / mouse /
    /// touch panel) and the routing that injects them into the content (Tier 0) or
    /// hands them to a receiver adapter (Tier 1).
    /// </summary>
    public struct SurfaceTouchPoint
    {
        /// <summary>Stable id while the same physical touch persists across frames.</summary>
        public int id;

        /// <summary>Position on the logical content canvas (0..1 per axis).</summary>
        public UnityEngine.Vector2 uv;

        /// <summary>Detection confidence 0..1 (URG cluster quality etc.; 1 for ideal input).</summary>
        public float confidence;

        /// <summary>Unscaled game time of the reading, seconds.</summary>
        public double timestamp;

        /// <summary>
        /// Logical output channel the touch belongs to = Unity display index the touched
        /// content targets (0 = Display 1). Lets the router convert UV to the right
        /// camera's pixels when the venue shows more than one content display.
        /// </summary>
        public int displayIndex;
    }
}
