using UnityEngine;

namespace ProjectionSpatialKit.Samples.TouchRippleContent
{
    /// <summary>
    /// An expanding, fading ring drawn with a LineRenderer at a touch point. Generated in
    /// in code — no prefab, sprite or material assets required.
    /// </summary>
    public sealed class RippleRing : MonoBehaviour
    {
        private const int Segments = 48;
        private const float Duration = 0.8f;
        private const float MaxRadius = 1.6f;

        private LineRenderer line;
        private float bornAt;

        public static RippleRing Spawn(Vector3 worldPosition)
        {
            GameObject go = new GameObject("Ripple");
            go.transform.position = worldPosition;
            RippleRing ripple = go.AddComponent<RippleRing>();
            return ripple;
        }

        private void Awake()
        {
            bornAt = Time.time;
            line = gameObject.AddComponent<LineRenderer>();
            line.loop = true;
            line.positionCount = Segments;
            line.useWorldSpace = false;
            line.startWidth = 0.06f;
            line.endWidth = 0.06f;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                line.material = new Material(unlit);
            }
        }

        private void Update()
        {
            float t = (Time.time - bornAt) / Duration;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            float radius = Mathf.Lerp(0.15f, MaxRadius, t);
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            if (line.material != null)
            {
                line.material.SetColor("_BaseColor", new Color(0.5f, 0.9f, 1f, 1f - t));
            }
        }
    }
}
