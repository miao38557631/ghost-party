using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Faint painted dashes on the floor along the tutorial's active movement limit.</summary>
internal sealed class TutorialBoundary
{
    public enum Edge { None, Left, Right, Near, Far }

    const float RevealDistance = .92f;
    const float DashSpacing = .38f;
    const float VisibleHalfLength = 1.5f;
    const float SurfaceLift = .024f;

    static readonly Color Ink = new Color(.30f, .16f, .22f, .78f);
    static readonly Color Paint = new Color(1f, .86f, .47f, .96f);

    readonly GhostPlayerMotor motor;
    readonly MeshRenderer renderer;
    readonly Mesh mesh;
    readonly Material material;
    readonly List<Vector3> vertices = new List<Vector3>(108);
    readonly List<Color> colors = new List<Color>(108);
    readonly List<int> triangles = new List<int>(144);
    readonly MaterialPropertyBlock tint = new MaterialPropertyBlock();
    Edge drawnEdge;
    int drawnCenter = int.MinValue;

    public TutorialBoundary(Transform owner, GhostPlayerMotor motor)
    {
        this.motor = motor;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null || motor == null) return;

        var art = new GameObject("Tutorial boundary", typeof(MeshFilter), typeof(MeshRenderer));
        art.transform.SetParent(owner, false);
        renderer = art.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;

        mesh = new Mesh { name = "Tutorial boundary dashes" };
        mesh.MarkDynamic();
        art.GetComponent<MeshFilter>().sharedMesh = mesh;
        material = new Material(shader) { name = "Tutorial boundary paint" };
        renderer.sharedMaterial = material;
    }

    public bool ShowNear(Rect bounds, Vector3 player, Edge blocked)
    {
        Edge side = blocked != Edge.None ? blocked : NearestEdge(bounds, player);
        float distance = DistanceTo(bounds, player, side);
        if (distance >= RevealDistance)
        {
            Hide();
            return false;
        }

        if (renderer == null) return true;
        bool alongZ = side == Edge.Left || side == Edge.Right;
        float min = alongZ ? bounds.yMin : bounds.xMin;
        float max = alongZ ? bounds.yMax : bounds.xMax;
        float along = alongZ ? player.z : player.x;
        int center = Mathf.RoundToInt((along - min) / DashSpacing);
        if (drawnEdge != side || drawnCenter != center)
        {
            drawnEdge = side;
            drawnCenter = center;
            DrawDashes(bounds, side, alongZ, min, max, center, player.y);
        }

        float fade = blocked != Edge.None ? 1f : Mathf.SmoothStep(0f, 1f, 1f - distance / RevealDistance);
        tint.SetColor("_Color", new Color(1f, 1f, 1f, fade));
        renderer.SetPropertyBlock(tint);
        renderer.enabled = mesh.vertexCount > 0;
        return true;
    }

    public void Hide()
    {
        if (renderer != null) renderer.enabled = false;
        drawnEdge = Edge.None;
    }

    void DrawDashes(Rect bounds, Edge side, bool alongZ, float min, float max, int center, float probeHeight)
    {
        // Align the ink to the same X/Z coordinates that stop movement.
        float cross = side == Edge.Left ? bounds.xMin : side == Edge.Right ? bounds.xMax
            : side == Edge.Near ? bounds.yMin : bounds.yMax;
        int radius = Mathf.CeilToInt(VisibleHalfLength / DashSpacing);
        vertices.Clear();
        colors.Clear();
        triangles.Clear();

        for (int i = center - radius; i <= center + radius; i++)
        {
            float along = min + i * DashSpacing;
            float halfLength = (i & 1) == 0 ? .124f : .146f;
            float start = Mathf.Max(min + .035f, along - halfLength);
            float end = Mathf.Min(max - .035f, along + halfLength);
            if (end - start < .08f) continue;

            Vector3 a = alongZ ? new Vector3(cross, probeHeight, start) : new Vector3(start, probeHeight, cross);
            Vector3 b = alongZ ? new Vector3(cross, probeHeight, end) : new Vector3(end, probeHeight, cross);
            if (!motor.TryGetSurfaceAt(a, out float heightA) || !motor.TryGetSurfaceAt(b, out float heightB)) continue;
            a.y = heightA + SurfaceLift;
            b.y = heightB + SurfaceLift;

            Vector3 across = alongZ ? Vector3.right : Vector3.forward;
            AddDash(a, b, across, .049f, Ink);
            Vector3 inset = (b - a).normalized * .018f;
            AddDash(a + inset, b - inset, across, .027f, Paint);
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
    }

    void AddDash(Vector3 a, Vector3 b, Vector3 across, float width, Color color)
    {
        int start = vertices.Count;
        Vector3 edge = across * width;
        Vector3 cap = (b - a) * .14f;
        AddVertex(a, color);
        AddVertex(a + cap - edge, color);
        AddVertex(b - cap - edge, color);
        AddVertex(b, color);
        AddVertex(b - cap + edge, color);
        AddVertex(a + cap + edge, color);
        for (int i = 1; i < 5; i++)
        {
            triangles.Add(start);
            triangles.Add(start + i);
            triangles.Add(start + i + 1);
        }
    }

    void AddVertex(Vector3 position, Color color)
    {
        vertices.Add(renderer.transform.InverseTransformPoint(position));
        colors.Add(color);
    }

    static Edge NearestEdge(Rect bounds, Vector3 player)
    {
        Edge closest = Edge.Left;
        float distance = DistanceTo(bounds, player, closest);
        for (Edge side = Edge.Right; side <= Edge.Far; side++)
        {
            float next = DistanceTo(bounds, player, side);
            if (next >= distance) continue;
            distance = next;
            closest = side;
        }
        return closest;
    }

    static float DistanceTo(Rect bounds, Vector3 player, Edge side)
    {
        switch (side)
        {
            case Edge.Left: return Mathf.Abs(player.x - bounds.xMin);
            case Edge.Right: return Mathf.Abs(player.x - bounds.xMax);
            case Edge.Near: return Mathf.Abs(player.z - bounds.yMin);
            default: return Mathf.Abs(player.z - bounds.yMax);
        }
    }

    public void Dispose()
    {
        if (renderer != null) Object.Destroy(renderer.gameObject);
        if (mesh != null) Object.Destroy(mesh);
        if (material != null) Object.Destroy(material);
    }
}
