using System.Collections.Generic;
using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Shareable, scene-independent simulator settings: which content to load and how its
    /// displays map to capture channels. Assign one to a <see cref="SpatialKitSimulator"/>
    /// to reuse the same setup across venue scenes (and later across venues A/B — this is
    /// the seed of the spec §6 venue profile).
    /// </summary>
    [CreateAssetMenu(
        fileName = "SpatialKitSimulationProfile",
        menuName = "Projection Spatial Kit/Simulation Profile")]
    public sealed class SpatialKitSimulationProfile : ScriptableObject
    {
        [Header("Content")]
        [Tooltip("Scene loaded additively as the unmodified content (project path).")]
        public string contentScenePath = "";
        [Tooltip("URP renderer index the content camera is routed to (the renderer that " +
                 "carries the content's own features). -1 = leave as-is.")]
        public int contentCameraRendererIndex = -1;

        [Header("Output channels")]
        [Tooltip("One capture channel per content display the venue reproduces.")]
        public List<OutputRouter.ChannelConfig> channels = new List<OutputRouter.ChannelConfig>
        {
            new OutputRouter.ChannelConfig { displayIndex = 0, resolution = new Vector2Int(1920, 1080) }
        };

        [Header("Separation")]
        [Tooltip("Layer the venue objects live on; excluded from the content camera.")]
        public string venueLayerName = "SpatialKitVenue";
    }
}
