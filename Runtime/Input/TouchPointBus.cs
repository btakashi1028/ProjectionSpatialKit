using System.Collections.Generic;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Frame-latest touch points published by the <see cref="TouchInjectionHub"/>, for
    /// Tier 1 receivers (<see cref="TouchPointReceiver"/>) that want the full contract
    /// (confidence, lost/held semantics) instead of the collapsed touch events.
    /// </summary>
    public static class TouchPointBus
    {
        private static readonly List<SurfaceTouchPoint> points = new List<SurfaceTouchPoint>();

        public static IReadOnlyList<SurfaceTouchPoint> Points => points;

        internal static void Publish(IReadOnlyList<SurfaceTouchPoint> current)
        {
            points.Clear();
            for (int i = 0; i < current.Count; i++)
            {
                points.Add(current[i]);
            }
        }
    }
}
