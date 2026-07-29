using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Spec §4: produces the "reference scene image" RT for one output channel (one Unity
    /// display index of the content) that projectors and monitor surfaces consume.
    ///
    /// IMPORTANT — capture source determinism:
    /// - <see cref="CaptureMethod.ContentCamera"/> (default) renders the content scene's
    ///   own camera into the RT. This is deterministic: the projection is ALWAYS the
    ///   reference scene, regardless of which display the Editor Game View is showing or
    ///   where the observer camera is. (Cost: Screen Space - Overlay UI / OnGUI are not
    ///   captured, since those bypass camera target textures.)
    /// - <see cref="CaptureMethod.ScreenCaptureApi"/> / <see cref="CaptureMethod.ReadPixels"/>
    ///   grab "the screen" (the Game View's currently shown display). These DO include
    ///   Overlay UI, but in the Editor they capture whatever display is shown — so if you
    ///   view the observer (Display 2), the observer image gets projected. Use only when a
    ///   dedicated content display is the shown one.
    /// </summary>
    public sealed class ContentScreenCaptureSource : MonoBehaviour, ICameraFrameSource
    {
        public enum CaptureMethod
        {
            ContentCamera,
            ScreenCaptureApi,
            ReadPixels
        }

        [Tooltip("ContentCamera = deterministic (projects the reference scene regardless of " +
                 "the observer / shown display). ScreenCaptureApi/ReadPixels capture the shown " +
                 "screen and can echo the observer.")]
        [SerializeField] private CaptureMethod method = CaptureMethod.ContentCamera;
        [Tooltip("Content scene camera to render for ContentCamera mode. Auto-resolves from " +
                 "Display Index when left empty.")]
        [SerializeField] private Camera contentCamera;
        [Tooltip("Logical output channel: the Unity display index (0 = Display 1) whose " +
                 "content camera this source captures. Multi-display content gets one " +
                 "capture source per display (see OutputRouter).")]
        [ContentDisplay, SerializeField] private int displayIndex;
        [SerializeField] private Vector2Int canvasResolution = new Vector2Int(1920, 1080);
        [Tooltip("Correct the vertical flip of ScreenCapture.CaptureScreenshotIntoRenderTexture. " +
                 "On D3D/Windows the captured image is bottom-up, so this should be ON to keep the " +
                 "canvas upright. Only applies to the ScreenCaptureApi path; ContentCamera is upright.")]
        [SerializeField] private bool flipVertically = true;

        private RenderTexture canvasRt;
        private RenderTexture captureRt;
        private Texture2D readbackTexture;
        private Coroutine captureLoop;
        private int capturedFrameCount;
        private double lastCaptureTime;
        private Vector2Int lastSourceSize;
        private string lastError = string.Empty;

        public RenderTexture CanvasTexture => canvasRt;
        public int DisplayIndex { get => displayIndex; set => displayIndex = value; }
        public Vector2Int CanvasResolution { get => canvasResolution; set => canvasResolution = value; }
        public CaptureMethod Method { get => method; set => method = value; }
        public bool FlipVertically { get => flipVertically; set => flipVertically = value; }
        public int CapturedFrameCount => capturedFrameCount;
        public Vector2Int LastSourceSize => lastSourceSize;
        public string LastError => lastError;

        public int Width => canvasResolution.x;
        public int Height => canvasResolution.y;

        public bool TryGetFrameTexture(out Texture texture, out double timestamp)
        {
            texture = canvasRt;
            timestamp = lastCaptureTime;
            return canvasRt != null && capturedFrameCount > 0;
        }

        private void OnEnable()
        {
            EnsureTargets();
            captureLoop = StartCoroutine(CaptureLoop());
        }

        private void OnDisable()
        {
            if (captureLoop != null)
            {
                StopCoroutine(captureLoop);
                captureLoop = null;
            }
            ReleaseTargets();
        }

        private void EnsureTargets()
        {
            if (canvasRt != null && (canvasRt.width != canvasResolution.x || canvasRt.height != canvasResolution.y))
            {
                canvasRt.Release();
                Destroy(canvasRt);
                canvasRt = null;
            }
            if (captureRt != null && (captureRt.width != canvasResolution.x || captureRt.height != canvasResolution.y))
            {
                captureRt.Release();
                Destroy(captureRt);
                captureRt = null;
            }
            if (canvasRt == null)
            {
                // 24-bit depth: the ContentCamera path renders a camera into this RT via the
                // render graph, which requires the output texture to have a depth buffer.
                canvasRt = new RenderTexture(canvasResolution.x, canvasResolution.y, 24, RenderTextureFormat.ARGB32)
                {
                    name = "SpatialKit Canvas RT"
                };
                canvasRt.Create();
            }

            if (captureRt == null)
            {
                captureRt = new RenderTexture(canvasResolution.x, canvasResolution.y, 0, RenderTextureFormat.ARGB32)
                {
                    name = "SpatialKit Capture RT"
                };
                captureRt.Create();
            }
        }

        private void ReleaseTargets()
        {
            if (canvasRt != null)
            {
                canvasRt.Release();
                Destroy(canvasRt);
                canvasRt = null;
            }
            if (captureRt != null)
            {
                captureRt.Release();
                Destroy(captureRt);
                captureRt = null;
            }
            if (readbackTexture != null)
            {
                Destroy(readbackTexture);
                readbackTexture = null;
            }
        }

        private IEnumerator CaptureLoop()
        {
            WaitForEndOfFrame wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                try
                {
                    CaptureOnce();
                }
                catch (Exception exception)
                {
                    lastError = exception.Message;
                }
            }
        }

        private void LateUpdate()
        {
            // The venue drives the content display's aspect: pin the content camera to the
            // channel resolution's aspect (16:9 by default) so the projected image, the
            // projector's aspect, the pointer injection AND the content's own
            // ScreenToWorldPoint all share one coordinate system — regardless of the Editor
            // Game View window shape. In a build the display is already this aspect, so this
            // is a no-op there. Content logic is untouched (this is a display-output setting).
            if (method != CaptureMethod.ContentCamera || canvasResolution.y <= 0)
            {
                return;
            }
            Camera cam = contentCamera != null
                ? contentCamera
                : ContentCameraResolver.Find(displayIndex, gameObject.scene);
            if (cam != null)
            {
                cam.aspect = (float)canvasResolution.x / canvasResolution.y;
            }
        }

        private void CaptureOnce()
        {
            EnsureTargets();

            switch (method)
            {
                case CaptureMethod.ContentCamera:
                    if (!CaptureViaContentCamera())
                    {
                        return; // no content camera yet (scene still loading)
                    }
                    break;
                case CaptureMethod.ScreenCaptureApi:
                    lastSourceSize = new Vector2Int(Screen.width, Screen.height);
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(captureRt);
                    BlitToCanvas(captureRt);
                    break;
                case CaptureMethod.ReadPixels:
                    lastSourceSize = new Vector2Int(Screen.width, Screen.height);
                    CaptureViaReadPixels();
                    break;
            }

            capturedFrameCount++;
            lastCaptureTime = Time.unscaledTimeAsDouble;
        }

        private bool CaptureViaContentCamera()
        {
            Camera cam = contentCamera != null
                ? contentCamera
                : ContentCameraResolver.Find(displayIndex, gameObject.scene);
            if (cam == null)
            {
                return false;
            }

            // Render the reference scene's camera straight into the canvas RT. This is
            // independent of the observer and of which display the Game View shows. Report
            // the actual RT size (cam.pixelWidth here reflects the render context, not the
            // content camera's own display aspect).
            lastSourceSize = new Vector2Int(canvasRt.width, canvasRt.height);

            UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest();
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                request.destination = canvasRt;
                RenderPipeline.SubmitRenderRequest(cam, request);
            }
            else
            {
                // Fallback (non-URP / unsupported): immediate render to the RT.
                RenderTexture previous = cam.targetTexture;
                cam.targetTexture = canvasRt;
                cam.Render();
                cam.targetTexture = previous;
            }
            return true;
        }

        private void CaptureViaReadPixels()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (readbackTexture == null || readbackTexture.width != width || readbackTexture.height != height)
            {
                if (readbackTexture != null)
                {
                    Destroy(readbackTexture);
                }
                readbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            }

            readbackTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            readbackTexture.Apply(false);
            Graphics.Blit(readbackTexture, canvasRt);
        }

        private void BlitToCanvas(Texture source)
        {
            if (flipVertically)
            {
                Graphics.Blit(source, canvasRt, new Vector2(1f, -1f), new Vector2(0f, 1f));
            }
            else
            {
                Graphics.Blit(source, canvasRt);
            }
        }
    }
}
