using UnityEngine;

namespace ProjectionSpatialKit
{
    public sealed class IdealMarker : MonoBehaviour
    {
        [SerializeField] private string markerId = "marker";
        [SerializeField] private string materialId = "concrete";
        [SerializeField] private Vector2 centerUV = new Vector2(0.3f, 0.55f);
        [SerializeField] private Vector2 sizeUV = new Vector2(0.12f, 0.12f);
        [SerializeField] private float rotationDegrees;
        [SerializeField] private bool useTransformPosition;
        [SerializeField] private ProjectionPlane2DWorld projectionPlane;

        public string MarkerId => markerId;
        public string MaterialId => materialId;
        public Vector2 CenterUV => useTransformPosition && projectionPlane != null
            ? projectionPlane.WorldToSurfaceUV(transform.position)
            : centerUV;
        public Vector2 SizeUV => sizeUV;

        public DetectedMarker ToDetectedMarker(double timestamp)
        {
            Vector2 center = CenterUV;
            Vector2 half = sizeUV * 0.5f;
            float radians = rotationDegrees * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            Vector2 up = new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
            Vector2[] corners =
            {
                center - right * half.x - up * half.y,
                center + right * half.x - up * half.y,
                center + right * half.x + up * half.y,
                center - right * half.x + up * half.y
            };

            return new DetectedMarker(markerId, materialId, center, corners, rotationDegrees, 1f, timestamp);
        }

        public void Configure(string id, Vector2 uv, Vector2 size, ProjectionPlane2DWorld plane)
        {
            Configure(id, id, uv, size, 0f, plane);
        }

        public void Configure(string id, string material, Vector2 uv, Vector2 size, float rotation, ProjectionPlane2DWorld plane)
        {
            markerId = id;
            materialId = material;
            centerUV = uv;
            sizeUV = size;
            rotationDegrees = rotation;
            projectionPlane = plane;
            useTransformPosition = false;
            ApplyTransform();
        }

        public void SetCenterUV(Vector2 uv)
        {
            centerUV = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            ApplyTransform();
        }

        public void SetPose(Vector2 uv, float rotation)
        {
            centerUV = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
            rotationDegrees = rotation;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (projectionPlane != null)
            {
                transform.position = projectionPlane.SurfaceUVToWorld(centerUV);
            }

            transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        }

        private void OnValidate()
        {
            centerUV = new Vector2(Mathf.Clamp01(centerUV.x), Mathf.Clamp01(centerUV.y));
            sizeUV = new Vector2(Mathf.Clamp(sizeUV.x, 0.01f, 1f), Mathf.Clamp(sizeUV.y, 0.01f, 1f));
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = materialId == "rubber" ? new Color(0.1f, 0.85f, 0.45f, 0.85f) : new Color(0.55f, 0.55f, 0.55f, 0.85f);
            Vector3 center = projectionPlane != null ? projectionPlane.SurfaceUVToWorld(CenterUV) : transform.position;
            Vector2 size = projectionPlane != null ? projectionPlane.SurfaceUVSizeToWorldSize(sizeUV) : sizeUV;
            Gizmos.DrawWireCube(center, new Vector3(size.x, size.y, 0f));
        }
    }
}
