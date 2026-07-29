using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// An output device whose visible image can be mapped back to content UV: a projected
    /// image on a wall (virtual projector) or a monitor panel. Lets pointer providers ask
    /// "which content pixel is under this world point?" without knowing the device kind.
    /// </summary>
    public interface IContentUVSurface
    {
        /// <summary>
        /// Maps a world point on the device's visible image back to the content canvas UV.
        /// Returns false when the point is not on the image.
        /// </summary>
        bool TryWorldToContentUV(Vector3 worldPoint, out Vector2 contentUV);

        /// <summary>
        /// Whether the device's image is ACTUALLY visible at the world point — the
        /// projector beam really lands there (within throw, not occluded), or the point is
        /// on the monitor panel. Pointer providers require this in addition to the UV
        /// mapping, so venue geometry that merely lies inside a projection cone does not
        /// become touchable.
        /// </summary>
        bool IsImagePresentAt(Vector3 worldPoint);

        /// <summary>Logical output channel = Unity display index the shown content targets.</summary>
        int ContentDisplayIndex { get; }
    }
}
