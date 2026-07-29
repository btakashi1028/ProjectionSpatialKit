using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    public sealed class IdealMarkerProvider : MonoBehaviour, IMarkerProvider
    {
        [SerializeField] private List<IdealMarker> markers = new List<IdealMarker>();
        [SerializeField] private bool includeInactive;

        private readonly List<DetectedMarker> detectedMarkers = new List<DetectedMarker>();

        private void Awake()
        {
            RefreshMarkers();
        }

        public IReadOnlyList<DetectedMarker> GetMarkers()
        {
            detectedMarkers.Clear();
            double timestamp = Time.unscaledTimeAsDouble;
            for (int i = 0; i < markers.Count; i++)
            {
                IdealMarker marker = markers[i];
                if (marker == null || (!includeInactive && !marker.isActiveAndEnabled))
                {
                    continue;
                }

                detectedMarkers.Add(marker.ToDetectedMarker(timestamp));
            }

            return detectedMarkers;
        }

        public void RefreshMarkers()
        {
            markers.Clear();
            GetComponentsInChildren(includeInactive, markers);
        }
    }
}
