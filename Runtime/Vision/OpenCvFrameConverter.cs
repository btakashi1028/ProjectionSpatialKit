using UnityEngine;

namespace ProjectionSpatialKit
{
    public static class OpenCvFrameConverter
    {
        public static OpenCvSharp.Mat Color32ToRgbaMat(Color32[] pixels, int width, int height)
        {
            if (pixels == null || pixels.Length == 0 || width <= 0 || height <= 0)
            {
                return new OpenCvSharp.Mat();
            }

            int expectedLength = width * height;
            if (pixels.Length < expectedLength)
            {
                Debug.LogWarning($"ProjectionSpatialKit: Pixel buffer is smaller than expected. pixels={pixels.Length}, expected={expectedLength}");
                return new OpenCvSharp.Mat();
            }

            byte[] bytes = new byte[expectedLength * 4];
            for (int yTop = 0; yTop < height; yTop++)
            {
                int unityY = height - 1 - yTop;
                int sourceRow = unityY * width;
                int targetRow = yTop * width * 4;
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = pixels[sourceRow + x];
                    int target = targetRow + x * 4;
                    bytes[target] = pixel.r;
                    bytes[target + 1] = pixel.g;
                    bytes[target + 2] = pixel.b;
                    bytes[target + 3] = pixel.a;
                }
            }

            OpenCvSharp.Mat mat = new OpenCvSharp.Mat(height, width, OpenCvSharp.MatType.CV_8UC4);
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, mat.Data, bytes.Length);
            return mat;
        }

        public static OpenCvSharp.Mat Color32ToGrayMat(Color32[] pixels, int width, int height)
        {
            using OpenCvSharp.Mat rgba = Color32ToRgbaMat(pixels, width, height);
            if (rgba.Empty())
            {
                return new OpenCvSharp.Mat();
            }

            OpenCvSharp.Mat gray = new OpenCvSharp.Mat();
            OpenCvSharp.Cv2.CvtColor(rgba, gray, OpenCvSharp.ColorConversionCodes.RGBA2GRAY);
            return gray;
        }
    }
}
