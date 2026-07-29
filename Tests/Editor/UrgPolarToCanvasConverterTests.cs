using NUnit.Framework;
using UnityEngine;

namespace ProjectionSpatialKit.Tests
{
    public sealed class UrgPolarToCanvasConverterTests
    {
        private const float Epsilon = 1e-4f;

        [Test]
        public void PolarToPlanePoint_ZeroAngle_IsStraightAhead()
        {
            Vector2 p = UrgPolarToCanvasConverter.PolarToPlanePoint(0f, 2.5f);
            Assert.AreEqual(0f, p.x, Epsilon);
            Assert.AreEqual(2.5f, p.y, Epsilon);
        }

        [Test]
        public void PolarToPlanePoint_RightAngle_IsToTheRight()
        {
            Vector2 p = UrgPolarToCanvasConverter.PolarToPlanePoint(Mathf.PI * 0.5f, 3f);
            Assert.AreEqual(3f, p.x, Epsilon);
            Assert.AreEqual(0f, p.y, Epsilon);
        }

        [Test]
        public void Calibrate_Identity_MapsPointsToThemselves()
        {
            var converter = new UrgPolarToCanvasConverter();
            Vector2[] corners = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            Assert.IsTrue(converter.TryCalibrate(corners, corners));

            Vector2 probe = new Vector2(0.31f, 0.77f);
            Assert.IsTrue(converter.TryPlaneToCanvasUV(probe, out Vector2 uv));
            Assert.AreEqual(probe.x, uv.x, Epsilon);
            Assert.AreEqual(probe.y, uv.y, Epsilon);
        }

        [Test]
        public void Calibrate_AffineRect_MapsInteriorLinearly()
        {
            // Plane rectangle x∈[-1,1], y∈[1,3] mapped onto the unit UV square.
            var converter = new UrgPolarToCanvasConverter();
            Vector2[] plane = { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 3f), new Vector2(-1f, 3f) };
            Vector2[] uvs = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            Assert.IsTrue(converter.TryCalibrate(plane, uvs));

            Assert.IsTrue(converter.TryPlaneToCanvasUV(new Vector2(0f, 2f), out Vector2 centre));
            Assert.AreEqual(0.5f, centre.x, Epsilon);
            Assert.AreEqual(0.5f, centre.y, Epsilon);

            Assert.IsTrue(converter.TryPlaneToCanvasUV(new Vector2(-0.5f, 1.5f), out Vector2 quarter));
            Assert.AreEqual(0.25f, quarter.x, Epsilon);
            Assert.AreEqual(0.25f, quarter.y, Epsilon);
        }

        [Test]
        public void Calibrate_Perspective_ReproducesReferenceHomography()
        {
            // Reference perspective map (a projector seen obliquely): u = x / (0.2 y + 1),
            // v = y / (0.2 y + 1). Calibrate from 4 correspondences, verify a 5th point.
            System.Func<Vector2, Vector2> reference = p =>
                new Vector2(p.x / (0.2f * p.y + 1f), p.y / (0.2f * p.y + 1f));

            Vector2[] plane = { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1.4f, 3f), new Vector2(-1.4f, 3f) };
            Vector2[] uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                uvs[i] = reference(plane[i]);
            }

            var converter = new UrgPolarToCanvasConverter();
            Assert.IsTrue(converter.TryCalibrate(plane, uvs));

            Vector2 probe = new Vector2(0.3f, 2.2f);
            Vector2 expected = reference(probe);
            Assert.IsTrue(converter.TryPlaneToCanvasUV(probe, out Vector2 uv));
            Assert.AreEqual(expected.x, uv.x, 1e-3f);
            Assert.AreEqual(expected.y, uv.y, 1e-3f);
        }

        [Test]
        public void Calibrate_CollinearPoints_Fails()
        {
            var converter = new UrgPolarToCanvasConverter();
            Vector2[] plane = { new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(2f, 2f), new Vector2(3f, 3f) };
            Vector2[] uvs = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            Assert.IsFalse(converter.TryCalibrate(plane, uvs));
            Assert.IsFalse(converter.TryPlaneToCanvasUV(Vector2.one, out _));
        }

        [Test]
        public void PolarToCanvasUV_FullPipeline_MatchesPlanePath()
        {
            var converter = new UrgPolarToCanvasConverter();
            Vector2[] plane = { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 3f), new Vector2(-1f, 3f) };
            Vector2[] uvs = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            Assert.IsTrue(converter.TryCalibrate(plane, uvs));

            float angle = 0.3f;
            float distance = 2.0f;
            Vector2 planePoint = UrgPolarToCanvasConverter.PolarToPlanePoint(angle, distance);
            Assert.IsTrue(converter.TryPolarToCanvasUV(angle, distance, out Vector2 fromPolar));
            Assert.IsTrue(converter.TryPlaneToCanvasUV(planePoint, out Vector2 fromPlane));
            Assert.AreEqual(fromPlane.x, fromPolar.x, Epsilon);
            Assert.AreEqual(fromPlane.y, fromPolar.y, Epsilon);
        }
    }
}
