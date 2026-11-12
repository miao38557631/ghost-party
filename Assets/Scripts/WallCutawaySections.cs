using System.Collections.Generic;
using UnityEngine;

/// <summary>Groups an imported wall mesh by material and coplanar surface, preserving all vertex data.</summary>
public static class WallCutawaySections
{
    public sealed class Section
    {
        public Vector3 localPoint;
        public Vector3 localNormal;
        public int materialIndex;
        public readonly List<int> triangles = new List<int>();
        public bool lowered;
        public float visibleTop;
    }

    public static Mesh Create(Mesh source, Transform owner, List<Section> sections)
    {
        sections.Clear();
        if (source == null || !source.isReadable) return null;
        Vector3[] vertices = source.vertices;
        Matrix4x4 normalMatrix = owner.localToWorldMatrix.inverse.transpose;
        for (int material = 0; material < source.subMeshCount; material++)
        {
            if (source.GetTopology(material) != MeshTopology.Triangles) return null;
            int[] triangles = source.GetTriangles(material);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 normal = Vector3.Cross(vertices[triangles[i + 1]] - a,
                    vertices[triangles[i + 2]] - a).normalized;
                Vector3 worldNormal = normalMatrix.MultiplyVector(normal).normalized;
                Vector3 worldPoint = owner.TransformPoint(a);
                Section match = null;
                for (int j = 0; j < sections.Count; j++)
                {
                    Section candidate = sections[j];
                    if (candidate.materialIndex != material) continue;
                    Vector3 otherNormal = normalMatrix.MultiplyVector(candidate.localNormal).normalized;
                    if (Mathf.Abs(Vector3.Dot(worldNormal, otherNormal)) < 0.9999f) continue;
                    if (Mathf.Abs(Vector3.Dot(worldNormal,
                        owner.TransformPoint(candidate.localPoint) - worldPoint)) > 0.005f) continue;
                    match = candidate;
                    break;
                }
                if (match == null)
                {
                    match = new Section { localPoint = a, localNormal = normal, materialIndex = material };
                    sections.Add(match);
                }
                match.triangles.Add(triangles[i]);
                match.triangles.Add(triangles[i + 1]);
                match.triangles.Add(triangles[i + 2]);
            }
        }
        if (sections.Count == 0) return null;
        Mesh result = Object.Instantiate(source);
        result.name = source.name + " (runtime wall sections)";
        result.subMeshCount = sections.Count;
        for (int i = 0; i < sections.Count; i++) result.SetTriangles(sections[i].triangles, i, false);
        result.bounds = source.bounds;
        return result;
    }
}
