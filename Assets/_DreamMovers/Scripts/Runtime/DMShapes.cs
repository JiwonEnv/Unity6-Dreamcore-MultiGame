using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DreamMovers
{
    /// <summary>
    /// Placeholder geometry until the Tripo models exist, like the UE /Engine/BasicShapes. Sizes are full sizes in
    /// meters (Unity's cylinder is 2 m tall, so it is scaled for that), and parts are tinted with a property block
    /// on one shared URP Lit material.
    /// </summary>
    public static class DMShapes
    {
        static readonly Dictionary<DMShape, Mesh> Meshes = new Dictionary<DMShape, Mesh>();
        static Material shared;
        static MaterialPropertyBlock block;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");

        public static Mesh GetMesh(DMShape shape)
        {
            if (Meshes.TryGetValue(shape, out Mesh mesh) && mesh != null)
            {
                return mesh;
            }
            mesh = shape == DMShape.Cone ? BuildCone() : CopyPrimitive(shape);
            Meshes[shape] = mesh;
            return mesh;
        }

        static Mesh CopyPrimitive(DMShape shape)
        {
            PrimitiveType type = shape == DMShape.Cylinder ? PrimitiveType.Cylinder : shape == DMShape.Sphere ? PrimitiveType.Sphere : PrimitiveType.Cube;
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(temp);
            return mesh;
        }

        /// <summary>1 m tall, 1 m wide at the base, pivot in the middle, tip up (the UE cone points up too).</summary>
        static Mesh BuildCone()
        {
            const int sides = 16;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sides; ++i)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
                int v = vertices.Count;
                vertices.Add(p0);
                vertices.Add(new Vector3(0f, 0.5f, 0f));
                vertices.Add(p1);
                triangles.Add(v);
                triangles.Add(v + 1);
                triangles.Add(v + 2);
                v = vertices.Count;
                vertices.Add(p0);
                vertices.Add(p1);
                vertices.Add(new Vector3(0f, -0.5f, 0f));
                triangles.Add(v);
                triangles.Add(v + 1);
                triangles.Add(v + 2);
            }
            var mesh = new Mesh { name = "DM Cone" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Local scale that gives the shape this full size (m).</summary>
        public static Vector3 ScaleFor(DMShape shape, Vector3 size)
        {
            return shape == DMShape.Cylinder ? new Vector3(size.x, size.y * 0.5f, size.z) : size;
        }

        public static Material SharedMaterial
        {
            get
            {
                if (shared == null)
                {
                    shared = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "DM Shapes", enableInstancing = true };
                }
                return shared;
            }
        }

        /// <summary>Roughness as in UE (0 glossy, 1 matte); URP Lit takes smoothness.</summary>
        public static void Tint(Renderer renderer, Color color, float roughness = 0.6f, float metallic = 0f)
        {
            block ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            block.SetFloat(Smoothness, 1f - roughness);
            block.SetFloat(Metallic, metallic);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>A visual-only part (no collider) under parent: local position, full size (m), optional local rotation.</summary>
        public static MeshRenderer AddPart(Transform parent, string name, DMShape shape, Vector3 position, Vector3 size, Color color,
            float roughness = 0.6f, float metallic = 0f, Vector3 euler = default)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(euler);
            part.transform.localScale = ScaleFor(shape, size);
            part.AddComponent<MeshFilter>().sharedMesh = GetMesh(shape);
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SharedMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            Tint(renderer, color, roughness, metallic);
            return renderer;
        }
    }
}
