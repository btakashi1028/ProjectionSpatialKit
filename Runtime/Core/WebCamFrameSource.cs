using System;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectionSpatialKit
{
    public sealed class WebCamFrameSource : MonoBehaviour, ICameraFrameSource
    {
        [SerializeField] private string requestedDeviceName = "Insta360 Link 2";
        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;
        [SerializeField] private int requestedFps = 30;
        [SerializeField] private bool playOnStart = true;
        [SerializeField] private RawImage previewTarget;

        private WebCamTexture webCamTexture;
        private Color32[] frameBuffer;

        public bool IsRunning => webCamTexture != null && webCamTexture.isPlaying;
        public bool HasFrame => webCamTexture != null && webCamTexture.didUpdateThisFrame;
        public Texture PreviewTexture => webCamTexture;
        public int Width => webCamTexture != null && webCamTexture.width > 16 ? webCamTexture.width : requestedWidth;
        public int Height => webCamTexture != null && webCamTexture.height > 16 ? webCamTexture.height : requestedHeight;
        public string ActiveDeviceName => webCamTexture != null ? webCamTexture.deviceName : string.Empty;

        private void Start()
        {
            if (playOnStart)
            {
                StartCamera();
            }
        }

        private void OnDestroy()
        {
            StopCamera();
        }

        public bool TryGetFrameTexture(out Texture texture, out double timestamp)
        {
            if (!IsRunning)
            {
                StartCamera();
            }

            texture = webCamTexture;
            timestamp = Time.unscaledTimeAsDouble;
            return webCamTexture != null;
        }

        public bool StartCamera()
        {
            if (IsRunning)
            {
                return true;
            }

            string deviceName = ResolveDeviceName();
            if (string.IsNullOrEmpty(deviceName) && WebCamTexture.devices.Length == 0)
            {
                Debug.LogWarning("ProjectionSpatialKit: No webcam devices found.");
                return false;
            }

            webCamTexture = string.IsNullOrEmpty(deviceName)
                ? new WebCamTexture(requestedWidth, requestedHeight, requestedFps)
                : new WebCamTexture(deviceName, requestedWidth, requestedHeight, requestedFps);

            webCamTexture.Play();
            ApplyPreviewTexture();
            return true;
        }

        public void StopCamera()
        {
            if (webCamTexture == null)
            {
                return;
            }

            if (webCamTexture.isPlaying)
            {
                webCamTexture.Stop();
            }

            Destroy(webCamTexture);
            webCamTexture = null;
            frameBuffer = null;
            ApplyPreviewTexture();
        }

        public Color32[] CopyLatestFrame()
        {
            if (webCamTexture == null)
            {
                return Array.Empty<Color32>();
            }

            int width = Width;
            int height = Height;
            int pixelCount = width * height;
            if (frameBuffer == null || frameBuffer.Length != pixelCount)
            {
                frameBuffer = new Color32[pixelCount];
            }

            webCamTexture.GetPixels32(frameBuffer);
            return frameBuffer;
        }

        private string ResolveDeviceName()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(requestedDeviceName))
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i].name == requestedDeviceName)
                    {
                        return requestedDeviceName;
                    }
                }

                Debug.LogWarning($"ProjectionSpatialKit: Requested webcam '{requestedDeviceName}' was not found. Falling back to '{devices[0].name}'.");
            }

            return devices[0].name;
        }

        private void ApplyPreviewTexture()
        {
            if (previewTarget != null)
            {
                previewTarget.texture = webCamTexture;
            }
        }
    }
}
