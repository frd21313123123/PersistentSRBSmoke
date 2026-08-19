using System;
using System.Collections.Generic;
using UnityEngine;

namespace PersistentSRBSmoke
{
    /// <summary>
    /// Generates the geometry used by the dependency-free volumetric fallback and by the
    /// raymarched renderer proxy. The fallback is a genuine 3D slice volume: density cards are
    /// distributed through the cloudlet interior on three orthogonal axes instead of all being
    /// stacked at the particle centre.
    /// </summary>
    internal static class VolumetricCloudletMesh
    {
        public static Mesh CreateSliceVolume(int slicesPerAxis)
        {
            slicesPerAxis = Mathf.Clamp(slicesPerAxis, 3, 9);
            if ((slicesPerAxis & 1) == 0)
                slicesPerAxis += 1;

            Vector3[] normals = { Vector3.right, Vector3.up, Vector3.forward };
            int quadCount = normals.Length * slicesPerAxis;
            var vertices = new List<Vector3>(quadCount * 4);
            var normalsOut = new List<Vector3>(quadCount * 4);
            var uvs = new List<Vector2>(quadCount * 4);
            var triangles = new List<int>(quadCount * 6);

            for (int axis = 0; axis < normals.Length; axis++)
            {
                Vector3 normal = normals[axis];
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
                    float offset = t * 0.43f;
                    float crossSection = Mathf.Sqrt(Mathf.Max(0.08f, 1f - t * t));
                    float halfSize = 0.53f * crossSection;
                    Vector3 centre = normal * offset;

                    // Rotate UVs per slice/axis so repeating density details do not line up into
                    // obvious straight bands when several slices overlap.
                    int uvRotation = (axis * 2 + slice) & 3;
                    Vector2[] quadUv = RotatedUvs(uvRotation);

                    int baseIndex = vertices.Count;
                    vertices.Add(centre - axisA * halfSize - axisB * halfSize);
                    vertices.Add(centre + axisA * halfSize - axisB * halfSize);
                    vertices.Add(centre + axisA * halfSize + axisB * halfSize);
                    vertices.Add(centre - axisA * halfSize + axisB * halfSize);

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

                    // Double-sided geometry without relying on material Cull state.
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 1);
                    triangles.Add(baseIndex + 0);
                    triangles.Add(baseIndex + 3);
                    triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex + 0);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "PersistentSRBSmoke.VolumetricSliceCloudlet";
            mesh.SetVertices(vertices);
            mesh.SetNormals(normalsOut);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
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
