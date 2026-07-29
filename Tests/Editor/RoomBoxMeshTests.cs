using NUnit.Framework;
using UnityEngine;

namespace ProjectionSpatialKit.Tests
{
    /// <summary>
    /// The room is an inside-out cube: every triangle must face INTO the room, and the walls
    /// must be dense enough that URP's per-vertex additional lighting reads as a smooth
    /// gradient instead of showing the triangle diagonals as seams.
    /// </summary>
    public sealed class RoomBoxMeshTests
    {
        private GameObject go;
        private Mesh mesh;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("room", typeof(MeshFilter), typeof(MeshCollider), typeof(RoomBox));
            mesh = go.GetComponent<MeshFilter>().sharedMesh;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
        }

        [Test]
        public void EveryTriangle_FacesIntoTheRoom()
        {
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            int[] triangles = mesh.triangles;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                // Winding must agree with the shading normal, or the face is culled from
                // inside the room (this once silently flipped only the floor and ceiling).
                Vector3 geometric = Vector3.Cross(b - a, c - a).normalized;
                Assert.Greater(Vector3.Dot(geometric, normals[triangles[i]]), 0.9f,
                    $"triangle {i / 3} is wound the wrong way round");

                // Inward: the unit cube is centred on the origin, so the normal must point
                // back toward the centre, not away from it.
                Assert.Less(Vector3.Dot(normals[triangles[i]], a), 0f,
                    $"triangle {i / 3} has an outward normal");
            }
        }

        [Test]
        public void Collider_SharesTheRenderedMesh()
        {
            Assert.AreSame(mesh, go.GetComponent<MeshCollider>().sharedMesh);
        }

        [Test]
        public void Walls_AreSubdividedForPerVertexLighting()
        {
            // 6 faces × a grid each. A plain cube (12 triangles) makes the per-vertex additional
            // light interpolate across half-wall-sized triangles, which shows up as diagonals.
            Assert.Greater(mesh.triangles.Length / 3, 6 * 2 * 8 * 8);
        }
    }
}
