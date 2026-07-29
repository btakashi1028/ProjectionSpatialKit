using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// A projector + URG treated as one "projection set". Aims both at a chosen surface,
    /// driven by a point on that surface and its inward normal (into the room), so it works
    /// for any wall orientation — axis-aligned or not, slanted partitions, floor and ceiling
    /// alike (curved surfaces are approximated by the flat plane at the clicked point; URG
    /// touch on true curves is out of scope). The Editor lets you click the inside of the
    /// room model to call <see cref="AimAtSurface"/>; nothing here assumes compass directions.
    ///
    /// The projector sits off the surface along its normal at a throw distance derived from
    /// the projector's throw ratio and the desired image width, facing the surface. The URG
    /// sits on the surface plane just below the image, its scan plane coincident with the
    /// surface so touches on it are detected; the runtime auto-calibration maps that scan
    /// plane to the projected image every frame.
    /// </summary>
    [ExecuteAlways]
    public sealed class ProjectionRig : MonoBehaviour
    {
        [SerializeField] private VirtualProjectorLight projector;
        [SerializeField] private UrgRig urg;
        [Tooltip("Desired projected image width on the surface, metres. Drives the projector " +
                 "throw distance (= width × throw ratio) and the URG offset below the image.")]
        [SerializeField] private float targetImageWidth = 2.0f;
        [Tooltip("Gap between the URG scan plane and the surface, metres (physical mounting " +
                 "clearance; stays within the URG scan-plane thickness).")]
        [SerializeField] private float urgSurfaceClearance = 0.05f;

        public VirtualProjectorLight Projector => projector;
        public UrgRig Urg => urg;

        /// <summary>Image width this set is aimed to produce, metres (the preflight target).</summary>
        public float TargetImageWidth => targetImageWidth;

        private void OnEnable()
        {
            AutoFind();
        }

        private void AutoFind()
        {
            if (projector == null)
            {
                projector = GetComponentInChildren<VirtualProjectorLight>(true);
            }
            if (urg == null)
            {
                urg = GetComponentInChildren<UrgRig>(true);
            }
        }

        /// <summary>
        /// Aims the projector and URG at a surface. <paramref name="surfacePoint"/> becomes the
        /// centre of the projected image; <paramref name="inwardNormal"/> points from the
        /// surface into the room (the direction the projector sits off the wall).
        /// </summary>
        public void AimAtSurface(Vector3 surfacePoint, Vector3 inwardNormal)
        {
            AutoFind();
            inwardNormal = inwardNormal.normalized;

            // "Up" along the surface: world-up projected onto the surface plane. On a
            // horizontal surface (floor/ceiling) that degenerates, so fall back to world-forward.
            Vector3 up = Vector3.up - inwardNormal * Vector3.Dot(Vector3.up, inwardNormal);
            if (up.sqrMagnitude < 1e-4f)
            {
                up = Vector3.forward - inwardNormal * Vector3.Dot(Vector3.forward, inwardNormal);
            }
            up = up.normalized;

            float aspect = projector != null ? Mathf.Max(0.1f, projector.ImageAspect) : 16f / 9f;
            float throwRatio = projector != null ? Mathf.Max(0.1f, projector.EffectiveThrowRatio) : 1.5f;
            float imageHeight = targetImageWidth / aspect;
            float throwDistance = targetImageWidth * throwRatio;

            if (projector != null)
            {
                projector.transform.position = surfacePoint + inwardNormal * throwDistance;
                projector.transform.rotation = Quaternion.LookRotation(-inwardNormal, up);
            }
            if (urg != null)
            {
                // Just below the image, on the surface plane (scan-plane normal = surface
                // normal), facing "up" the surface so its fan sweeps across the image.
                urg.transform.position = surfacePoint
                    - up * (imageHeight * 0.5f + 0.1f)
                    + inwardNormal * urgSurfaceClearance;
                urg.transform.rotation = Quaternion.LookRotation(up, inwardNormal);
            }
        }
    }
}
