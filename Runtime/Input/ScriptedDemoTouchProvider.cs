using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Scripted UV drags as a touch point provider — for unattended verification of the
    /// whole Tier 0 chain (provider → hub → virtual Touchscreen/Mouse → content) without
    /// an operator. Disabled by default; enable playOnStart or call <see cref="Play"/>.
    /// </summary>
    public sealed class ScriptedDemoTouchProvider : MonoBehaviour, ITouchPointProvider
    {
        [Serializable]
        public struct DragStep
        {
            public Vector2 fromUV;
            public Vector2 toUV;
            public float duration;
            [Tooltip("Pause after this drag, seconds (touch released).")]
            public float pauseAfter;
        }

        [SerializeField] private bool playOnStart;
        [SerializeField] private float startDelay = 1.5f;
        [Tooltip("Logical output channel the drags target (Unity display index).")]
        [ContentDisplay, SerializeField] private int displayIndex;
        [SerializeField] private List<DragStep> steps = new List<DragStep>
        {
            new DragStep { fromUV = new Vector2(0.32f, 0.62f), toUV = new Vector2(0.43f, 0.55f), duration = 0.6f, pauseAfter = 0.8f },
            new DragStep { fromUV = new Vector2(0.66f, 0.50f), toUV = new Vector2(0.58f, 0.42f), duration = 0.6f, pauseAfter = 0.4f }
        };

        public bool IsPlaying { get; private set; }

        private readonly List<SurfaceTouchPoint> points = new List<SurfaceTouchPoint>();
        private int lastRefreshFrame = -1;
        private double playStartTime;

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
        }

        /// <summary>Starts (or restarts) the drag sequence after the start delay.</summary>
        public void Play()
        {
            playStartTime = Time.unscaledTimeAsDouble + startDelay;
            IsPlaying = true;
            Debug.Log("[SpatialKit] scripted demo drags scheduled");
        }

        public IReadOnlyList<SurfaceTouchPoint> GetTouchPoints()
        {
            if (Time.frameCount != lastRefreshFrame)
            {
                lastRefreshFrame = Time.frameCount;
                Refresh();
            }
            return points;
        }

        private void Refresh()
        {
            points.Clear();
            if (!IsPlaying)
            {
                return;
            }

            double elapsed = Time.unscaledTimeAsDouble - playStartTime;
            if (elapsed < 0)
            {
                return; // still in the start delay
            }

            foreach (DragStep step in steps)
            {
                float duration = Mathf.Max(0.01f, step.duration);
                if (elapsed < duration)
                {
                    Vector2 uv = Vector2.Lerp(step.fromUV, step.toUV, (float)(elapsed / duration));
                    points.Add(new SurfaceTouchPoint
                    {
                        id = 0,
                        uv = uv,
                        confidence = 1f,
                        timestamp = Time.unscaledTimeAsDouble,
                        displayIndex = displayIndex
                    });
                    return;
                }
                elapsed -= duration;

                if (elapsed < step.pauseAfter)
                {
                    return; // between drags, touch released
                }
                elapsed -= step.pauseAfter;
            }

            IsPlaying = false;
            Debug.Log("[SpatialKit] scripted demo drags finished");
        }
    }
}
