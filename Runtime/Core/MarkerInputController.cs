using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    public sealed class MarkerInputController : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour markerProviderBehaviour;
        [SerializeField] private float lostRetentionSeconds = 0.75f;

        private readonly Dictionary<string, RetainedMarker> retainedMarkers = new Dictionary<string, RetainedMarker>();
        private readonly List<DetectedMarker> currentMarkers = new List<DetectedMarker>();
        private readonly List<IMarkerProvider> markerProviders = new List<IMarkerProvider>();
        private IMarkerProvider markerProvider;
        private bool warnedInvalidProvider;

        public IReadOnlyList<DetectedMarker> CurrentMarkers => currentMarkers;
        public int RetainedCount => retainedMarkers.Count;

        private void Awake()
        {
            ResolveProvider();
        }

        private void Update()
        {
            Refresh();
        }

        public void SetProvider(MonoBehaviour providerBehaviour)
        {
            markerProviderBehaviour = providerBehaviour;
            ResolveProvider();
        }

        public void Refresh()
        {
            ResolveProvider();
            if (markerProviders.Count == 0)
            {
                currentMarkers.Clear();
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            for (int providerIndex = 0; providerIndex < markerProviders.Count; providerIndex++)
            {
                IReadOnlyList<DetectedMarker> detected = markerProviders[providerIndex].GetMarkers();
                for (int i = 0; i < detected.Count; i++)
                {
                    DetectedMarker marker = detected[i];
                    if (string.IsNullOrWhiteSpace(marker.id))
                    {
                        continue;
                    }

                    retainedMarkers[marker.id] = new RetainedMarker(marker, now, false);
                }
            }

            currentMarkers.Clear();
            List<string> expiredIds = null;
            foreach (KeyValuePair<string, RetainedMarker> pair in retainedMarkers)
            {
                RetainedMarker retained = pair.Value;
                double age = now - retained.lastSeenTime;
                if (age > lostRetentionSeconds)
                {
                    expiredIds ??= new List<string>();
                    expiredIds.Add(pair.Key);
                    continue;
                }

                DetectedMarker marker = retained.marker;
                if (age > 0.001d)
                {
                    marker.confidence = Mathf.Clamp01(1f - (float)(age / Mathf.Max(0.001f, lostRetentionSeconds)));
                }

                currentMarkers.Add(marker);
            }

            if (expiredIds == null)
            {
                return;
            }

            for (int i = 0; i < expiredIds.Count; i++)
            {
                retainedMarkers.Remove(expiredIds[i]);
            }
        }

        public void ForgetMarker(string markerId)
        {
            if (string.IsNullOrWhiteSpace(markerId))
            {
                return;
            }

            retainedMarkers.Remove(markerId);
            for (int i = currentMarkers.Count - 1; i >= 0; i--)
            {
                if (currentMarkers[i].id == markerId)
                {
                    currentMarkers.RemoveAt(i);
                }
            }
        }

        private void ResolveProvider()
        {
            markerProviders.Clear();
            if (markerProviderBehaviour == null)
            {
                MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IMarkerProvider)
                    {
                        markerProviderBehaviour = behaviours[i];
                        break;
                    }
                }
            }

            markerProvider = markerProviderBehaviour as IMarkerProvider;
            if (markerProvider != null)
            {
                markerProviders.Add(markerProvider);
            }

            if (markerProvider == null && markerProviderBehaviour != null && !warnedInvalidProvider)
            {
                warnedInvalidProvider = true;
                Debug.LogWarning($"{nameof(MarkerInputController)} provider does not implement {nameof(IMarkerProvider)}.", this);
            }

            MonoBehaviour[] siblingBehaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < siblingBehaviours.Length; i++)
            {
                if (siblingBehaviours[i] is IMarkerProvider siblingProvider && !markerProviders.Contains(siblingProvider))
                {
                    markerProviders.Add(siblingProvider);
                }
            }
        }

        private readonly struct RetainedMarker
        {
            public readonly DetectedMarker marker;
            public readonly double lastSeenTime;

            public RetainedMarker(DetectedMarker marker, double lastSeenTime, bool _)
            {
                this.marker = marker;
                this.lastSeenTime = lastSeenTime;
            }
        }
    }
}
