using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §5.3 Tier 1 receiver adapter (reference implementation). Drop ONE of these
    /// into a content scene to consume the kit's touch points directly — with confidence
    /// and lost/held semantics that the Tier 0 touch events cannot carry. The content
    /// then depends on the kit assembly (the allowed dependency direction, §10).
    /// </summary>
    public sealed class TouchPointReceiver : MonoBehaviour
    {
        /// <summary>Raised the first frame a point id appears.</summary>
        public event Action<SurfaceTouchPoint> PointBegan;
        /// <summary>Raised every subsequent frame the point persists.</summary>
        public event Action<SurfaceTouchPoint> PointHeld;
        /// <summary>Raised (with the last known state) the frame a point disappears.</summary>
        public event Action<SurfaceTouchPoint> PointLost;

        private readonly Dictionary<int, SurfaceTouchPoint> previous = new Dictionary<int, SurfaceTouchPoint>();
        private readonly List<int> lostIds = new List<int>();

        /// <summary>All current touch points (frame-latest).</summary>
        public IReadOnlyList<SurfaceTouchPoint> Points => TouchPointBus.Points;

        private void Update()
        {
            IReadOnlyList<SurfaceTouchPoint> current = TouchPointBus.Points;

            lostIds.Clear();
            lostIds.AddRange(previous.Keys);

            for (int i = 0; i < current.Count; i++)
            {
                SurfaceTouchPoint point = current[i];
                if (previous.ContainsKey(point.id))
                {
                    lostIds.Remove(point.id);
                    PointHeld?.Invoke(point);
                }
                else
                {
                    PointBegan?.Invoke(point);
                }
                previous[point.id] = point;
            }

            foreach (int id in lostIds)
            {
                PointLost?.Invoke(previous[id]);
                previous.Remove(id);
            }
        }
    }
}
