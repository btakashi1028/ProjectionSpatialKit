using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Owns one capture channel (<see cref="ContentScreenCaptureSource"/>) per content
    /// display index and hands them to output devices. Lets a venue reproduce content that
    /// itself outputs to multiple displays — e.g. the wall projection shows the content's
    /// Display 1 while a floor monitor shows its Display 2 — without the devices knowing
    /// how frames are captured.
    /// </summary>
    public sealed class OutputRouter : MonoBehaviour
    {
        [Serializable]
        public struct ChannelConfig
        {
            [Tooltip("Content display index this channel captures (0 = Display 1).")]
            [ContentDisplay] public int displayIndex;
            public Vector2Int resolution;
        }

        [Tooltip("Channels ensured on enable. Usually left empty: the SpatialKitSimulator " +
                 "façade creates channels from its own settings.")]
        [SerializeField] private List<ChannelConfig> channels = new List<ChannelConfig>();

        private readonly Dictionary<int, ContentScreenCaptureSource> sources =
            new Dictionary<int, ContentScreenCaptureSource>();

        private void OnEnable()
        {
            foreach (ChannelConfig config in channels)
            {
                GetOrCreateChannel(config.displayIndex, config.resolution);
            }
        }

        /// <summary>Capture source for a content display index; creates it when missing.</summary>
        public ContentScreenCaptureSource GetOrCreateChannel(int displayIndex, Vector2Int resolution)
        {
            if (sources.TryGetValue(displayIndex, out ContentScreenCaptureSource cached) && cached != null)
            {
                return cached;
            }

            // Adopt a pre-authored child channel (bootstrap-generated scenes wire these statically).
            foreach (ContentScreenCaptureSource existing in GetComponentsInChildren<ContentScreenCaptureSource>(true))
            {
                if (existing.DisplayIndex == displayIndex)
                {
                    sources[displayIndex] = existing;
                    return existing;
                }
            }

            GameObject child = new GameObject($"Channel Display {displayIndex + 1}");
            child.transform.SetParent(transform, false);
            ContentScreenCaptureSource source = child.AddComponent<ContentScreenCaptureSource>();
            source.DisplayIndex = displayIndex;
            if (resolution.x > 0 && resolution.y > 0)
            {
                source.CanvasResolution = resolution;
            }
            sources[displayIndex] = source;
            return source;
        }

        /// <summary>
        /// Scene-wide channel lookup by content display index: the router's channel when a
        /// router exists, otherwise any standalone capture source with that index. Lets
        /// output devices reference their image by a plain display NUMBER instead of an
        /// object reference (single knob in the Inspector).
        /// </summary>
        public static ContentScreenCaptureSource FindChannel(int displayIndex)
        {
            OutputRouter router = FindFirstObjectByType<OutputRouter>();
            if (router != null)
            {
                ContentScreenCaptureSource channel = router.GetChannel(displayIndex);
                if (channel != null)
                {
                    return channel;
                }
            }
            foreach (ContentScreenCaptureSource source in
                     FindObjectsByType<ContentScreenCaptureSource>(FindObjectsSortMode.InstanceID))
            {
                if (source.DisplayIndex == displayIndex)
                {
                    return source;
                }
            }
            return null;
        }

        /// <summary>Existing channel for a display index, or null.</summary>
        public ContentScreenCaptureSource GetChannel(int displayIndex)
        {
            if (sources.TryGetValue(displayIndex, out ContentScreenCaptureSource cached) && cached != null)
            {
                return cached;
            }
            foreach (ContentScreenCaptureSource existing in GetComponentsInChildren<ContentScreenCaptureSource>(true))
            {
                if (existing.DisplayIndex == displayIndex)
                {
                    sources[displayIndex] = existing;
                    return existing;
                }
            }
            return null;
        }
    }
}
