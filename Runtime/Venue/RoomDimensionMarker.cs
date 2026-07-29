using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// A placeable, rotatable star marker for reading the room's inner dimensions.
    /// Dotted measurement lines are drawn along the marker's local X / Y / Z axes with
    /// the room's width / height / depth in metres (see the editor gizmo). By default
    /// (no rotation) the axes line up with world X / Y / Z. The marker reads the size
    /// live from the referenced <see cref="RoomBox"/>, so resizing the room updates it.
    /// </summary>
    public sealed class RoomDimensionMarker : MonoBehaviour
    {
        [SerializeField] private RoomBox room;
        [SerializeField] private float starSize = 0.09f;
        [SerializeField] private Color xColor = new Color(1f, 0.35f, 0.35f);
        [SerializeField] private Color yColor = new Color(0.4f, 1f, 0.45f);
        [SerializeField] private Color zColor = new Color(0.45f, 0.6f, 1f);
        [SerializeField] private Color starColor = new Color(1f, 0.9f, 0.3f);

        public RoomBox Room => room;
        public float StarSize => starSize;
        public Color XColor => xColor;
        public Color YColor => yColor;
        public Color ZColor => zColor;
        public Color StarColor => starColor;

        /// <summary>Room inner size (metres) to display, or a 1m fallback if unassigned.</summary>
        public Vector3 MeasuredSize => room != null ? room.InnerSize : Vector3.one;
    }
}
