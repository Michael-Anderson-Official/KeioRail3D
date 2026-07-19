using System.Collections.Generic;
using UnityEngine;

// SEGMENT.points から線路リボン(複線相当の帯)と、高架区間の築堤・桁・橋脚を生成する。
// 高さは RailProfile.RailY(u) (縦断勾配+八幡山高架) に従う。rail-sim game.js の
// buildTrack()/buildViaduct() の移植(意匠は単純化した箱の連なりのまま踏襲)。
public static class RailBuilder
{
    public const float Width = 8f; // 複線分の帯幅

    public static GameObject Build(KeioData.Segment seg, RailProfile profile, Material mat)
    {
        var pts = seg.points;
        var center = new List<Vector3>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            float y = profile.RailY(profile.UAtIndex(i));
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
    public static List<Vector3> ResampledPath(KeioData.Segment seg, RailProfile profile, float step = 10f)
    {
        var raw = new List<Vector3>(seg.points.Count);
        for (int i = 0; i < seg.points.Count; i++)
        {
            var p = seg.points[i];
            float y = profile.RailY(profile.UAtIndex(i));
            raw.Add(new Vector3(p.x, y, -p.y));
        }
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

    // ランプ区間=土盛りの築堤(高さに応じた箱)、本設高架区間=桁(箱の連なり)+橋脚(柱+柱頭)
    public static GameObject BuildViaduct(KeioData.Segment seg, KeioData.TerrainGrid grid, RailProfile profile,
        Material embankMat, Material deckMat, Material pierMat)
    {
        if (profile.URampEnd >= 1f) return null; // 区間内に高架が存在しない

        const float halfW = 11.5f, deckThick = 1.3f, deckGap = 0.3f; // 桁上面=railY-deckGap(バラスト厚みぶん)
        var unitCube = GetUnitCubeMesh();

        var embankCombine = new List<CombineInstance>();
        var deckCombine = new List<CombineInstance>();
        var pierCombine = new List<CombineInstance>();

        float rampStep = 8f, rampStepU = rampStep / profile.TotalLen;
        for (float u = profile.URampStart; u < profile.URampEnd; u += rampStepU)
        {
            var pos = profile.PositionAt(u);
            var rot = RotFromTangent(profile.TangentAt(u));
            float gy = grid.HeightAt(pos.x, pos.y);
            float topY = profile.RailY(u) - deckGap;
            float height = Mathf.Max(0.3f, topY - gy);
            var trs = Matrix4x4.TRS(new Vector3(pos.x, gy + height / 2f, -pos.y), rot,
                new Vector3(halfW * 2f - 3f, height, rampStep + 0.6f));
            embankCombine.Add(new CombineInstance { mesh = unitCube, transform = trs });
        }

        float deckStep = 10f, deckStepU = deckStep / profile.TotalLen;
        for (float u = profile.URampEnd; u < 1f; u += deckStepU)
        {
            var pos = profile.PositionAt(u);
            var rot = RotFromTangent(profile.TangentAt(u));
            float topY = profile.RailY(u) - deckGap;
            var trs = Matrix4x4.TRS(new Vector3(pos.x, topY - deckThick / 2f, -pos.y), rot,
                new Vector3(halfW * 2f, deckThick, deckStep + 0.5f));
            deckCombine.Add(new CombineInstance { mesh = unitCube, transform = trs });
        }

        float pierSpacing = 24f, pierStepU = pierSpacing / profile.TotalLen;
        for (float u = profile.URampEnd + pierStepU * 0.5f; u < 1f; u += pierStepU)
        {
            var pos = profile.PositionAt(u);
            var rot = RotFromTangent(profile.TangentAt(u));
            float deckBottomY = profile.RailY(u) - deckGap - deckThick;
            float gy = grid.HeightAt(pos.x, pos.y);
            float h = Mathf.Max(0.5f, deckBottomY - gy);
            var colTrs = Matrix4x4.TRS(new Vector3(pos.x, gy + h / 2f, -pos.y), rot, new Vector3(2.2f, h, 3.2f));
            pierCombine.Add(new CombineInstance { mesh = unitCube, transform = colTrs });
            var capTrs = Matrix4x4.TRS(new Vector3(pos.x, deckBottomY - 0.35f, -pos.y), rot, new Vector3(3.6f, 0.7f, 4.4f));
            pierCombine.Add(new CombineInstance { mesh = unitCube, transform = capTrs });
        }

        var root = new GameObject("Viaduct");
        AddCombined(root.transform, "Embankment", embankCombine, embankMat);
        AddCombined(root.transform, "Deck", deckCombine, deckMat);
        AddCombined(root.transform, "Piers", pierCombine, pierMat);
        return root;
    }

    static void AddCombined(Transform parent, string name, List<CombineInstance> combine, Material mat)
    {
        if (combine.Count == 0) return;
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(combine.ToArray());
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // three系tan(x,zSouth)→Unity forward(x,zNorth)。zNorth=-zSouth
    static Quaternion RotFromTangent(Vector2 tanThree)
    {
        var fwd = new Vector3(tanThree.x, 0f, -tanThree.y);
        return fwd.sqrMagnitude > 1e-9f ? Quaternion.LookRotation(fwd) : Quaternion.identity;
    }

    static Mesh GetUnitCubeMesh()
    {
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(tmp);
        return mesh;
    }
}
