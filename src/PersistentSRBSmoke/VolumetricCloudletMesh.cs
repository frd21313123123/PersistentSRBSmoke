using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Generates the geometry used by the dependency-free volumetric fallback and by the
    /// raymarched renderer proxy. v0.4.2 changes the fallback from one smooth slice-sphere into
    /// several overlapping slice-volumes. The resulting silhouette has large and small lobes,
    /// closer to the cauliflower structure of real SRB exhaust clouds.
    /// </summary>
    internal static class VolumetricCloudletMesh
    {
        private struct Lobe
        {
            public Vector3 Centre;
            public float Radius;
            public Vector3 Stretch;
            public int UvSeed;

            public Lobe(Vector3 centre, float radius, Vector3 stretch, int uvSeed)
            {
                Centre = centre;
                Radius = radius;
                Stretch = stretch;
                UvSeed = uvSeed;
            }
        }

        public static Mesh CreateSliceVolume(int slicesPerAxis)
        {
            slicesPerAxis = Mathf.Clamp(slicesPerAxis, 3, 9);
            if ((slicesPerAxis & 1) == 0)
                slicesPerAxis += 1;

            // The central mass guarantees a continuous core. Satellite lobes break the outline into
            // large billows instead of rendering each emitted particle as an obvious little sphere.
            Lobe[] lobes =
            {
                new Lobe(new Vector3( 0.00f,  0.00f,  0.00f), 0.72f, new Vector3(1.08f, 1.00f, 1.03f), 0),
                new Lobe(new Vector3( 0.28f,  0.13f,  0.02f), 0.54f, new Vector3(1.05f, 0.90f, 1.12f), 1),
                new Lobe(new Vector3(-0.25f,  0.11f,  0.13f), 0.50f, new Vector3(0.92f, 1.10f, 1.00f), 2),
                new Lobe(new Vector3( 0.08f, -0.25f, -0.18f), 0.47f, new Vector3(1.14f, 0.94f, 0.92f), 3),
                new Lobe(new Vector3(-0.05f,  0.30f, -0.17f), 0.43f, new Vector3(0.94f, 1.10f, 1.06f), 5),
                new Lobe(new Vector3( 0.20f, -0.08f,  0.29f), 0.40f, new Vector3(1.08f, 1.03f, 0.92f), 7),
                new Lobe(new Vector3(-0.23f, -0.19f,  0.21f), 0.38f, new Vector3(0.94f, 1.06f, 1.10f), 9)
            };

            int estimatedQuads = lobes.Length * 3 * slicesPerAxis;
            var vertices = new List<Vector3>(estimatedQuads * 4);
            var normals = new List<Vector3>(estimatedQuads * 4);
            var uvs = new List<Vector2>(estimatedQuads * 4);
            var triangles = new List<int>(estimatedQuads * 12);

            for (int i = 0; i < lobes.Length; i++)
                AddLobe(lobes[i], slicesPerAxis, vertices, normals, uvs, triangles);

            Mesh mesh = new Mesh();
            mesh.name = "PersistentSRBSmoke.CauliflowerSliceCloudlet";
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddLobe(
            Lobe lobe,
            int slicesPerAxis,
            List<Vector3> vertices,
            List<Vector3> normalsOut,
            List<Vector2> uvs,
            List<int> triangles)
        {
            Vector3[] sliceNormals = { Vector3.right, Vector3.up, Vector3.forward };

            for (int axis = 0; axis < sliceNormals.Length; axis++)
            {
                Vector3 normal = sliceNormals[axis];
                Vector3 reference = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > 0.88f
                    ? Vector3.right
                    : Vector3.up;
                Vector3 axisA = Vector3.Cross(normal, reference).normalized;
                Vector3 axisB = Vector3.Cross(normal, axisA).normalized;

                for (int slice = 0; slice < slicesPerAxis; slice++)
                {
                    float t = slicesPerAxis <= 1
                        ? 0f
                        : (slice / (float)(slicesPerAxis - 1)) * 2f - 1f;

                    float offset = t * lobe.Radius * 0.62f;
                    float crossSection = Mathf.Sqrt(Mathf.Max(0.05f, 1f - t * t));
                    float halfSize = lobe.Radius * 0.73f * crossSection;

                    Vector3 centre = lobe.Centre + Scale(normal * offset, lobe.Stretch);
                    Vector3 scaledA = Scale(axisA * halfSize, lobe.Stretch);
                    Vector3 scaledB = Scale(axisB * halfSize, lobe.Stretch);

                    // Vary UV orientation by both lobe and slice. This prevents the internal density
                    // noise from lining up across all layers into obvious planar bands.
                    int uvRotation = (lobe.UvSeed + axis * 2 + slice) & 3;
                    Vector2[] quadUv = RotatedUvs(uvRotation);

                    int baseIndex = vertices.Count;
                    vertices.Add(centre - scaledA - scaledB);
                    vertices.Add(centre + scaledA - scaledB);
                    vertices.Add(centre + scaledA + scaledB);
                    vertices.Add(centre - scaledA + scaledB);

                    normalsOut.Add(normal);
                    normalsOut.Add(normal);
                    normalsOut.Add(normal);
                    normalsOut.Add(normal);

                    uvs.Add(quadUv[0]);
                    uvs.Add(quadUv[1]);
                    uvs.Add(quadUv[2]);
                    uvs.Add(quadUv[3]);

                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 3);

                    // Double-sided without relying on a Cull setting that varies between KSP shaders.
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 3);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 0);
                }
            }
        }

        public static Mesh CreateProxyCube()
        {
            Vector3[] v =
            {
                new Vector3(-0.5f,-0.5f,-0.5f), new Vector3( 0.5f,-0.5f,-0.5f),
                new Vector3( 0.5f, 0.5f,-0.5f), new Vector3(-0.5f, 0.5f,-0.5f),
                new Vector3(-0.5f,-0.5f, 0.5f), new Vector3( 0.5f,-0.5f, 0.5f),
                new Vector3( 0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f)
            };
            int[] t =
            {
                0,2,1, 0,3,2,
                4,5,6, 4,6,7,
                0,1,5, 0,5,4,
                3,7,6, 3,6,2,
                1,2,6, 1,6,5,
                0,4,7, 0,7,3
            };

            Mesh mesh = new Mesh();
            mesh.name = "PersistentSRBSmoke.RaymarchProxyCube";
            mesh.vertices = v;
            mesh.triangles = t;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Scale(Vector3 value, Vector3 scale)
        {
            return new Vector3(value.x * scale.x, value.y * scale.y, value.z * scale.z);
        }

        private static Vector2[] RotatedUvs(int rotation)
        {
            Vector2[] baseUv =
            {
                new Vector2(0f,0f), new Vector2(1f,0f),
                new Vector2(1f,1f), new Vector2(0f,1f)
            };
            if (rotation == 0)
                return baseUv;

            Vector2[] result = new Vector2[4];
            for (int i = 0; i < 4; i++)
                result[i] = baseUv[(i + rotation) & 3];
            return result;
        }
    }
}
