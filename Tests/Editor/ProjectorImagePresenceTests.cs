using NUnit.Framework;
using UnityEngine;

namespace ProjectionSpatialKit.Tests
{
    public sealed class ProjectorImagePresenceTests
    {
        private GameObject projectorGo;
        private VirtualProjectorLight projector;
        private GameObject wall;

        [SetUp]
        public void SetUp()
        {
            projectorGo = new GameObject("projector under test");
            projector = projectorGo.AddComponent<VirtualProjectorLight>();

            // A wall 2 m ahead, facing the projector.
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 0f, 2f);
            wall.transform.localScale = new Vector3(10f, 10f, 0.1f);
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(projectorGo);
            Object.DestroyImmediate(wall);
        }

        [Test]
        public void ImagePresent_OnTheLitWall()
        {
            Assert.IsTrue(projector.IsImagePresentAt(new Vector3(0.4f, 0.3f, 1.95f)));
        }

        [Test]
        public void ImageAbsent_BehindTheWall()
        {
            // The beam stops at the wall; a point behind it is never lit.
            Assert.IsFalse(projector.IsImagePresentAt(new Vector3(0f, 0f, 3f)));
        }

        [Test]
        public void ImageAbsent_BehindAnOccluder()
        {
            // On the projector→target LINE: the ray to (0.4, 0.3, 1.95) passes
            // (0.205, 0.154, 1.0) at z = 1.0.
            GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            occluder.transform.position = new Vector3(0.205f, 0.154f, 1.0f);
            occluder.transform.localScale = new Vector3(0.3f, 0.3f, 0.05f);
            Physics.SyncTransforms();

            Assert.IsFalse(projector.IsImagePresentAt(new Vector3(0.4f, 0.3f, 1.95f)));
            Object.DestroyImmediate(occluder);
        }

        [Test]
        public void ImageAbsent_BeyondThrowRange()
        {
            wall.transform.position = new Vector3(0f, 0f, 50f); // farther than maxThrowDistance
            Physics.SyncTransforms();
            Assert.IsFalse(projector.IsImagePresentAt(new Vector3(0f, 0f, 49.9f)));
        }

        [Test]
        public void ImageAbsent_BehindTheProjector()
        {
            Assert.IsFalse(projector.IsImagePresentAt(new Vector3(0f, 0f, -1f)));
        }
    }
}
