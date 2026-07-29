using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// Catalogue for an LCD/OLED monitor model. Unlike a projector, a monitor is a
    /// self-emissive panel with a fixed physical size — no throw ratio, lens shift or
    /// focus. The panel dimensions in metres derive from the diagonal + aspect.
    /// </summary>
    [CreateAssetMenu(
        fileName = "MonitorDeviceProfile",
        menuName = "Projection Spatial Kit/Monitor Device Profile")]
    public sealed class MonitorDeviceProfile : DisplayDeviceProfile
    {
        [Header("Panel physical size")]
        [Tooltip("Panel diagonal in inches (catalogue value, e.g. 55).")]
        public float diagonalInches = 55f;
        [Tooltip("Bezel width around the panel, millimetres (drawn as housing).")]
        public float bezelMillimeters = 12f;

        [Header("Emission")]
        [Tooltip("Rated brightness in nits (cd/m²); reference for the emissive intensity.")]
        public float brightnessNits = 450f;

        [Header("Input")]
        [Tooltip("Whether this model has a touch panel (enables touch mapping on the surface).")]
        public bool isTouchPanel;

        private const float MetresPerInch = 0.0254f;

        /// <summary>Visible panel size in metres (width, height) from diagonal + aspect.</summary>
        public Vector2 PanelSizeMetres
        {
            get
            {
                float aspect = ImageAspect;
                float diagonalMetres = Mathf.Max(0.01f, diagonalInches) * MetresPerInch;
                float height = diagonalMetres / Mathf.Sqrt(1f + aspect * aspect);
                return new Vector2(height * aspect, height);
            }
        }
    }
}
