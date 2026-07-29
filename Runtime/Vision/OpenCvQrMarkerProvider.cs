using System;
using System.Collections.Generic;
using OpenCvSharp;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ProjectionSpatialKit
{
    public sealed class OpenCvQrMarkerProvider : MonoBehaviour, IMarkerProvider
    {
        [SerializeField] private WebCamFrameSource frameSource;
        [SerializeField] private OpenCvProjectionCalibrator projectionCalibrator;
        [SerializeField] private RawImage correctedPreviewTarget;
        [SerializeField] private float detectionIntervalSeconds = 0.12f;
        [SerializeField] private double differenceThreshold = 28.0;
        [SerializeField] private int minimumCandidateSizePixels = 48;
        [SerializeField] private int candidatePaddingPixels = 24;
        [SerializeField] private bool flipHorizontal;
        [SerializeField] private bool useBackgroundDifferenceForDetection = true;
        [SerializeField] private string defaultMaterialId = "concrete";

        private readonly List<DetectedMarker> markers = new List<DetectedMarker>();
        private readonly QRCodeDetector detector = new QRCodeDetector();
        private Texture2D correctedPreviewTexture;
        private Mat backgroundGray;
        private Mat diffGray;
        private Mat thresholdedDiffGray;
        private Mat qrCandidateGray;
        private Mat dilatedCandidateMask;
        private double nextDetectionTime;
        private Mat latestCorrectedGray;

        public bool HasBackground => backgroundGray != null && !backgroundGray.Empty();

        private void Awake()
        {
            frameSource ??= FindFirstObjectByType<WebCamFrameSource>();
            projectionCalibrator ??= FindFirstObjectByType<OpenCvProjectionCalibrator>();
            SetCorrectedPreviewVisible(false);
        }

        private void OnDestroy()
        {
            detector.Dispose();
            backgroundGray?.Dispose();
            diffGray?.Dispose();
            thresholdedDiffGray?.Dispose();
            qrCandidateGray?.Dispose();
            dilatedCandidateMask?.Dispose();
            latestCorrectedGray?.Dispose();
            if (correctedPreviewTexture != null)
            {
                Destroy(correctedPreviewTexture);
            }
        }

        private void Update()
        {
            if (WasKeyPressed(KeyCode.B))
            {
                CaptureBackground();
            }
            else if (WasKeyPressed(KeyCode.R))
            {
                ClearBackground();
            }

            if (Time.unscaledTimeAsDouble < nextDetectionTime)
            {
                return;
            }

            nextDetectionTime = Time.unscaledTimeAsDouble + Mathf.Max(0.02f, detectionIntervalSeconds);
            Detect();
        }

        public IReadOnlyList<DetectedMarker> GetMarkers()
        {
            return markers;
        }

        private void Detect()
        {
            if (frameSource == null || !frameSource.TryGetFrameTexture(out _, out double timestamp))
            {
                markers.Clear();
                return;
            }

            Color32[] pixels = frameSource.CopyLatestFrame();
            int width = frameSource.Width;
            int height = frameSource.Height;
            if (pixels.Length < width * height)
            {
                markers.Clear();
                return;
            }

            using Mat rawGray = OpenCvFrameConverter.Color32ToGrayMat(pixels, width, height);
            if (rawGray.Empty())
            {
                markers.Clear();
                return;
            }

            using Mat corrected = projectionCalibrator != null ? projectionCalibrator.WarpToProjection(rawGray) : rawGray.Clone();
            if (corrected.Empty())
            {
                markers.Clear();
                return;
            }

            if (flipHorizontal)
            {
                Cv2.Flip(corrected, corrected, FlipMode.Y);
            }

            latestCorrectedGray?.Dispose();
            latestCorrectedGray = corrected.Clone();

            Mat detectMat = corrected;
            if (HasBackground)
            {
                EnsureDifferenceMats(corrected.Rows, corrected.Cols);
                Cv2.Absdiff(corrected, backgroundGray, diffGray);
                Cv2.Threshold(diffGray, thresholdedDiffGray, differenceThreshold, 255, ThresholdTypes.Binary);
                Cv2.BitwiseNot(thresholdedDiffGray, qrCandidateGray);
                detectMat = useBackgroundDifferenceForDetection ? qrCandidateGray : corrected;
            }

            if (HasBackground && useBackgroundDifferenceForDetection)
            {
                DetectQrCandidates(qrCandidateGray, thresholdedDiffGray, timestamp);
            }
            else
            {
                DetectQr(detectMat, timestamp);
            }
            SetCorrectedPreviewVisible(HasBackground);
            if (HasBackground)
            {
                UpdateCorrectedPreview(qrCandidateGray);
            }
        }

        public void CaptureBackground()
        {
            if (latestCorrectedGray == null || latestCorrectedGray.Empty())
            {
                Debug.LogWarning("ProjectionSpatialKit: Cannot capture QR background before a corrected camera frame is available.", this);
                return;
            }

            backgroundGray?.Dispose();
            backgroundGray = latestCorrectedGray.Clone();
            SetCorrectedPreviewVisible(true);
            Debug.Log($"ProjectionSpatialKit: QR background captured ({backgroundGray.Width}x{backgroundGray.Height}).", this);
        }

        public void ClearBackground()
        {
            backgroundGray?.Dispose();
            backgroundGray = null;
            markers.Clear();
            SetCorrectedPreviewVisible(false);
            Debug.Log("ProjectionSpatialKit: QR background cleared.", this);
        }

        private void EnsureDifferenceMats(int rows, int cols)
        {
            if (diffGray != null && diffGray.Rows == rows && diffGray.Cols == cols)
            {
                return;
            }

            diffGray?.Dispose();
            thresholdedDiffGray?.Dispose();
            qrCandidateGray?.Dispose();
            dilatedCandidateMask?.Dispose();
            diffGray = new Mat(rows, cols, MatType.CV_8UC1);
            thresholdedDiffGray = new Mat(rows, cols, MatType.CV_8UC1);
            qrCandidateGray = new Mat(rows, cols, MatType.CV_8UC1);
            dilatedCandidateMask = new Mat(rows, cols, MatType.CV_8UC1);
        }

        private void DetectQr(Mat correctedGray, double timestamp)
        {
            markers.Clear();

            string payload = detector.DetectAndDecode(correctedGray, out Point2f[] points);
            if (string.IsNullOrWhiteSpace(payload) || points == null || points.Length < 4)
            {
                return;
            }

            AddMarker(payload, 0, points, correctedGray.Width, correctedGray.Height, timestamp);
        }

        private void DetectQrCandidates(Mat qrImage, Mat changedMask, double timestamp)
        {
            markers.Clear();

            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(15, 15));
            Cv2.Dilate(changedMask, dilatedCandidateMask, kernel, iterations: 2);
            Cv2.FindContours(
                dilatedCandidateMask,
                out Point[][] contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            int markerIndex = 0;
            for (int i = 0; i < contours.Length; i++)
            {
                OpenCvSharp.Rect rect = Cv2.BoundingRect(contours[i]);
                if (rect.Width < minimumCandidateSizePixels || rect.Height < minimumCandidateSizePixels)
                {
                    continue;
                }

                OpenCvSharp.Rect padded = PadRect(rect, candidatePaddingPixels, qrImage.Width, qrImage.Height);
                using Mat roi = new Mat(qrImage, padded);
                string payload = detector.DetectAndDecode(roi, out Point2f[] roiPoints);
                if (string.IsNullOrWhiteSpace(payload) || roiPoints == null || roiPoints.Length < 4)
                {
                    continue;
                }

                Point2f[] fullImagePoints = new Point2f[roiPoints.Length];
                for (int p = 0; p < roiPoints.Length; p++)
                {
                    fullImagePoints[p] = new Point2f(roiPoints[p].X + padded.X, roiPoints[p].Y + padded.Y);
                }

                AddMarkerIfUnique(payload, markerIndex++, fullImagePoints, qrImage.Width, qrImage.Height, timestamp);
            }

            if (markers.Count == 0)
            {
                DetectQr(qrImage, timestamp);
            }
        }

        private void AddMarkerIfUnique(string payload, int index, Point2f[] imageCorners, int width, int height, double timestamp)
        {
            int before = markers.Count;
            AddMarker(payload, index, imageCorners, width, height, timestamp);
            if (markers.Count <= before)
            {
                return;
            }

            Vector2 center = markers[markers.Count - 1].centerUV;
            for (int i = 0; i < markers.Count - 1; i++)
            {
                if (Vector2.Distance(markers[i].centerUV, center) < 0.035f)
                {
                    markers.RemoveAt(markers.Count - 1);
                    return;
                }
            }
        }

        private static OpenCvSharp.Rect PadRect(OpenCvSharp.Rect rect, int padding, int width, int height)
        {
            int x = Mathf.Max(0, rect.X - padding);
            int y = Mathf.Max(0, rect.Y - padding);
            int right = Mathf.Min(width, rect.X + rect.Width + padding);
            int bottom = Mathf.Min(height, rect.Y + rect.Height + padding);
            return new OpenCvSharp.Rect(x, y, Mathf.Max(1, right - x), Mathf.Max(1, bottom - y));
        }

        private void AddMarker(string payload, int index, Point2f[] imageCorners, int width, int height, double timestamp)
        {
            Vector2[] cornersUV = new Vector2[4];
            Vector2 center = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                cornersUV[i] = ImagePointToSurfaceUV(imageCorners[i], width, height);
                center += cornersUV[i];
            }

            center /= 4f;
            if (center.x < 0f || center.x > 1f || center.y < 0f || center.y > 1f)
            {
                return;
            }

            string materialId = ResolveMaterialId(payload);
            Vector2 right = cornersUV[1] - cornersUV[0];
            float rotationDegrees = right.sqrMagnitude > 0.000001f ? Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg : 0f;
            string markerId = materialId + "_" + index;
            markers.Add(new DetectedMarker(markerId, materialId, center, cornersUV, rotationDegrees, 1f, timestamp));
        }

        private Vector2 ImagePointToSurfaceUV(Point2f point, int width, int height)
        {
            float x = width > 1 ? point.X / (width - 1f) : 0f;
            float yTopOrigin = height > 1 ? point.Y / (height - 1f) : 0f;
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(1f - yTopOrigin));
        }

        private string ResolveMaterialId(string payload)
        {
            string normalized = payload.Trim().ToLowerInvariant();
            if (normalized.Contains("rubber"))
            {
                return "rubber";
            }

            if (normalized.Contains("concrete"))
            {
                return "concrete";
            }

            return string.IsNullOrWhiteSpace(defaultMaterialId) ? normalized : defaultMaterialId;
        }

        private void UpdateCorrectedPreview(Mat correctedGray)
        {
            if (correctedPreviewTarget == null)
            {
                return;
            }

            if (correctedPreviewTexture == null || correctedPreviewTexture.width != correctedGray.Width || correctedPreviewTexture.height != correctedGray.Height)
            {
                if (correctedPreviewTexture != null)
                {
                    Destroy(correctedPreviewTexture);
                }

                correctedPreviewTexture = new Texture2D(correctedGray.Width, correctedGray.Height, TextureFormat.RGBA32, false)
                {
                    name = "ProjectionSpatialKit Corrected QR Preview"
                };
                correctedPreviewTarget.texture = correctedPreviewTexture;
            }

            byte[] bytes = new byte[correctedGray.Width * correctedGray.Height];
            System.Runtime.InteropServices.Marshal.Copy(correctedGray.Data, bytes, 0, bytes.Length);
            Color32[] colors = new Color32[bytes.Length];
            int width = correctedGray.Width;
            int height = correctedGray.Height;
            for (int y = 0; y < height; y++)
            {
                int sourceRow = y * width;
                int unityRow = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    byte value = bytes[sourceRow + x];
                    colors[unityRow + x] = new Color32(value, value, value, 255);
                }
            }

            correctedPreviewTexture.SetPixels32(colors);
            correctedPreviewTexture.Apply(false);
        }

        private void SetCorrectedPreviewVisible(bool visible)
        {
            if (correctedPreviewTarget != null && correctedPreviewTarget.gameObject.activeSelf != visible)
            {
                correctedPreviewTarget.gameObject.SetActive(visible);
            }
        }

        private static bool WasKeyPressed(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (key == KeyCode.B)
                {
                    return Keyboard.current.bKey.wasPressedThisFrame;
                }

                if (key == KeyCode.R)
                {
                    return Keyboard.current.rKey.wasPressedThisFrame;
                }
            }
#endif

            try
            {
                return Input.GetKeyDown(key);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
