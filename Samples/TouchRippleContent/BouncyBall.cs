using UnityEngine;

namespace ProjectionSpatialKit.Samples.TouchRippleContent
{
    /// <summary>
    /// A ball spawned at a touch point: 2D physics (gravity + bounce), random colour,
    /// destroyed after its lifetime. Everything is generated in code so the sample requires
    /// no prefab or material assets.
    /// </summary>
    public sealed class BouncyBall : MonoBehaviour
    {
        private const float Radius = 0.35f;
        private static PhysicsMaterial2D bouncyMaterial;
        private static Mesh sphereMesh;

        private float dieAt;
        private Renderer ballRenderer;

        public static BouncyBall Spawn(Vector3 worldPosition, float lifetime)
        {
            // Built from an empty GameObject: a primitive would come with a 3D collider,
            // and Unity refuses AddComponent of a 2D collider while a 3D one is present
            // (Destroy() is deferred, so destroy-then-add within one frame still fails).
            GameObject go = new GameObject("Ball");
            go.AddComponent<MeshFilter>().sharedMesh = GetSphereMesh();
            Renderer renderer = go.AddComponent<MeshRenderer>();

            go.transform.position = new Vector3(worldPosition.x, worldPosition.y, worldPosition.z);
            go.transform.localScale = Vector3.one * (Radius * 2f);

            CircleCollider2D circle = go.AddComponent<CircleCollider2D>();
            circle.radius = 0.5f; // local space (unit sphere scaled by transform)
            bouncyMaterial ??= new PhysicsMaterial2D("SampleBouncy") { bounciness = 0.75f, friction = 0.05f };
            circle.sharedMaterial = bouncyMaterial;

            Rigidbody2D body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 1f;
            body.linearVelocity = new Vector2(Random.Range(-2f, 2f), Random.Range(1f, 3f));

            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                Material material = new Material(unlit);
                material.SetColor("_BaseColor", Color.HSVToRGB(Random.value, 0.75f, 1f));
                renderer.material = material;
            }

            BouncyBall ball = go.AddComponent<BouncyBall>();
            ball.dieAt = Time.time + lifetime;
            ball.ballRenderer = renderer;
            return ball;
        }

        private static Mesh GetSphereMesh()
        {
            if (sphereMesh == null)
            {
                GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh; // built-in asset, persists
                Destroy(temp);
            }
            return sphereMesh;
        }

        private void Update()
        {
            float remaining = dieAt - Time.time;
            if (remaining <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (remaining < 1f && ballRenderer != null)
            {
                ballRenderer.material.SetColor("_BaseColor",
                    ballRenderer.material.GetColor("_BaseColor") * new Color(1f, 1f, 1f, remaining));
                transform.localScale = Vector3.one * (Radius * 2f * remaining);
            }
        }
    }
}
