using OpenCvSharp;
using UnityEngine;

namespace ProjectionSpatialKit
{
    public sealed class OpenCvProjectionCalibrator : MonoBehaviour
    {
        [SerializeField] private bool useCalibration = true;
        [SerializeField] private Vector2 topLeft = new Vector2(0f, 0f);
        [SerializeField] private Vector2 topRight = new Vector2(1f, 0f);
        [SerializeField] private Vector2 bottomRight = new Vector2(1f, 1f);
        [SerializeField] private Vector2 bottomLeft = new Vector2(0f, 1f);
        [SerializeField] private Vector2Int outputSize = new Vector2Int(1280, 720);

        private Mat homography;
        private int cachedSourceWidth = -1;
        private int cachedSourceHeight = -1;
        private Vector2 cachedTopLeft;
        private Vector2 cachedTopRight;
        private Vector2 cachedBottomRight;
        private Vector2 cachedBottomLeft;
        private Vector2Int cachedOutputSize;

        public bool UseCalibration
        {
            get => useCalibration;
            set
            {
                if (useCalibration != value)
                {
                    useCalibration = value;
                    Invalidate();
                }
            }
        }

        public Vector2 TopLeft => topLeft;
        public Vector2 TopRight => topRight;
        public Vector2 BottomRight => bottomRight;
        public Vector2 BottomLeft => bottomLeft;
        public Vector2Int OutputSize => outputSize;

        private void OnValidate()
        {
            topLeft = Clamp01(topLeft);
            topRight = Clamp01(topRight);
            bottomRight = Clamp01(bottomRight);
            bottomLeft = Clamp01(bottomLeft);
            outputSize = new Vector2Int(Mathf.Max(32, outputSize.x), Mathf.Max(32, outputSize.y));
            Invalidate();
        }

        private void OnDestroy()
        {
            homography?.Dispose();
        }

        public Mat WarpToProjection(Mat source)
        {
            if (source == null || source.Empty())
            {
                return new Mat();
            }

            if (!useCalibration)
            {
                return source.Clone();
            }

            EnsureHomography(source.Width, source.Height);
            Mat output = new Mat(outputSize.y, outputSize.x, source.Type());
            Cv2.WarpPerspective(source, output, homography, new Size(outputSize.x, outputSize.y));
            return output;
        }

        public void SetCorners(Vector2 newTopLeft, Vector2 newTopRight, Vector2 newBottomRight, Vector2 newBottomLeft)
        {
            topLeft = Clamp01(newTopLeft);
            topRight = Clamp01(newTopRight);
            bottomRight = Clamp01(newBottomRight);
            bottomLeft = Clamp01(newBottomLeft);
            Invalidate();
        }

        private void EnsureHomography(int sourceWidth, int sourceHeight)
        {
            if (homography != null
                && cachedSourceWidth == sourceWidth
                && cachedSourceHeight == sourceHeight
                && cachedTopLeft == topLeft
                && cachedTopRight == topRight
                && cachedBottomRight == bottomRight
                && cachedBottomLeft == bottomLeft
                && cachedOutputSize == outputSize)
            {
                return;
            }

            homography?.Dispose();
            Point2f[] src =
            {
                ToPixel(topLeft, sourceWidth, sourceHeight),
                ToPixel(topRight, sourceWidth, sourceHeight),
                ToPixel(bottomRight, sourceWidth, sourceHeight),
                ToPixel(bottomLeft, sourceWidth, sourceHeight)
            };
            Point2f[] dst =
            {
                new Point2f(0f, 0f),
                new Point2f(outputSize.x - 1f, 0f),
                new Point2f(outputSize.x - 1f, outputSize.y - 1f),
                new Point2f(0f, outputSize.y - 1f)
            };

            homography = Cv2.GetPerspectiveTransform(src, dst);
            cachedSourceWidth = sourceWidth;
            cachedSourceHeight = sourceHeight;
            cachedTopLeft = topLeft;
            cachedTopRight = topRight;
            cachedBottomRight = bottomRight;
            cachedBottomLeft = bottomLeft;
            cachedOutputSize = outputSize;
        }

        private void Invalidate()
        {
            cachedSourceWidth = -1;
            cachedSourceHeight = -1;
        }

        private static Point2f ToPixel(Vector2 normalizedTopLeftOrigin, int width, int height)
        {
            return new Point2f(normalizedTopLeftOrigin.x * (width - 1f), normalizedTopLeftOrigin.y * (height - 1f));
        }

        private static Vector2 Clamp01(Vector2 value)
        {
            return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
        }
    }
}
