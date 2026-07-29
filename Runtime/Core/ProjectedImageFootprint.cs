using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Where a projector's image actually LANDS, traced against scene colliders.
    ///
    /// This is the measurement the preflight checks are built on: instead of assuming the
    /// image is a rectangle on a known wall, the four image corners are cast into the scene
    /// and we report what they hit. A projector knows only its pose and intrinsics, so the
    /// footprint — size, distance, incidence, whether it spills onto another surface — is a
    /// RESULT, not a setting.
    /// </summary>
    public struct ProjectedImageFootprint
    {
        /// <summary>Where the beam axis (image centre) meets geometry.</summary>
        public Vector3 Centre;

        /// <summary>Surface normal at <see cref="Centre"/>.</summary>
        public Vector3 SurfaceNormal;

        /// <summary>Collider the image centre landed on (the "projection surface").</summary>
        public Collider SurfaceCollider;

        /// <summary>Distance from the lens to <see cref="Centre"/>, metres.</summary>
        public float ThrowDistance;

        /// <summary>Image corners as they land, in metres (bottom-left, bottom-right, top-right, top-left).</summary>
        public Vector3 BottomLeft;
        public Vector3 BottomRight;
        public Vector3 TopRight;
        public Vector3 TopLeft;

        /// <summary>True when all four corner rays reached geometry within the throw distance.</summary>
        public bool AllCornersHit;

        /// <summary>How many of the four corners landed on the SAME collider as the centre (4 = fits one surface).</summary>
        public int CornersOnSurface;

        /// <summary>Landed image width in metres (mean of the bottom and top edges).</summary>
        public float Width;

        /// <summary>Landed image height in metres (mean of the left and right edges).</summary>
        public float Height;

        /// <summary>
        /// Angle between the beam axis and the surface normal, degrees. 0 = dead-on;
        /// large values mean keystone distortion and uneven focus across the image.
        /// </summary>
        public float IncidenceDegrees;

        /// <summary>Landed aspect (width / height), useful to spot severe trapezoid distortion.</summary>
        public float Aspect => Height > 0.0001f ? Width / Height : 0f;
    }
}
