using System.Collections.Generic;
using UnityEngine;

// SEGMENT.points から線路リボン(複線相当の帯)を生成する。
// 高架化・縦断勾配(railY)の本移植は次スライス。まずは地形+1.2mに沿わせる。
public static class RailBuilder
{
    public const float Width = 8f;      // 複線分の帯幅
    public const float LiftAboveGround = 1.2f;

    public static GameObject Build(KeioData.Segment seg, KeioData.TerrainGrid grid, Material mat)
    {
        var pts = seg.points;
        var center = new List<Vector3>(pts.Count);
        foreach (var p in pts)
        {
            float y = grid.HeightAt(p.x, p.y) + LiftAboveGround;
            center.Add(new Vector3(p.x, y, -p.y)); // three z(南) → Unity z(北)
        }

        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < center.Count; i++)
        {
            Vector3 dir = i == 0 ? center[1] - center[0]
                : i == center.Count - 1 ? center[i] - center[i - 1]
                : center[i + 1] - center[i - 1];
            dir.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * (Width * 0.5f);
            verts.Add(center[i] - side);
            verts.Add(center[i] + side);
            if (i > 0)
            {
                // z反転後のUnity左手系で上面が表(時計回り)になる巻き順
                int b = verts.Count - 4;
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }
        }
        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();

        var go = new GameObject("RailLine");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    // 弧長パラメータ u(0..1) → 位置。列車走行用
    public static List<Vector3> ResampledPath(KeioData.Segment seg, KeioData.TerrainGrid grid, float step = 10f)
    {
        var raw = new List<Vector3>();
        foreach (var p in seg.points)
            raw.Add(new Vector3(p.x, grid.HeightAt(p.x, p.y) + LiftAboveGround, -p.y));
        var outPts = new List<Vector3> { raw[0] };
        float carry = 0f;
        for (int i = 1; i < raw.Count; i++)
        {
            Vector3 a = raw[i - 1], b = raw[i];
            float len = Vector3.Distance(a, b);
            float d = step - carry;
            while (d <= len)
            {
                outPts.Add(Vector3.Lerp(a, b, d / len));
                d += step;
            }
            carry = (carry + len) % step;
        }
        outPts.Add(raw[^1]);
        return outPts;
    }
}
