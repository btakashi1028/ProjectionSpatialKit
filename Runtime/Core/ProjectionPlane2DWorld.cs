using UnityEngine;

namespace ProjectionSpatialKit
{
    public sealed class ProjectionPlane2DWorld : MonoBehaviour, IProjectionSurfaceMapper
    {
        [SerializeField] private Vector2 worldSizeMeters = new Vector2(4f, 3f);
        [SerializeField] private Vector2 worldOrigin = Vector2.zero;

        public Vector2 WorldSizeMeters => worldSizeMeters;
        public Vector2 WorldOrigin => worldOrigin;

        public Vector2 WorldToSurfaceUV(Vector3 worldPosition)
        {
            float x = worldSizeMeters.x > 0f ? (worldPosition.x - worldOrigin.x) / worldSizeMeters.x : 0f;
            float y = worldSizeMeters.y > 0f ? (worldPosition.y - worldOrigin.y) / worldSizeMeters.y : 0f;
            return new Vector2(x, y);
        }

        public Vector3 SurfaceUVToWorld(Vector2 uv)
        {
            return new Vector3(
                worldOrigin.x + uv.x * worldSizeMeters.x,
                worldOrigin.y + uv.y * worldSizeMeters.y,
                transform.position.z);
        }

        public Vector2 SurfaceUVSizeToWorldSize(Vector2 uvSize)
        {
            return new Vector2(
                Mathf.Abs(uvSize.x) * worldSizeMeters.x,
                Mathf.Abs(uvSize.y) * worldSizeMeters.y);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.1f, 0.65f, 1f, 0.35f);
            Vector3 center = SurfaceUVToWorld(new Vector2(0.5f, 0.5f));
            Gizmos.DrawWireCube(center, new Vector3(worldSizeMeters.x, worldSizeMeters.y, 0f));
        }
    }
}
