using NUnit.Framework;
using UnityEngine;

namespace ProjectionSpatialKit.Tests
{
    public sealed class VirtualProjectorMathTests
    {
        private GameObject go;
        private VirtualProjectorLight projector;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("projector under test");
            projector = go.AddComponent<VirtualProjectorLight>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
        }

        [Test]
        public void HorizontalFov_FromThrowRatio_MatchesCatalogueMath()
        {
            // Manual intrinsics (no profile): throw ratio 1.5 → hFOV = 2 atan(0.5 / 1.5) ≈ 36.87°
            // (the §12.2 verified value).
            Assert.AreEqual(36.87f, projector.HorizontalFovDegrees, 0.01f);
        }

        [Test]
        public void SpotAngle_IsWiderThanImageFov_AndDistanceIndependent()
        {
            float spot = projector.SpotAngleDegrees;
            Assert.Greater(spot, projector.HorizontalFovDegrees);
            go.transform.position += new Vector3(0f, 0f, 3f); // pose must not change intrinsics
            Assert.AreEqual(spot, projector.SpotAngleDegrees, 1e-4f);
        }

        [Test]
        public void WorldToContentUV_OnAxisPoint_IsCanvasCentre()
        {
            Assert.IsTrue(projector.TryWorldToContentUV(new Vector3(0f, 0f, 2f), out Vector2 uv));
            Assert.AreEqual(0.5f, uv.x, 1e-4f);
            Assert.AreEqual(0.5f, uv.y, 1e-4f);
        }

        [Test]
        public void WorldToContentUV_BehindProjector_Fails()
        {
            Assert.IsFalse(projector.TryWorldToContentUV(new Vector3(0f, 0f, -1f), out _));
        }

        [Test]
        public void WorldToContentUV_RightOfAxis_ReadsRightOfCanvas()
        {
            // FRONT projection (the default): the image is NOT mirrored, so a world point to
            // the projector's right is the right-hand side of the content canvas. Only rear
            // projection flips it, and that is opt-in.
            Assert.IsTrue(projector.TryWorldToContentUV(new Vector3(0.3f, 0f, 2f), out Vector2 uv));
            Assert.Greater(uv.x, 0.5f);
            Assert.AreEqual(0.5f, uv.y, 1e-4f);
        }

        [Test]
        public void WorldToContentUV_AboveAxis_MapsUpward()
        {
            Assert.IsTrue(projector.TryWorldToContentUV(new Vector3(0f, 0.3f, 2f), out Vector2 uv));
            Assert.Greater(uv.y, 0.5f);
        }

        [Test]
        public void WorldToContentUV_Portrait_MapsVerticalToContentX()
        {
            // Portrait mount: the content's horizontal axis runs vertically on the wall, so an
            // above-axis point moves content u, not v.
            projector.ImageOrientation = DisplayOrientation.Portrait;
            Assert.IsTrue(projector.TryWorldToContentUV(new Vector3(0f, 0.3f, 2f), out Vector2 uv));
            Assert.Greater(uv.x, 0.5f);
            Assert.AreEqual(0.5f, uv.y, 1e-4f);
        }

        [Test]
        public void WorldToContentUV_Portrait_CentreStaysCentre()
        {
            projector.ImageOrientation = DisplayOrientation.Portrait;
            Assert.IsTrue(projector.TryWorldToContentUV(new Vector3(0f, 0f, 2f), out Vector2 uv));
            Assert.AreEqual(0.5f, uv.x, 1e-4f);
            Assert.AreEqual(0.5f, uv.y, 1e-4f);
        }

        [Test]
        public void WorldToContentUV_ScalesWithDistance()
        {
            // The same off-axis ANGLE maps to the same UV at any distance (cone projection):
            // doubling both the offset and the distance leaves UV unchanged.
            Assert.IsTrue(projector.TryWorldToContentUV(new Vector3(0.3f, 0.2f, 2f), out Vector2 near));
            Assert.IsTrue(projector.TryWorldToContentUV(new Vector3(0.6f, 0.4f, 4f), out Vector2 far));
            Assert.AreEqual(near.x, far.x, 1e-4f);
            Assert.AreEqual(near.y, far.y, 1e-4f);
        }
    }
}
