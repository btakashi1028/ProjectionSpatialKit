using UnityEngine;

namespace ProjectionSpatialKit
{
    public interface IProjectionSurfaceMapper
    {
        Vector2 WorldToSurfaceUV(Vector3 worldPosition);
        Vector3 SurfaceUVToWorld(Vector2 uv);
        Vector2 SurfaceUVSizeToWorldSize(Vector2 uvSize);
    }
}
