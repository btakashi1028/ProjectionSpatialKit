using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §5.4: the URG coordinate pipeline "polar → scan-plane point → canvas UV" in one
    /// class, isolated from the rig so connecting a REAL URG later uses the exact same path:
    /// the sensor gives polar readings, a 4-point calibration (touch the 4 corners of the
    /// projected image) gives the plane→canvas homography, and this class does the rest.
    /// Pure math — no scene access — so it is EditMode-testable.
    ///
    /// Plane coordinates: x = to the sensor's right, y = straight ahead, angle 0 = ahead,
    /// positive = to the right, metres.
    /// </summary>
    public sealed class UrgPolarToCanvasConverter
    {
        // Row-major 3x3 homography, h[8] fixed to 1.
        private readonly float[] h = new float[9];
        public bool IsCalibrated { get; private set; }

        /// <summary>Polar reading (radians, metres) to a scan-plane point.</summary>
        public static Vector2 PolarToPlanePoint(float angleRadians, float distanceMetres)
        {
            return new Vector2(
                Mathf.Sin(angleRadians) * distanceMetres,
                Mathf.Cos(angleRadians) * distanceMetres);
        }

        /// <summary>
        /// Calibrates the plane→canvas mapping from 4 point correspondences (no 3 of the
        /// plane points collinear). Returns false when the system is degenerate.
        /// </summary>
        public bool TryCalibrate(Vector2[] planePoints, Vector2[] canvasUVs)
        {
            IsCalibrated = planePoints != null && canvasUVs != null
                && planePoints.Length == 4 && canvasUVs.Length == 4
                && TrySolveHomography(planePoints, canvasUVs, h);
            return IsCalibrated;
        }

        /// <summary>Scan-plane point to canvas UV. False before calibration or at the horizon.</summary>
        public bool TryPlaneToCanvasUV(Vector2 planePoint, out Vector2 uv)
        {
            uv = default;
            if (!IsCalibrated)
            {
                return false;
            }
            float w = h[6] * planePoint.x + h[7] * planePoint.y + h[8];
            if (Mathf.Abs(w) < 1e-6f)
            {
                return false;
            }
            uv = new Vector2(
                (h[0] * planePoint.x + h[1] * planePoint.y + h[2]) / w,
                (h[3] * planePoint.x + h[4] * planePoint.y + h[5]) / w);
            return true;
        }

        /// <summary>Polar reading straight to canvas UV (the full real-URG path).</summary>
        public bool TryPolarToCanvasUV(float angleRadians, float distanceMetres, out Vector2 uv)
        {
            return TryPlaneToCanvasUV(PolarToPlanePoint(angleRadians, distanceMetres), out uv);
        }

        /// <summary>
        /// 4-point DLT: solves the 8 unknowns of a homography (h33 = 1) with Gaussian
        /// elimination + partial pivoting. Public/static for tests.
        /// </summary>
        public static bool TrySolveHomography(Vector2[] src, Vector2[] dst, float[] result)
        {
            // Two equations per correspondence:
            //   u = (h0 x + h1 y + h2) - u (h6 x + h7 y)
            //   v = (h3 x + h4 y + h5) - v (h6 x + h7 y)
            float[,] a = new float[8, 9];
            for (int i = 0; i < 4; i++)
            {
                float x = src[i].x, y = src[i].y, u = dst[i].x, v = dst[i].y;
                int r = i * 2;
                a[r, 0] = x; a[r, 1] = y; a[r, 2] = 1f;
                a[r, 6] = -u * x; a[r, 7] = -u * y; a[r, 8] = u;
                a[r + 1, 3] = x; a[r + 1, 4] = y; a[r + 1, 5] = 1f;
                a[r + 1, 6] = -v * x; a[r + 1, 7] = -v * y; a[r + 1, 8] = v;
            }

            for (int col = 0; col < 8; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < 8; row++)
                {
                    if (Mathf.Abs(a[row, col]) > Mathf.Abs(a[pivot, col]))
                    {
                        pivot = row;
                    }
                }
                if (Mathf.Abs(a[pivot, col]) < 1e-9f)
                {
                    return false; // degenerate (collinear points)
                }
                if (pivot != col)
                {
                    for (int k = 0; k <= 8; k++)
                    {
                        (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                    }
                }
                float inv = 1f / a[col, col];
                for (int k = col; k <= 8; k++)
                {
                    a[col, k] *= inv;
                }
                for (int row = 0; row < 8; row++)
                {
                    if (row == col)
                    {
                        continue;
                    }
                    float factor = a[row, col];
                    if (factor == 0f)
                    {
                        continue;
                    }
                    for (int k = col; k <= 8; k++)
                    {
                        a[row, k] -= factor * a[col, k];
                    }
                }
            }

            for (int i = 0; i < 8; i++)
            {
                result[i] = a[i, 8];
            }
            result[8] = 1f;
            return true;
        }
    }
}
