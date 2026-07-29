using System;
using System.Collections.Generic;
using OpenCvSharp;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectionSpatialKit
{
    public sealed class OpenCvColorSquareMarkerProvider : MonoBehaviour, IMarkerProvider
    {
        [SerializeField] private WebCamFrameSource frameSource;
        [SerializeField] private OpenCvProjectionCalibrator projectionCalibrator;
        [SerializeField] private ColorMarkerDetectionDatabase detectionDatabase;
        [SerializeField] private RawImage correctedPreviewTarget;
        [SerializeField] private float detectionIntervalSeconds = 0.08f;
        [SerializeField] private bool flipHorizontal;
        [SerializeField] private bool showMaskPreview = true;

        private readonly List<DetectedMarker> markers = new List<DetectedMarker>();
        private readonly List<CandidateMarker> candidates = new List<CandidateMarker>();
        private Texture2D correctedPreviewTexture;
        private Mat hsv;
        private Mat rgb;
        private Mat targetMask;
        private Mat sampleMask;
        private Mat previewMask;
        private Mat latestCorrectedRgba;
        private double nextDetectionTime;

        public ColorMarkerDetectionDatabase DetectionDatabase => detectionDatabase;
        public int LastDetectedCount => markers.Count;

        private void Awake()
        {
            frameSource ??= FindFirstObjectByType<WebCamFrameSource>();
            projectionCalibrator ??= FindFirstObjectByType<OpenCvProjectionCalibrator>();
            SetCorrectedPreviewVisible(showMaskPreview);
        }

        private void OnDestroy()
        {
            hsv?.Dispose();
            rgb?.Dispose();
            targetMask?.Dispose();
            sampleMask?.Dispose();
            previewMask?.Dispose();
            latestCorrectedRgba?.Dispose();
            if (correctedPreviewTexture != null)
            {
                Destroy(correctedPreviewTexture);
            }
        }

        private void Update()
        {
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

        public bool TrySampleCorrectedColor(Vector2 projectionUv, int radiusPixels, out Color color)
        {
            color = Color.clear;
            if (latestCorrectedRgba == null || latestCorrectedRgba.Empty())
            {
                return false;
            }

            projectionUv = new Vector2(Mathf.Clamp01(projectionUv.x), Mathf.Clamp01(projectionUv.y));
            int width = latestCorrectedRgba.Width;
            int height = latestCorrectedRgba.Height;
            int centerX = Mathf.Clamp(Mathf.RoundToInt(projectionUv.x * (width - 1)), 0, width - 1);
            int centerY = Mathf.Clamp(Mathf.RoundToInt((1f - projectionUv.y) * (height - 1)), 0, height - 1);
            int radius = Mathf.Max(0, radiusPixels);
            int minX = Mathf.Max(0, centerX - radius);
            int maxX = Mathf.Min(width - 1, centerX + radius);
            int minY = Mathf.Max(0, centerY - radius);
            int maxY = Mathf.Min(height - 1, centerY + radius);
            float r = 0f;
            float g = 0f;
            float b = 0f;
            int count = 0;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vec4b pixel = latestCorrectedRgba.At<Vec4b>(y, x);
                    float pr = pixel.Item0 / 255f;
                    float pg = pixel.Item1 / 255f;
                    float pb = pixel.Item2 / 255f;
                    float brightness = Mathf.Max(pr, Mathf.Max(pg, pb));
                    if (brightness > 0.98f || brightness < 0.02f)
                    {
                        continue;
                    }

                    r += pr;
                    g += pg;
                    b += pb;
                    count++;
                }
            }

            if (count == 0)
            {
                return false;
            }

            color = new Color(r / count, g / count, b / count, 1f);
            return true;
        }

        private void Detect()
        {
            markers.Clear();
            candidates.Clear();
            if (detectionDatabase == null || detectionDatabase.Targets.Count == 0)
            {
                return;
            }

            if (frameSource == null || !frameSource.TryGetFrameTexture(out _, out double timestamp))
            {
                return;
            }

            Color32[] pixels = frameSource.CopyLatestFrame();
            int width = frameSource.Width;
            int height = frameSource.Height;
            if (pixels.Length < width * height)
            {
                return;
            }

            using Mat rawRgba = OpenCvFrameConverter.Color32ToRgbaMat(pixels, width, height);
            if (rawRgba.Empty())
            {
                return;
            }

            using Mat correctedRgba = projectionCalibrator != null ? projectionCalibrator.WarpToProjection(rawRgba) : rawRgba.Clone();
            if (correctedRgba.Empty())
            {
                return;
            }

            if (flipHorizontal)
            {
                Cv2.Flip(correctedRgba, correctedRgba, FlipMode.Y);
            }

            latestCorrectedRgba?.Dispose();
            latestCorrectedRgba = correctedRgba.Clone();

            EnsureMats(correctedRgba.Rows, correctedRgba.Cols);
            Cv2.CvtColor(correctedRgba, rgb, ColorConversionCodes.RGBA2RGB);
            Cv2.CvtColor(rgb, hsv, ColorConversionCodes.RGB2HSV);
            previewMask.SetTo(Scalar.Black);

            for (int i = 0; i < detectionDatabase.Targets.Count; i++)
            {
                ColorMarkerDetectionTarget target = detectionDatabase.Targets[i];
                if (target == null || target.SampleColors.Count == 0)
                {
                    continue;
                }

                BuildTargetMask(target);
                FindTargetCandidates(target, timestamp, correctedRgba.Width, correctedRgba.Height, i);
                Cv2.BitwiseOr(previewMask, targetMask, previewMask);
            }

            candidates.Sort((a, b) => b.Marker.confidence.CompareTo(a.Marker.confidence));
            int accepted = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (OverlapsAccepted(candidates[i].Marker.centerUV))
                {
                    continue;
                }

                DetectedMarker marker = candidates[i].Marker;
                marker.id = marker.id + "_" + accepted;
                markers.Add(marker);
                accepted++;
            }

            SetCorrectedPreviewVisible(showMaskPreview);
            if (showMaskPreview)
            {
                UpdateCorrectedPreview(previewMask);
            }
        }

        private void EnsureMats(int rows, int cols)
        {
            if (hsv != null && hsv.Rows == rows && hsv.Cols == cols)
            {
                return;
            }

            hsv?.Dispose();
            rgb?.Dispose();
            targetMask?.Dispose();
            sampleMask?.Dispose();
            previewMask?.Dispose();
            hsv = new Mat(rows, cols, MatType.CV_8UC3);
            rgb = new Mat(rows, cols, MatType.CV_8UC3);
            targetMask = new Mat(rows, cols, MatType.CV_8UC1);
            sampleMask = new Mat(rows, cols, MatType.CV_8UC1);
            previewMask = new Mat(rows, cols, MatType.CV_8UC1);
        }

        private void BuildTargetMask(ColorMarkerDetectionTarget target)
        {
            targetMask.SetTo(Scalar.Black);
            for (int i = 0; i < target.SampleColors.Count; i++)
            {
                AddSampleMask(target, target.SampleColors[i]);
            }

            int kernelSize = detectionDatabase.MorphologyKernelSize;
            int iterations = detectionDatabase.MorphologyIterations;
            if (iterations <= 0)
            {
                return;
            }

            using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelSize, kernelSize));
            Cv2.MorphologyEx(targetMask, targetMask, MorphTypes.Open, kernel, iterations: iterations);
            Cv2.MorphologyEx(targetMask, targetMask, MorphTypes.Close, kernel, iterations: iterations);
        }

        private void AddSampleMask(ColorMarkerDetectionTarget target, Color color)
        {
            Color.RGBToHSV(color, out float hue, out float saturation, out float value);
            int hueCenter = Mathf.RoundToInt(hue * 179f);
            int hueTolerance = Mathf.CeilToInt(target.HueToleranceDegrees * 0.5f);
            int saturationCenter = Mathf.RoundToInt(saturation * 255f);
            int valueCenter = Mathf.RoundToInt(value * 255f);
            int saturationTolerance = Mathf.RoundToInt(target.SaturationTolerance * 255f);
            int valueTolerance = Mathf.RoundToInt(target.ValueTolerance * 255f);
            int minSaturation = Mathf.RoundToInt(target.MinimumSaturation * 255f);
            int minValue = Mathf.RoundToInt(target.MinimumValue * 255f);

            int lowS = Mathf.Max(minSaturation, saturationCenter - saturationTolerance);
            int highS = Mathf.Min(255, saturationCenter + saturationTolerance);
            int lowV = Mathf.Max(minValue, valueCenter - valueTolerance);
            int highV = Mathf.Min(255, valueCenter + valueTolerance);
            AddHueRangeMask(hueCenter - hueTolerance, hueCenter + hueTolerance, lowS, highS, lowV, highV);
        }

        private void AddHueRangeMask(int lowH, int highH, int lowS, int highS, int lowV, int highV)
        {
            if (lowH < 0)
            {
                AddHueRangeMask(lowH + 180, 179, lowS, highS, lowV, highV);
                AddHueRangeMask(0, highH, lowS, highS, lowV, highV);
                return;
            }

            if (highH > 179)
            {
                AddHueRangeMask(lowH, 179, lowS, highS, lowV, highV);
                AddHueRangeMask(0, highH - 180, lowS, highS, lowV, highV);
                return;
            }

            Cv2.InRange(hsv, new Scalar(lowH, lowS, lowV), new Scalar(highH, highS, highV), sampleMask);
            Cv2.BitwiseOr(targetMask, sampleMask, targetMask);
        }

        private void FindTargetCandidates(ColorMarkerDetectionTarget target, double timestamp, int width, int height, int targetIndex)
        {
            Cv2.FindContours(
                targetMask,
                out Point[][] contours,
                out _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple);

            int localIndex = 0;
            for (int i = 0; i < contours.Length; i++)
            {
                double area = Cv2.ContourArea(contours[i]);
                if (area < detectionDatabase.MinimumAreaPixels || area > detectionDatabase.MaximumAreaPixels)
                {
                    continue;
                }

                RotatedRect rect = Cv2.MinAreaRect(contours[i]);
                float longSide = Mathf.Max(rect.Size.Width, rect.Size.Height);
                float shortSide = Mathf.Min(rect.Size.Width, rect.Size.Height);
                if (shortSide < 1f)
                {
                    continue;
                }

                float squareRatio = shortSide / longSide;
                if (squareRatio < detectionDatabase.MinimumSquareRatio)
                {
                    continue;
                }

                float rectArea = Mathf.Max(1f, rect.Size.Width * rect.Size.Height);
                float fillRatio = Mathf.Clamp01((float)(area / rectArea));
                if (fillRatio < detectionDatabase.MinimumFillRatio)
                {
                    continue;
                }

                Point2f[] imageCorners = OrderCorners(rect.Points());
                Vector2[] cornersUV = new Vector2[4];
                Vector2 center = Vector2.zero;
                for (int c = 0; c < cornersUV.Length; c++)
                {
                    cornersUV[c] = ImagePointToSurfaceUV(imageCorners[c], width, height);
                    center += cornersUV[c];
                }

                center /= 4f;
                if (center.x < 0f || center.x > 1f || center.y < 0f || center.y > 1f)
                {
                    continue;
                }

                Vector2 right = cornersUV[1] - cornersUV[0];
                float rotationDegrees = right.sqrMagnitude > 0.000001f ? Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg : 0f;
                float confidence = Mathf.Clamp01(squareRatio * 0.55f + fillRatio * 0.45f);
                string markerId = target.MarkerId + "_" + targetIndex + "_" + localIndex;
                candidates.Add(new CandidateMarker(new DetectedMarker(markerId, target.MaterialId, center, cornersUV, rotationDegrees, confidence, timestamp)));
                localIndex++;
            }
        }

        private bool OverlapsAccepted(Vector2 center)
        {
            for (int i = 0; i < markers.Count; i++)
            {
                if (Vector2.Distance(markers[i].centerUV, center) < 0.025f)
                {
                    return true;
                }
            }

            return false;
        }

        private static Point2f[] OrderCorners(Point2f[] points)
        {
            Array.Sort(points, (a, b) => a.Y.CompareTo(b.Y));
            Point2f[] top = { points[0], points[1] };
            Point2f[] bottom = { points[2], points[3] };
            Array.Sort(top, (a, b) => a.X.CompareTo(b.X));
            Array.Sort(bottom, (a, b) => a.X.CompareTo(b.X));
            return new[] { top[0], top[1], bottom[1], bottom[0] };
        }

        private static Vector2 ImagePointToSurfaceUV(Point2f point, int width, int height)
        {
            float x = width > 1 ? point.X / (width - 1f) : 0f;
            float yTopOrigin = height > 1 ? point.Y / (height - 1f) : 0f;
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(1f - yTopOrigin));
        }

        private void UpdateCorrectedPreview(Mat mask)
        {
            if (correctedPreviewTarget == null)
            {
                return;
            }

            if (correctedPreviewTexture == null || correctedPreviewTexture.width != mask.Width || correctedPreviewTexture.height != mask.Height)
            {
                if (correctedPreviewTexture != null)
                {
                    Destroy(correctedPreviewTexture);
                }

                correctedPreviewTexture = new Texture2D(mask.Width, mask.Height, TextureFormat.RGBA32, false)
                {
                    name = "ProjectionSpatialKit Color Marker Mask Preview"
                };
                correctedPreviewTarget.texture = correctedPreviewTexture;
            }

            byte[] bytes = new byte[mask.Width * mask.Height];
            System.Runtime.InteropServices.Marshal.Copy(mask.Data, bytes, 0, bytes.Length);
            Color32[] colors = new Color32[bytes.Length];
            int width = mask.Width;
            int height = mask.Height;
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

        private readonly struct CandidateMarker
        {
            public readonly DetectedMarker Marker;

            public CandidateMarker(DetectedMarker marker)
            {
                Marker = marker;
            }
        }
    }
}
