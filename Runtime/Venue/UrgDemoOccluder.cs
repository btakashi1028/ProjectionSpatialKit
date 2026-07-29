using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// A stand-in for a visitor's hand: a small collider that oscillates through the URG
    /// scan plane, so Physical-mode detection (appear → track → occlude → lost) can be
    /// demonstrated and verified unattended. Sits on the venue rig; harmless when the URG
    /// is in Ideal mode (it only matters to Physics raycasts).
    /// </summary>
    public sealed class UrgDemoOccluder : MonoBehaviour
    {
        [Tooltip("Local-space travel of the oscillation, metres.")]
        [SerializeField] private Vector3 travel = new Vector3(0.8f, 0f, 0f);
        [SerializeField] private float periodSeconds = 4f;

        private Vector3 startPosition;

        private void OnEnable()
        {
            startPosition = transform.localPosition;
        }

        private void OnDisable()
        {
            transform.localPosition = startPosition;
        }

        private void Update()
        {
            float phase = Mathf.Sin(Time.time * (Mathf.PI * 2f / Mathf.Max(0.1f, periodSeconds)));
            transform.localPosition = startPosition + travel * phase;
        }
    }
}
