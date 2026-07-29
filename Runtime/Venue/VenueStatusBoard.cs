using UnityEngine;
using UnityEngine.UI;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Venue-scene HUD (Screen Space - Overlay on the observer display): capture channels,
    /// content load state, input hub and URG status as quiet monospace telemetry. Overlay
    /// UI is not captured by the ContentCamera render, so this never leaks into the
    /// projected image.
    /// </summary>
    public sealed class VenueStatusBoard : MonoBehaviour
    {
        [SerializeField] private OutputRouter outputRouter;
        [SerializeField] private ContentSceneLoader contentLoader;
        [SerializeField] private TouchInjectionHub injectionHub;
        [SerializeField] private UrgRig urgRig;
        [SerializeField] private Text statusText;

        private void Update()
        {
            if (statusText == null)
            {
                return;
            }
            // Lazy resolution: the simulator façade creates the router/loader/hub at Play
            // start, possibly after this component's Awake — keep looking until found.
            if (outputRouter == null) outputRouter = FindFirstObjectByType<OutputRouter>();
            if (contentLoader == null) contentLoader = FindFirstObjectByType<ContentSceneLoader>();
            if (injectionHub == null) injectionHub = FindFirstObjectByType<TouchInjectionHub>();
            if (urgRig == null) urgRig = FindFirstObjectByType<UrgRig>();

            string body = "PROJECTION SPATIAL KIT · SIM\n";
            body += Row("content", contentLoader != null ? Shorten(contentLoader.State) : "—");

            for (int display = 0; display < 4; display++)
            {
                ContentScreenCaptureSource channel = outputRouter != null ? outputRouter.GetChannel(display) : null;
                if (channel == null)
                {
                    continue;
                }
                body += Row($"ch D{display + 1}",
                    $"{channel.Method}  {channel.LastSourceSize.x}x{channel.LastSourceSize.y}  f{channel.CapturedFrameCount}");
            }

            body += Row("input", injectionHub != null
                ? $"{injectionHub.LastStatus}  active={injectionHub.ActiveTouchCount}"
                : "—");
            if (urgRig != null)
            {
                body += Row("urg", $"{urgRig.Mode}  {urgRig.LastStatus}");
            }
            statusText.text = body;
        }

        private static string Row(string label, string value)
        {
            // Fixed-width label column so the values line up under a monospace font.
            return label.PadRight(8) + " " + value + "\n";
        }

        private static string Shorten(string state)
        {
            int slash = state.LastIndexOf('/');
            return slash >= 0 ? state.Substring(0, state.IndexOf(':') + 2) + state.Substring(slash + 1) : state;
        }
    }
}
