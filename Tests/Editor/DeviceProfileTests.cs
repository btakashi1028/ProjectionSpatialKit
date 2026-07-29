using NUnit.Framework;
using UnityEngine;

namespace ProjectionSpatialKit.Tests
{
    public sealed class DeviceProfileTests
    {
        [Test]
        public void ProjectorProfile_ThrowRatioAt_LerpsAndClamps()
        {
            var profile = ScriptableObject.CreateInstance<ProjectorDeviceProfile>();
            profile.throwRatioMin = 1.2f;
            profile.throwRatioMax = 1.6f;

            Assert.AreEqual(1.2f, profile.ThrowRatioAt(0f), 1e-5f);
            Assert.AreEqual(1.4f, profile.ThrowRatioAt(0.5f), 1e-5f);
            Assert.AreEqual(1.6f, profile.ThrowRatioAt(1f), 1e-5f);
            Assert.AreEqual(1.2f, profile.ThrowRatioAt(-3f), 1e-5f);
            Assert.AreEqual(1.6f, profile.ThrowRatioAt(9f), 1e-5f);
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void DisplayProfile_ImageAspect_FromResolution()
        {
            var profile = ScriptableObject.CreateInstance<ProjectorDeviceProfile>();
            profile.resolution = new Vector2Int(1920, 1200);
            Assert.AreEqual(1.6f, profile.ImageAspect, 1e-5f);
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void MonitorProfile_PanelSize_From55Inch16To9()
        {
            var profile = ScriptableObject.CreateInstance<MonitorDeviceProfile>();
            profile.resolution = new Vector2Int(1920, 1080);
            profile.diagonalInches = 55f;

            Vector2 size = profile.PanelSizeMetres;
            // 55" 16:9 panel ≈ 1.218 x 0.685 m.
            Assert.AreEqual(1.218f, size.x, 2e-3f);
            Assert.AreEqual(0.685f, size.y, 2e-3f);
            Object.DestroyImmediate(profile);
        }
    }
}
