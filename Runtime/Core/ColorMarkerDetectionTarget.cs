using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    [Serializable]
    public sealed class ColorMarkerDetectionTarget
    {
        [SerializeField] private string markerId = "green_sticky";
        [SerializeField] private string materialId = "rubber";
        [SerializeField] private Color debugColor = Color.green;
        [SerializeField] private List<Color> sampleColors = new List<Color> { Color.green };
        [SerializeField, Range(1f, 90f)] private float hueToleranceDegrees = 18f;
        [SerializeField, Range(0f, 1f)] private float saturationTolerance = 0.34f;
        [SerializeField, Range(0f, 1f)] private float valueTolerance = 0.38f;
        [SerializeField, Range(0f, 1f)] private float minimumSaturation = 0.22f;
        [SerializeField, Range(0f, 1f)] private float minimumValue = 0.18f;

        public string MarkerId => markerId;
        public string MaterialId => string.IsNullOrWhiteSpace(materialId) ? markerId : materialId;
        public Color DebugColor => debugColor;
        public IReadOnlyList<Color> SampleColors => sampleColors;
        public float HueToleranceDegrees => hueToleranceDegrees;
        public float SaturationTolerance => saturationTolerance;
        public float ValueTolerance => valueTolerance;
        public float MinimumSaturation => minimumSaturation;
        public float MinimumValue => minimumValue;
    }
}
