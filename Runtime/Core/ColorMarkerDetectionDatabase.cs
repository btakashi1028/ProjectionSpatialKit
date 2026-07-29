using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    [CreateAssetMenu(menuName = "Projection Spatial Kit/Color Marker Detection Database")]
    public sealed class ColorMarkerDetectionDatabase : ScriptableObject
    {
        [SerializeField] private List<ColorMarkerDetectionTarget> targets = new List<ColorMarkerDetectionTarget>();
        [SerializeField] private int minimumAreaPixels = 900;
        [SerializeField] private int maximumAreaPixels = 220000;
        [SerializeField, Range(0.05f, 1f)] private float minimumFillRatio = 0.45f;
        [SerializeField, Range(0.1f, 1f)] private float minimumSquareRatio = 0.62f;
        [SerializeField] private int morphologyKernelSize = 7;
        [SerializeField] private int morphologyIterations = 1;

        public IReadOnlyList<ColorMarkerDetectionTarget> Targets => targets;
        public int MinimumAreaPixels => Mathf.Max(1, minimumAreaPixels);
        public int MaximumAreaPixels => Mathf.Max(MinimumAreaPixels, maximumAreaPixels);
        public float MinimumFillRatio => Mathf.Clamp01(minimumFillRatio);
        public float MinimumSquareRatio => Mathf.Clamp01(minimumSquareRatio);
        public int MorphologyKernelSize => Mathf.Max(1, morphologyKernelSize | 1);
        public int MorphologyIterations => Mathf.Max(0, morphologyIterations);
    }
}
