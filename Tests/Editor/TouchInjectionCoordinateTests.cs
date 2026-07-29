#if ENABLE_INPUT_SYSTEM
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectionSpatialKit.Tests
{
    /// <summary>
    /// The hub must convert a touch's content UV to pixels using the OUTPUT CHANNEL resolution
    /// (the logical display size the content renders at, e.g. 1920×1080), NOT the content
    /// camera's pixelWidth/pixelHeight — which in the Editor is the Game View's current size and
    /// need not match. Basing coordinates on the camera made injected touches land at the wrong
    /// spot whenever the Game View aspect/size differed from the channel.
    /// </summary>
    public sealed class TouchInjectionCoordinateTests
    {
        private GameObject go;

        [SetUp]
        public void SetUp()
        {
            // A capture source starts a coroutine on enable, which the Editor can't run — that is
            // an expected, harmless log here.
            LogAssert.ignoreFailingMessages = true;
            go = new GameObject("hub", typeof(OutputRouter), typeof(TouchInjectionHub));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            LogAssert.ignoreFailingMessages = false;
        }

        private Vector2 Pixel(Vector2 uv, int display)
        {
            TouchInjectionHub hub = go.GetComponent<TouchInjectionHub>();
            SurfaceTouchPoint sp = new SurfaceTouchPoint { uv = uv, displayIndex = display, confidence = 1f };
            MethodInfo method = typeof(TouchInjectionHub).GetMethod(
                "TryGetContentPixel", BindingFlags.NonPublic | BindingFlags.Instance);
            object[] args = { sp, null };
            bool ok = (bool)method.Invoke(hub, args);
            Assert.IsTrue(ok, "a configured channel should always yield a pixel");
            return (Vector2)args[1];
        }

        [Test]
        public void ContentPixel_UsesChannelResolution()
        {
            go.GetComponent<OutputRouter>().GetOrCreateChannel(1, new Vector2Int(1234, 567));

            Vector2 px = Pixel(new Vector2(0.5f, 0.5f), 1);

            Assert.AreEqual(617f, px.x, 0.5f);   // 0.5 * 1234
            Assert.AreEqual(283.5f, px.y, 0.5f); // 0.5 * 567
        }

        [Test]
        public void ContentPixel_ClampsUvIntoTheImage()
        {
            go.GetComponent<OutputRouter>().GetOrCreateChannel(0, new Vector2Int(800, 600));

            Vector2 px = Pixel(new Vector2(1.5f, -0.2f), 0);

            Assert.AreEqual(800f, px.x, 0.5f); // clamped to 1.0 * 800
            Assert.AreEqual(0f, px.y, 0.5f);   // clamped to 0.0 * 600
        }
    }
}
#endif
