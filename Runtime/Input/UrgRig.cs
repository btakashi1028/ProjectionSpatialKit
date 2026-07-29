using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §5.4 URG (scanning rangefinder) simulator. Lives on the venue rig — it is a
    /// physical device with a pose in the room, typically mounted so its scan plane hugs
    /// the projection wall a few centimetres in front of it.
    ///
    /// Scan geometry: beams fan inside the transform's local X-Z plane (x = right,
    /// z = forward = angle 0), rotating around local Y. Anything with a collider crossing
    /// that plane occludes the beams behind it and becomes a detection — reproducing the
    /// real URG's "front object shadows the back one" and "leaving the plane loses the
    /// point" behaviours.
    ///
    /// Two modes (spec table):
    /// - Ideal: forwards the observer-click provider's points (daily content development).
    /// - Physical: fan raycasts + clustering + the polar→plane→canvas UV converter, using
    ///   an auto 4-point calibration against the target surface (same pipeline a real URG
    ///   would use after a manual 4-corner calibration).
    /// </summary>
    public sealed class UrgRig : MonoBehaviour, ITouchPointProvider
    {
        public enum DetectionMode
        {
            Ideal,
            Physical
        }

        [Tooltip("Ideal = observer clicks (daily content development); Physical = fan raycasts " +
                 "reproducing real URG behaviour (occlusion, lost points).")]
        [SerializeField] private DetectionMode mode = DetectionMode.Ideal;

        [Header("Ideal mode")]
        [Tooltip("Provider whose points are forwarded in Ideal mode (observer clicks).")]
        [SerializeField] private ObserverPointerTouchProvider idealProvider;
        [Tooltip("Ideal-mode clicks must land this close to the scan plane (metres) to count " +
                 "as touches — clicks where the sensor sheet physically is not are rejected, " +
                 "matching the real installation.")]
        [SerializeField] private float scanPlaneThickness = 0.15f;

        [Header("Physical mode — sensor spec")]
        [Tooltip("Total scan fan angle, degrees (e.g. Hokuyo URG ~240°).")]
        [SerializeField, Range(10f, 300f)] private float scanAngleDegrees = 180f;
        [SerializeField, Range(11, 1081)] private int beamCount = 361;
        [SerializeField] private float maxRangeMetres = 6f;
        [SerializeField] private LayerMask detectionMask = ~0;

        [Header("Physical mode — clustering / tracking")]
        [Tooltip("Neighbouring beams whose distances differ less than this join one cluster.")]
        [SerializeField] private float clusterDistanceJump = 0.15f;
        [SerializeField, Min(1)] private int minBeamsPerCluster = 2;
        [Tooltip("A cluster within this plane distance of a previous point keeps its id.")]
        [SerializeField] private float trackingRadius = 0.25f;
        [Tooltip("Beam count that maps to confidence 1.")]
        [SerializeField, Min(1)] private int fullConfidenceBeams = 5;

        [Header("Physical mode — canvas mapping")]
        [Tooltip("The output surface whose image the scan plane covers (usually the wall " +
                 "projector). Must implement IContentUVSurface; drives the auto calibration.")]
        [SerializeField] private MonoBehaviour targetSurfaceBehaviour;

        // Scene-view info-plate display state (driven by the plate's own handles).
        [HideInInspector, SerializeField] private float infoPlateScale = 0.7f;
        [HideInInspector, SerializeField] private bool infoPlateMinimized;

        public string LastStatus { get; private set; } = "idle";
        public DetectionMode Mode { get => mode; set => mode = value; }

        public float ScanAngleDegrees => scanAngleDegrees;
        public int BeamCount => beamCount;
        public float MaxRangeMetres => maxRangeMetres;
        public float InfoPlateScale => infoPlateScale;
        public bool InfoPlateMinimized => infoPlateMinimized;

        /// <summary>Ideal-mode click source (auto-wired by SpatialKitSimulator when unset).</summary>
        public ObserverPointerTouchProvider IdealProvider
        {
            get => idealProvider;
            set => idealProvider = value;
        }

        /// <summary>The output surface the scan covers (auto-wired by SpatialKitSimulator when unset).</summary>
        public MonoBehaviour TargetSurfaceBehaviour
        {
            get => targetSurfaceBehaviour;
            set => targetSurfaceBehaviour = value;
        }

        private readonly UrgPolarToCanvasConverter converter = new UrgPolarToCanvasConverter();
        private readonly List<SurfaceTouchPoint> points = new List<SurfaceTouchPoint>();
        private readonly List<TrackedPoint> tracked = new List<TrackedPoint>();
        private readonly List<Cluster> clusters = new List<Cluster>();
        private float[] beamDistances;
        private int lastRefreshFrame = -1;
        private int nextPointId;

        private sealed class TrackedPoint
        {
            public int id;
            public Vector2 planePoint;
            public bool seenThisFrame;
        }

        private struct Cluster
        {
            public Vector2 planeCentroid;
            public int beams;
        }

        private IContentUVSurface TargetSurface => targetSurfaceBehaviour as IContentUVSurface;

        public IReadOnlyList<SurfaceTouchPoint> GetTouchPoints()
        {
            if (Time.frameCount != lastRefreshFrame)
            {
                lastRefreshFrame = Time.frameCount;
                Refresh();
            }
            return points;
        }

        private void Refresh()
        {
            points.Clear();
            if (mode == DetectionMode.Ideal)
            {
                if (idealProvider == null)
                {
                    LastStatus = "no ideal provider assigned";
                    return;
                }
                // Even in Ideal mode the click must be where the sensor sheet physically
                // is: inside the fan's angle/range and near the scan plane. A click on a
                // spot the infrared never reaches must not become a touch.
                IReadOnlyList<SurfaceTouchPoint> raw = idealProvider.GetTouchPoints();
                for (int i = 0; i < raw.Count; i++)
                {
                    if (IsWorldPointInScanCoverage(idealProvider.LastWorldHit))
                    {
                        points.Add(raw[i]);
                    }
                }
                LastStatus = $"ideal: {points.Count}/{raw.Count} in scan coverage";
                return;
            }

            IContentUVSurface surface = TargetSurface;
            if (surface == null)
            {
                LastStatus = "no target surface (IContentUVSurface) assigned";
                return;
            }
            if (!AutoCalibrate(surface))
            {
                LastStatus = "calibration failed (degenerate homography)";
                return;
            }

            Scan();
            BuildClusters();
            TrackClusters(surface);
            LastStatus = $"physical: {points.Count} point(s), {clusters.Count} cluster(s)";
        }

        /// <summary>
        /// Simulator stand-in for the real 4-corner calibration: sample 4 scan-plane points
        /// and ask the surface (projector cone math) for their true canvas UVs, then solve
        /// the same homography a venue operator's calibration would produce.
        /// </summary>
        private bool AutoCalibrate(IContentUVSurface surface)
        {
            float reach = Mathf.Max(1f, maxRangeMetres * 0.5f);
            Vector2[] planePoints =
            {
                new Vector2(-reach * 0.5f, reach * 0.5f),
                new Vector2(reach * 0.5f, reach * 0.5f),
                new Vector2(reach * 0.5f, reach),
                new Vector2(-reach * 0.5f, reach)
            };
            Vector2[] uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                if (!surface.TryWorldToContentUV(PlaneToWorld(planePoints[i]), out uvs[i]))
                {
                    return false;
                }
            }
            return converter.TryCalibrate(planePoints, uvs);
        }

        private void Scan()
        {
            if (beamDistances == null || beamDistances.Length != beamCount)
            {
                beamDistances = new float[beamCount];
            }
            for (int i = 0; i < beamCount; i++)
            {
                float angle = BeamAngleDegrees(i);
                Vector3 direction = Quaternion.AngleAxis(angle, transform.up) * transform.forward;
                beamDistances[i] = Physics.Raycast(transform.position, direction, out RaycastHit hit, maxRangeMetres, detectionMask)
                    ? hit.distance
                    : float.PositiveInfinity;
            }
        }

        private void BuildClusters()
        {
            clusters.Clear();
            Vector2 sum = Vector2.zero;
            int count = 0;
            float previousDistance = float.PositiveInfinity;

            for (int i = 0; i <= beamCount; i++)
            {
                float distance = i < beamCount ? beamDistances[i] : float.PositiveInfinity;
                bool continues = !float.IsInfinity(distance)
                    && (count == 0 || Mathf.Abs(distance - previousDistance) < clusterDistanceJump);

                if (continues)
                {
                    sum += UrgPolarToCanvasConverter.PolarToPlanePoint(
                        BeamAngleDegrees(i) * Mathf.Deg2Rad, distance);
                    count++;
                    previousDistance = distance;
                    continue;
                }

                if (count >= minBeamsPerCluster)
                {
                    clusters.Add(new Cluster { planeCentroid = sum / count, beams = count });
                }
                sum = Vector2.zero;
                count = 0;
                previousDistance = float.PositiveInfinity;
                if (!float.IsInfinity(distance) && i < beamCount)
                {
                    // this beam starts a new cluster
                    sum = UrgPolarToCanvasConverter.PolarToPlanePoint(
                        BeamAngleDegrees(i) * Mathf.Deg2Rad, distance);
                    count = 1;
                    previousDistance = distance;
                }
            }
        }

        private void TrackClusters(IContentUVSurface surface)
        {
            foreach (TrackedPoint track in tracked)
            {
                track.seenThisFrame = false;
            }

            foreach (Cluster cluster in clusters)
            {
                // Strict area trim (like a real URG's detection-area setting): the scan
                // plane also crosses side walls / fixtures, which show up as permanent
                // clusters just outside or exactly at the image border — only accept
                // detections clearly ON the projected image.
                if (!converter.TryPlaneToCanvasUV(cluster.planeCentroid, out Vector2 uv)
                    || uv.x < 0.01f || uv.x > 0.99f || uv.y < 0.01f || uv.y > 0.99f)
                {
                    continue;
                }

                TrackedPoint best = null;
                float bestDistance = trackingRadius;
                foreach (TrackedPoint track in tracked)
                {
                    if (track.seenThisFrame)
                    {
                        continue;
                    }
                    float d = Vector2.Distance(track.planePoint, cluster.planeCentroid);
                    if (d < bestDistance)
                    {
                        best = track;
                        bestDistance = d;
                    }
                }
                if (best == null)
                {
                    best = new TrackedPoint { id = nextPointId++ };
                    if (nextPointId > 1_000_000)
                    {
                        nextPointId = 0;
                    }
                    tracked.Add(best);
                }
                best.planePoint = cluster.planeCentroid;
                best.seenThisFrame = true;

                points.Add(new SurfaceTouchPoint
                {
                    id = best.id,
                    uv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y)),
                    confidence = Mathf.Clamp01((float)cluster.beams / fullConfidenceBeams),
                    timestamp = Time.unscaledTimeAsDouble,
                    displayIndex = surface.ContentDisplayIndex
                });
            }

            tracked.RemoveAll(track => !track.seenThisFrame);
        }

        /// <summary>
        /// Whether a world point lies inside the sensed sheet: close to the scan plane and
        /// within the fan's angle and range. (Coverage only — occlusion by other objects is
        /// a Physical-mode behaviour.)
        /// </summary>
        public bool IsWorldPointInScanCoverage(Vector3 worldPoint)
        {
            Vector3 delta = worldPoint - transform.position;
            if (Mathf.Abs(Vector3.Dot(transform.up, delta)) > scanPlaneThickness)
            {
                return false; // off the scan plane (the beam sheet is not there)
            }
            float x = Vector3.Dot(transform.right, delta);
            float y = Vector3.Dot(transform.forward, delta);
            float distance = Mathf.Sqrt(x * x + y * y);
            if (distance < 0.01f || distance > maxRangeMetres)
            {
                return false;
            }
            float angleDegrees = Mathf.Atan2(x, y) * Mathf.Rad2Deg;
            return Mathf.Abs(angleDegrees) <= scanAngleDegrees * 0.5f;
        }

        private float BeamAngleDegrees(int beamIndex)
        {
            float step = scanAngleDegrees / Mathf.Max(1, beamCount - 1);
            return -scanAngleDegrees * 0.5f + step * Mathf.Clamp(beamIndex, 0, beamCount - 1);
        }

        private Vector3 PlaneToWorld(Vector2 planePoint)
        {
            return transform.position + transform.right * planePoint.x + transform.forward * planePoint.y;
        }

        private void OnDrawGizmosSelected()
        {
            // Fan outline + a sparse set of beams; hits drawn to their distance.
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.5f);
            int stride = Mathf.Max(1, beamCount / 60);
            for (int i = 0; i < beamCount; i += stride)
            {
                float angle = BeamAngleDegrees(i);
                Vector3 direction = Quaternion.AngleAxis(angle, transform.up) * transform.forward;
                float distance = beamDistances != null && i < beamDistances.Length && !float.IsInfinity(beamDistances[i])
                    ? beamDistances[i]
                    : maxRangeMetres;
                Gizmos.DrawLine(transform.position, transform.position + direction * distance);
            }

            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.95f);
            foreach (TrackedPoint track in tracked)
            {
                Gizmos.DrawSphere(PlaneToWorld(track.planePoint), 0.05f);
            }
        }
    }
}
