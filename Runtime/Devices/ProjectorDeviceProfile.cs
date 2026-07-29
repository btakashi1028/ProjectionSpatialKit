using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Device catalogue for a projector model — the immutable, model-specific specs that
    /// are reusable across venues (categories 1 "panel" and 2 "lens"). Install state
    /// (pose / surface), correction (keystone / quick-corner) and environment approximation
    /// are deliberately NOT here; they belong to a per-venue placement profile (a later topic).
    ///
    /// Bundle one asset per real model and swap them to see how the projected image size and
    /// lens-shift range change.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ProjectorDeviceProfile",
        menuName = "Projection Spatial Kit/Projector Device Profile")]
    public sealed class ProjectorDeviceProfile : DisplayDeviceProfile
    {
        [Header("Brightness (category 1)")]
        [Tooltip("Rated brightness in lumens (reference for the projected intensity).")]
        public float brightnessLumens = 3000f;

        [Header("Lens (category 2)")]
        [Tooltip("Throw ratio = throw distance / image width. min=max for a fixed lens; a " +
                 "range models the zoom lens. Smaller = wider image (short throw).")]
        public float throwRatioMin = 1.2f;
        public float throwRatioMax = 1.6f;
        [Tooltip("Maximum lens shift as a fraction of the image, per axis (0..1).")]
        [Range(0f, 1f)] public float lensShiftMaxHorizontal = 0.3f;
        [Range(0f, 1f)] public float lensShiftMaxVertical = 0.6f;

        /// <summary>Throw ratio at a zoom position (0 = widest/min, 1 = tightest/max).</summary>
        public float ThrowRatioAt(float zoom01)
        {
            return Mathf.Lerp(throwRatioMin, throwRatioMax, Mathf.Clamp01(zoom01));
        }
    }
}
