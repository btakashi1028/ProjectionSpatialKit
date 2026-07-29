using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Finds the CONTENT scene camera that renders a given Unity display index. Venue-side
    /// cameras (observer, hardware camera rig) are excluded by scene: content is always
    /// additively loaded, so any camera living in the venue scene is not content.
    /// </summary>
    public static class ContentCameraResolver
    {
        /// <summary>
        /// Camera in a scene other than <paramref name="venueScene"/> whose targetDisplay is
        /// <paramref name="displayIndex"/> and which renders to the display (no RT). For
        /// display 0, Camera.main wins when it qualifies. Null when none exists (yet).
        /// </summary>
        public static Camera Find(int displayIndex, Scene venueScene)
        {
            Camera main = Camera.main;
            if (displayIndex == 0 && Qualifies(main, 0, venueScene))
            {
                return main;
            }

            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                if (Qualifies(all[i], displayIndex, venueScene))
                {
                    return all[i];
                }
            }
            return null;
        }

        private static bool Qualifies(Camera camera, int displayIndex, Scene venueScene)
        {
            return camera != null
                && camera.targetDisplay == displayIndex
                && camera.targetTexture == null
                && camera.gameObject.scene != venueScene;
        }
    }
}
