using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace ProjectionSpatialKit
{
    public sealed class ProjectionCalibrationOverlay : MonoBehaviour
    {
        [SerializeField] private OpenCvProjectionCalibrator projectionCalibrator;
        [SerializeField] private RawImage cameraPreview;
        [SerializeField] private bool autoCreateUi = true;
        [SerializeField] private bool showWhenCalibrationDisabled = true;
        [SerializeField] private float handleSize = 18f;

        private CalibrationHandle topLeftHandle;
        private CalibrationHandle topRightHandle;
        private CalibrationHandle bottomRightHandle;
        private CalibrationHandle bottomLeftHandle;
        private RectTransform previewRect;
        private Image[] lines;
        private Text label;
        private Font uiFont;
        private RectTransform handleRootRect;

        private void Awake()
        {
            projectionCalibrator ??= FindFirstObjectByType<OpenCvProjectionCalibrator>();
        }

        private void Start()
        {
            if (!autoCreateUi)
            {
                return;
            }

            EnsureEventSystem();
            ResolveCameraPreview();
            if (projectionCalibrator == null || cameraPreview == null)
            {
                return;
            }

            CreateRuntimeUi();
            SyncHandlesFromCalibrator();
            ApplyToCalibrator();
        }

        private void Update()
        {
            bool visible = showWhenCalibrationDisabled
                || projectionCalibrator == null
                || projectionCalibrator.UseCalibration;
            SetVisible(visible);
            UpdateLines();
        }

        private void ResolveCameraPreview()
        {
            if (cameraPreview != null)
            {
                return;
            }

            GameObject previewObject = GameObject.Find("Camera Preview");
            if (previewObject != null)
            {
                cameraPreview = previewObject.GetComponent<RawImage>();
            }
        }

        private void CreateRuntimeUi()
        {
            previewRect = cameraPreview.rectTransform;
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            GameObject root = new GameObject("Projection Calibration Handles");
            root.transform.SetParent(previewRect, false);
            handleRootRect = root.AddComponent<RectTransform>();
            handleRootRect.anchorMin = Vector2.zero;
            handleRootRect.anchorMax = Vector2.one;
            handleRootRect.offsetMin = Vector2.zero;
            handleRootRect.offsetMax = Vector2.zero;

            lines = new Image[4];
            for (int i = 0; i < lines.Length; i++)
            {
                Image line = CreateImage("Calibration Edge " + i, root.transform, new Color(0f, 0.65f, 1f, 0.75f));
                line.raycastTarget = false;
                lines[i] = line;
            }

            topLeftHandle = CreateHandle("TL", root.transform, projectionCalibrator.TopLeft);
            topRightHandle = CreateHandle("TR", root.transform, projectionCalibrator.TopRight);
            bottomRightHandle = CreateHandle("BR", root.transform, projectionCalibrator.BottomRight);
            bottomLeftHandle = CreateHandle("BL", root.transform, projectionCalibrator.BottomLeft);

            GameObject labelGo = new GameObject("Calibration Label");
            labelGo.transform.SetParent(root.transform, false);
            label = labelGo.AddComponent<Text>();
            label.font = uiFont;
            label.fontSize = 11;
            label.alignment = TextAnchor.UpperLeft;
            label.color = new Color(0f, 0.15f, 0.2f, 0.95f);
            label.text = "Drag corners to projection bounds";
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.anchoredPosition = new Vector2(4f, -4f);
            labelRect.sizeDelta = new Vector2(0f, 18f);
        }

        private CalibrationHandle CreateHandle(string handleName, Transform parent, Vector2 normalizedTopLeft)
        {
            Image image = CreateImage("Calibration " + handleName, parent, new Color(0f, 0.65f, 1f, 0.95f));
            image.raycastTarget = true;
            RectTransform rect = image.rectTransform;
            rect.sizeDelta = new Vector2(handleSize, handleSize);

            GameObject textGo = new GameObject("Label");
            textGo.transform.SetParent(image.transform, false);
            Text text = textGo.AddComponent<Text>();
            text.font = uiFont;
            text.fontSize = 10;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = handleName;
            Stretch(text.rectTransform);

            CalibrationHandle handle = image.gameObject.AddComponent<CalibrationHandle>();
            handle.Initialize(handleRootRect, normalizedTopLeft, OnHandleDragged);
            return handle;
        }

        private void OnHandleDragged()
        {
            ApplyToCalibrator();
        }

        private void SyncHandlesFromCalibrator()
        {
            topLeftHandle.SetNormalizedTopLeft(projectionCalibrator.TopLeft);
            topRightHandle.SetNormalizedTopLeft(projectionCalibrator.TopRight);
            bottomRightHandle.SetNormalizedTopLeft(projectionCalibrator.BottomRight);
            bottomLeftHandle.SetNormalizedTopLeft(projectionCalibrator.BottomLeft);
        }

        private void ApplyToCalibrator()
        {
            if (projectionCalibrator == null
                || topLeftHandle == null
                || topRightHandle == null
                || bottomRightHandle == null
                || bottomLeftHandle == null)
            {
                return;
            }

            projectionCalibrator.SetCorners(
                topLeftHandle.NormalizedTopLeft,
                topRightHandle.NormalizedTopLeft,
                bottomRightHandle.NormalizedTopLeft,
                bottomLeftHandle.NormalizedTopLeft);
        }

        private void UpdateLines()
        {
            if (lines == null || topLeftHandle == null)
            {
                return;
            }

            SetLine(lines[0].rectTransform, topLeftHandle.RectTransform.anchoredPosition, topRightHandle.RectTransform.anchoredPosition);
            SetLine(lines[1].rectTransform, topRightHandle.RectTransform.anchoredPosition, bottomRightHandle.RectTransform.anchoredPosition);
            SetLine(lines[2].rectTransform, bottomRightHandle.RectTransform.anchoredPosition, bottomLeftHandle.RectTransform.anchoredPosition);
            SetLine(lines[3].rectTransform, bottomLeftHandle.RectTransform.anchoredPosition, topLeftHandle.RectTransform.anchoredPosition);
        }

        private void SetVisible(bool visible)
        {
            if (topLeftHandle == null)
            {
                return;
            }

            topLeftHandle.gameObject.SetActive(visible);
            topRightHandle.gameObject.SetActive(visible);
            bottomRightHandle.gameObject.SetActive(visible);
            bottomLeftHandle.gameObject.SetActive(visible);
            if (label != null)
            {
                label.gameObject.SetActive(visible);
            }

            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i].gameObject.SetActive(visible);
                }
            }
        }

        private static void SetLine(RectTransform line, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            line.anchorMin = new Vector2(0.5f, 0.5f);
            line.anchorMax = new Vector2(0.5f, 0.5f);
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = a;
            line.sizeDelta = new Vector2(delta.magnitude, 2f);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemGo.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemGo.AddComponent<StandaloneInputModule>();
#endif
        }

        private sealed class CalibrationHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
        {
            private RectTransform previewRect;
            private RectTransform rectTransform;
            private System.Action onDragged;

            public RectTransform RectTransform => rectTransform;
            public Vector2 NormalizedTopLeft { get; private set; }

            public void Initialize(RectTransform container, Vector2 normalizedTopLeft, System.Action draggedCallback)
            {
                previewRect = container;
                rectTransform = GetComponent<RectTransform>();
                onDragged = draggedCallback;
                SetNormalizedTopLeft(normalizedTopLeft);
            }

            public void SetNormalizedTopLeft(Vector2 normalizedTopLeft)
            {
                NormalizedTopLeft = Clamp01(normalizedTopLeft);
                Vector2 normalizedUi = new Vector2(NormalizedTopLeft.x, 1f - NormalizedTopLeft.y);
                rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.anchoredPosition = NormalizedToLocal(normalizedUi);
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                OnDrag(eventData);
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (previewRect == null)
                {
                    return;
                }

                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    previewRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 local))
                {
                    return;
                }

                Rect rect = previewRect.rect;
                float x = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
                float yUi = Mathf.InverseLerp(rect.yMin, rect.yMax, local.y);
                SetNormalizedTopLeft(new Vector2(x, 1f - yUi));
                onDragged?.Invoke();
            }

            private Vector2 NormalizedToLocal(Vector2 normalizedUi)
            {
                Rect rect = previewRect.rect;
                return new Vector2(
                    Mathf.Lerp(rect.xMin, rect.xMax, normalizedUi.x),
                    Mathf.Lerp(rect.yMin, rect.yMax, normalizedUi.y));
            }

            private static Vector2 Clamp01(Vector2 value)
            {
                return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
            }
        }
    }
}
