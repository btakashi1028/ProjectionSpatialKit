using UnityEngine;

namespace ProjectionSpatialKit
{
    /// <summary>
    /// A single inside-out cube that represents the venue room. The cube mesh has its
    /// normals and winding inverted, so:
    /// - viewed from outside, the near walls (front faces) are culled and you see the
    ///   interior from any angle;
    /// - the visible inner faces have inward normals, so the projector light correctly
    ///   illuminates them (a plain Cull-Front cube would light them as if from behind).
    ///
    /// Resize the room freely with the standard Transform scale gizmo; the scale is the
    /// room's inner size in metres (the cube is a unit cube).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class RoomBox : MonoBehaviour
    {
        private const string MeshName = "SpatialKitInvertedCube";
        private static Mesh cachedMesh;

        /// <summary>Inner dimensions of the room in metres (width, height, depth).</summary>
        public Vector3 InnerSize => new Vector3(
            Mathf.Abs(transform.lossyScale.x),
            Mathf.Abs(transform.lossyScale.y),
            Mathf.Abs(transform.lossyScale.z));

        private void OnEnable()
        {
            Mesh mesh = GetInvertedCube();
            MeshFilter filter = GetComponent<MeshFilter>();
            // Reference equality, not a name check: a scene saved with an older build of the kit
            // holds a mesh with this same name, and it must still be replaced.
            if (filter.sharedMesh != mesh)
            {
                filter.sharedMesh = mesh;
            }
            // Mesh collider on the inverted cube: an observer ray from outside passes through
            // the near (back-facing) wall and hits the far inner wall — so clicks land where
            // the image is projected, with no separate projection-surface object needed.
            MeshCollider collider = GetComponent<MeshCollider>();
            if (collider.sharedMesh != mesh)
            {
                collider.sharedMesh = mesh;
            }
        }

        // Each wall is a grid, not a single quad. URP commonly evaluates ADDITIONAL lights
        // (our venue point light is one) PER VERTEX, so a wall made of two big triangles gets
        // its lighting interpolated across them and the triangle diagonals show up as seams.
        // Subdividing makes the interpolation dense enough to read as smooth — without
        // touching the host project's URP quality settings.
        private const int SubdivisionsPerFace = 16;

        private static Mesh GetInvertedCube()
        {
            if (cachedMesh != null)
            {
                return cachedMesh;
            }

            var vertices = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var uv = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();

            // Unit cube centred on the origin; the transform's scale is the room's inner size.
            AddFace(Vector3.forward, Vector3.right, vertices, normals, uv, triangles);
            AddFace(Vector3.back, Vector3.left, vertices, normals, uv, triangles);
            AddFace(Vector3.right, Vector3.back, vertices, normals, uv, triangles);
            AddFace(Vector3.left, Vector3.forward, vertices, normals, uv, triangles);
            AddFace(Vector3.up, Vector3.right, vertices, normals, uv, triangles);
            AddFace(Vector3.down, Vector3.right, vertices, normals, uv, triangles);

            // DontSave: the mesh is generated, so it must never be baked into the host's scene file.
            cachedMesh = new Mesh { name = MeshName, hideFlags = HideFlags.DontSave };
            cachedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            cachedMesh.SetVertices(vertices);
            cachedMesh.SetNormals(normals);
            cachedMesh.SetUVs(0, uv);
            cachedMesh.SetTriangles(triangles, 0);
            cachedMesh.RecalculateBounds();
            return cachedMesh;
        }

        /// <summary>
        /// One subdivided face of the unit cube, built INSIDE-OUT: the normal points into the
        /// room and the winding is reversed, so the face is visible (and correctly lit) from
        /// within, and culled when seen from outside.
        /// </summary>
        private static void AddFace(
            Vector3 outward, Vector3 right,
            System.Collections.Generic.List<Vector3> vertices,
            System.Collections.Generic.List<Vector3> normals,
            System.Collections.Generic.List<Vector2> uv,
            System.Collections.Generic.List<int> triangles)
        {
            int baseIndex = vertices.Count;
            Vector3 centre = outward * 0.5f;
            Vector3 inward = -outward;
            // Derive the second tangent instead of hand-writing it, so cross(right, up) == outward
            // holds for every face by construction and the winding below can never come out
            // reversed on some faces (which is exactly how the floor and ceiling once ended up
            // inside-in while the walls were inside-out).
            Vector3 up = Vector3.Cross(outward, right);

            for (int y = 0; y <= SubdivisionsPerFace; y++)
            {
                for (int x = 0; x <= SubdivisionsPerFace; x++)
                {
                    float u = (float)x / SubdivisionsPerFace;
                    float v = (float)y / SubdivisionsPerFace;
                    vertices.Add(centre + right * (u - 0.5f) + up * (v - 0.5f));
                    normals.Add(inward);
                    uv.Add(new Vector2(u, v));
                }
            }

            int stride = SubdivisionsPerFace + 1;
            for (int y = 0; y < SubdivisionsPerFace; y++)
            {
                for (int x = 0; x < SubdivisionsPerFace; x++)
                {
                    int a = baseIndex + y * stride + x;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    // Reversed winding: front-facing when viewed from inside the room.
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
    }
}
