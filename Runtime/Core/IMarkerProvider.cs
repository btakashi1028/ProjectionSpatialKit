using System.Collections.Generic;

namespace ProjectionSpatialKit
{
    public interface IMarkerProvider
    {
        IReadOnlyList<DetectedMarker> GetMarkers();
    }
}
