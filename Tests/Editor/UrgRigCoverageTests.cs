using NUnit.Framework;
using UnityEngine;

namespace ProjectionSpatialKit.Tests
{
    public sealed class UrgRigCoverageTests
    {
        private GameObject go;
        private UrgRig rig;

        [SetUp]
        public void SetUp()
        {
            // Identity transform: scan plane = world XZ plane at y=0, angle 0 = +Z,
            // defaults: fan 180°, range 6 m, plane thickness 0.15 m.
            go = new GameObject("urg under test");
            rig = go.AddComponent<UrgRig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Coverage_PointAhead_IsInside()
        {
            Assert.IsTrue(rig.IsWorldPointInScanCoverage(new Vector3(0f, 0f, 2f)));
        }

        [Test]
        public void Coverage_PointNearPlaneAtAnAngle_IsInside()
        {
            Assert.IsTrue(rig.IsWorldPointInScanCoverage(new Vector3(1.5f, 0.05f, 1.5f)));
        }

        [Test]
        public void Coverage_PointOffThePlane_IsOutside()
        {
            // 1 m above the sheet: the infrared never reaches there.
            Assert.IsFalse(rig.IsWorldPointInScanCoverage(new Vector3(0f, 1f, 2f)));
        }

        [Test]
        public void Coverage_PointBeyondRange_IsOutside()
        {
            Assert.IsFalse(rig.IsWorldPointInScanCoverage(new Vector3(0f, 0f, 7f)));
        }

        [Test]
        public void Coverage_PointBehindTheFan_IsOutside()
        {
            // atan2(0.5, -1) ≈ 153° > 90° half-angle.
            Assert.IsFalse(rig.IsWorldPointInScanCoverage(new Vector3(0.5f, 0f, -1f)));
        }
    }
}
