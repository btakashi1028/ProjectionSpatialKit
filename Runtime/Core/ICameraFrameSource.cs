using UnityEngine;

namespace ProjectionSpatialKit
{
    public interface ICameraFrameSource
    {
        int Width { get; }
        int Height { get; }
        bool TryGetFrameTexture(out Texture texture, out double timestamp);
    }
}
