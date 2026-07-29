using NUnit.Framework;
using UnityEngine;

namespace ProjectionSpatialKit.Tests
{
    /// <summary>
    /// The footprint trace is what the preflight checks measure the venue with, so its
    /// geometry has to be right: image width must follow the throw-ratio relation
    /// (width = distance / throwRatio) on a wall the projector faces head-on, and the trace
    /// must report honestly when the beam reaches nothing.
    /// </summary>
    public sealed class ProjectedImageFootprintTests
    {
        private GameObject projectorGo;
        private GameObject wall;
        private VirtualProjectorLight projector;

        [SetUp]
        public void SetUp()
        {
            projectorGo = new GameObject("projector", typeof(Light), typeof(VirtualProjectorLight));
            projector = projectorGo.GetComponent<VirtualProjectorLight>();
            projectorGo.transform.position = Vector3.zero;
            projectorGo.transform.rotation = Quaternion.identity; // looking down +Z

            // A large wall at z = +3, facing back toward the projector.
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 3.5f);
            wall.transform.localScale = new Vector3(40f, 40f, 1f);

            // Edit mode runs no physics step, so collider transforms must be pushed to the
            // physics scene by hand before any raycast sees them.
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(projectorGo);
            Object.DestroyImmediate(wall);
        }

        [Test]
        public void Trace_HeadOnWall_ReportsThrowRatioWidthAndZeroIncidence()
        {
            Assert.IsTrue(projector.TryTraceImage(out ProjectedImageFootprint footprint));

            // Wall face sits at z = 3.0 (centre 3.5 minus half of the 1-unit depth).
            Assert.AreEqual(3f, footprint.ThrowDistance, 0.02f);
            Assert.AreEqual(0f, footprint.IncidenceDegrees, 0.5f, "facing the wall squarely");

            // The defining relation of a projector: width = distance / throw ratio.
            float expectedWidth = footprint.ThrowDistance / projector.EffectiveThrowRatio;
            Assert.AreEqual(expectedWidth, footprint.Width, expectedWidth * 0.02f);

            Assert.IsTrue(footprint.AllCornersHit);
            Assert.AreEqual(4, footprint.CornersOnSurface, "the whole image lands on one wall");
            Assert.AreEqual(projector.ImageAspect, footprint.Aspect, 0.05f);
        }

        [Test]
        public void Trace_TiltedWall_ReportsIncidenceAngle()
        {
            wall.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            Physics.SyncTransforms();

            Assert.IsTrue(projector.TryTraceImage(out ProjectedImageFootprint footprint));
            Assert.AreEqual(30f, footprint.IncidenceDegrees, 1f);
        }

        [Test]
        public void Trace_NothingInRange_Fails()
        {
            wall.transform.position = new Vector3(0f, 0f, 500f); // beyond any throw distance
            Physics.SyncTransforms();

            Assert.IsFalse(projector.TryTraceImage(out _),
                "a beam that reaches nothing must not report a footprint");
        }

        [Test]
        public void ImageRayDirection_SpansTheImage_LeftOfCentreToRightOfCentre()
        {
            Vector3 left = projector.GetImageRayDirection(0f, 0.5f);
            Vector3 centre = projector.GetImageRayDirection(0.5f, 0.5f);
            Vector3 right = projector.GetImageRayDirection(1f, 0.5f);

            // Centre ray runs along the projector axis; edges splay symmetrically around it.
            Assert.AreEqual(0f, Vector3.Angle(centre, projectorGo.transform.forward), 0.5f);
            Assert.Less(left.x, centre.x);
            Assert.Greater(right.x, centre.x);
            Assert.AreEqual(Vector3.Angle(centre, left), Vector3.Angle(centre, right), 0.5f);
        }
    }
}
