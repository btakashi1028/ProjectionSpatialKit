using System.Collections.Generic;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §5.2: a source of touch points on the logical content canvas. Implemented by
    /// the ideal pointer provider (observer click), the URG simulator, and later by real
    /// hardware bridges. Swapping the provider swaps the input origin without touching
    /// the routing or the content.
    /// </summary>
    public interface ITouchPointProvider
    {
        /// <summary>Current touch points, valid for this frame. May be empty, never null.</summary>
        IReadOnlyList<SurfaceTouchPoint> GetTouchPoints();
    }
}
