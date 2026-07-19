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
        return BuildRibbon("RailLine", center, Width * 0.5f, mat);
    }

    // 中心点列に沿った平帯メッシュ(線路帯・ホーム・上屋の共通実装)
    static GameObject BuildRibbon(string name, List<Vector3> center, float halfWidth, Material mat)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i < center.Count; i++)
        {
            Vector3 dir = i == 0 ? center[1] - center[0]
                : i == center.Count - 1 ? center[i] - center[i - 1]
                : center[i + 1] - center[i - 1];
            dir.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * halfWidth;
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

        var go = new GameObject(name);
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

    // 桜上水:2面4線の島式ホーム2面+線路をまたぐ橋上駅舎
    // (rail-sim game.jsのbuildIslandLoopStation/buildBridgeStationHouseの移植。
    // 複線分離前の簡易表現のため、待避線ふくらみ(loopBump)は使わずホーム位置を固定オフセットで近似)
    public static GameObject BuildSakurajosuiStation(KeioData.Segment seg, RailProfile profile,
        Material platformMat, Material canopyMat, Material wallMat, Material glassMat, Material roofMat)
    {
        float uStn = profile.NearestU(seg.stations["sakurajosui"]);
        var root = new GameObject("SakurajosuiStation");

        const float platformLen = 210f; // 10両編成対応(実物と同じ全長)
        const float platformHalfW = 1.5f, canopyHalfW = 2.7f;
        const float platformLift = 1.0f, canopyLift = 4.4f, sideOffset = 7f;
        int n = 40;
        foreach (float side in new[] { 1f, -1f })
        {
            var slabPts = new List<Vector3>();
            var roofPts = new List<Vector3>();
            for (int i = 0; i <= n; i++)
            {
                float u = uStn - platformLen * 0.5f / profile.TotalLen + platformLen / profile.TotalLen * i / n;
                if (u < 0f || u > 1f) continue;
                var pos2 = profile.PositionAt(u);
                var rot = RotFromTangent(profile.TangentAt(u));
                Vector3 lateral = rot * Vector3.right;
                var basePos = new Vector3(pos2.x, profile.RailY(u), -pos2.y) + lateral * (sideOffset * side);
                slabPts.Add(basePos + Vector3.up * platformLift);
                roofPts.Add(basePos + Vector3.up * canopyLift);
            }
            if (slabPts.Count < 2) continue;
            BuildRibbon("Platform", slabPts, platformHalfW, platformMat).transform.SetParent(root.transform, false);
            BuildRibbon("Canopy", roofPts, canopyHalfW, canopyMat).transform.SetParent(root.transform, false);
        }

        // 橋上駅舎:線路をまたぐ箱+支柱
        var stnPos2 = profile.PositionAt(uStn);
        var stnRot = RotFromTangent(profile.TangentAt(uStn));
        var stnBase = new Vector3(stnPos2.x, profile.RailY(uStn), -stnPos2.y);
        var unitCube = GetUnitCubeMesh();
        Matrix4x4 Box(Vector3 localOffset, Vector3 size) =>
            Matrix4x4.TRS(stnBase + stnRot * localOffset, stnRot, size);

        var bodyCombine = new List<CombineInstance>
        {
            new() { mesh = unitCube, transform = Box(new Vector3(0, 8.2f, 0), new Vector3(26f, 3.4f, 13f)) },
        };
        AddCombined(root.transform, "BridgeHouseBody", bodyCombine, wallMat);

        var roofCombine = new List<CombineInstance>
        {
            new() { mesh = unitCube, transform = Box(new Vector3(0, 10.1f, 0), new Vector3(27.5f, 0.5f, 14.5f)) },
        };
        AddCombined(root.transform, "BridgeHouseRoof", roofCombine, roofMat);

        var windowCombine = new List<CombineInstance>
        {
            new() { mesh = unitCube, transform = Box(new Vector3(0, 8.4f, 0), new Vector3(26.1f, 1.2f, 13.1f)) },
        };
        AddCombined(root.transform, "BridgeHouseWindow", windowCombine, glassMat);

        var legCombine = new List<CombineInstance>();
        foreach (var (ox, oz) in new[] { (-11f, 5f), (-11f, -5f), (11f, 5f), (11f, -5f) })
            legCombine.Add(new CombineInstance { mesh = unitCube, transform = Box(new Vector3(ox, 3.25f, oz), new Vector3(1.1f, 6.5f, 1.1f)) });
        AddCombined(root.transform, "BridgeHouseLegs", legCombine, wallMat);

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
