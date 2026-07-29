using System;
using UnityEngine;

namespace ProjectionSpatialKit
{
    [Serializable]
    public struct DetectedMarker
    {
        public string id;
        public string materialId;
        public Vector2 centerUV;
        public Vector2[] cornersUV;
        public float rotationDegrees;
        public float confidence;
        public double timestamp;

        public DetectedMarker(string id, Vector2 centerUV, Vector2[] cornersUV, float confidence, double timestamp)
            : this(id, id, centerUV, cornersUV, 0f, confidence, timestamp)
        {
        }

        public DetectedMarker(string id, string materialId, Vector2 centerUV, Vector2[] cornersUV, float rotationDegrees, float confidence, double timestamp)
        {
            this.id = id;
            this.materialId = materialId;
            this.centerUV = centerUV;
            this.cornersUV = cornersUV;
            this.rotationDegrees = rotationDegrees;
            this.confidence = confidence;
            this.timestamp = timestamp;
        }

        public Rect BoundsUV
        {
            get
            {
                if (cornersUV == null || cornersUV.Length == 0)
                {
                    return new Rect(centerUV.x, centerUV.y, 0f, 0f);
                }

                Vector2 min = cornersUV[0];
                Vector2 max = cornersUV[0];
                for (int i = 1; i < cornersUV.Length; i++)
                {
                    min = Vector2.Min(min, cornersUV[i]);
                    max = Vector2.Max(max, cornersUV[i]);
                }

                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }
    }
}
