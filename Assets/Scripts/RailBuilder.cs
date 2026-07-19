using System.Collections.Generic;
using UnityEngine;

// SEGMENT.points から線路4本(上り/下り本線・上り/下り待避線)と、高架区間の築堤・桁・橋脚を生成する。
// 高さは RailProfile.RailY(u) (縦断勾配+八幡山高架) に従う。rail-sim game.js の
// buildTrack()/offsetCurve()/buildViaduct() の移植(意匠は単純化した箱の連なりのまま踏襲)。
public static class RailBuilder
{
    const float Gauge = 1.6f, RailHalfWidth = 0.08f, BallastHalfWidth = 1.9f;
    const float RailLift = 0.15f, TieLift = 0.06f, TieSpacing = 3f;
    static readonly Vector3 TieScale = new(2.5f, 0.12f, 0.28f);

    // 4本すべて(上り/下り本線・上り/下り待避線)をまとめて構築。待避線は本線と重なる区間を省き、
    // 桜上水・八幡山それぞれの窓(loopBumpが非ゼロの範囲、駅中心±170m)だけを別ジオメトリとして生成する
    // (game.jsは全長を4本重ねて描画しているが、Unity側は無駄な重複ジオメトリ/Zファイティングを避ける)
    public static GameObject BuildTracks(RailProfile profile, Material ballastMat, Material railMat, Material tieMat)
    {
        var root = new GameObject("Tracks");
        BuildTrack("UpThrough", profile, RailProfile.Track.UpThrough, 0f, 1f, ballastMat, railMat, tieMat)
            .transform.SetParent(root.transform, false);
        BuildTrack("DnThrough", profile, RailProfile.Track.DnThrough, 0f, 1f, ballastMat, railMat, tieMat)
            .transform.SetParent(root.transform, false);

        const float loopWindow = 170f;
        foreach (var (label, uStn) in new[] { ("Sakurajosui", profile.USakura), ("Hachimanyama", profile.UHachi) })
        {
            float uStart = Mathf.Max(0f, uStn - loopWindow / profile.TotalLen);
            float uEnd = Mathf.Min(1f, uStn + loopWindow / profile.TotalLen);
            BuildTrack($"UpLoop_{label}", profile, RailProfile.Track.UpLoop, uStart, uEnd, ballastMat, railMat, tieMat)
                .transform.SetParent(root.transform, false);
            BuildTrack($"DnLoop_{label}", profile, RailProfile.Track.DnLoop, uStart, uEnd, ballastMat, railMat, tieMat)
                .transform.SetParent(root.transform, false);
        }
        return root;
    }

    // 1本の線路:バラスト帯+レール2本+枕木(インスタンス相当、CombineMeshesで集約)
    static GameObject BuildTrack(string name, RailProfile profile, RailProfile.Track track, float uStart, float uEnd,
        Material ballastMat, Material railMat, Material tieMat)
    {
        int n = Mathf.Max(2, Mathf.RoundToInt((uEnd - uStart) * profile.TotalLen / 6f));
        var center = new List<Vector3>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float u = Mathf.Lerp(uStart, uEnd, (float)i / n);
            var pos2 = profile.PositionAt(u) + NormalAt(profile, u) * profile.TrackOffset(track, u);
            center.Add(new Vector3(pos2.x, profile.RailY(u), -pos2.y));
        }

        var root = new GameObject(name);
        BuildRibbon("Ballast", center, BallastHalfWidth, ballastMat).transform.SetParent(root.transform, false);
        foreach (float rOff in new[] { Gauge * 0.5f, -Gauge * 0.5f })
            BuildRibbon("Rail", OffsetParallel(center, rOff, RailLift), RailHalfWidth, railMat)
                .transform.SetParent(root.transform, false);

        var unitCube = GetUnitCubeMesh();
        var tieCombine = new List<CombineInstance>();
        float carried = 0f;
        for (int i = 1; i < center.Count; i++)
        {
            Vector3 a = center[i - 1], b = center[i];
            float segLen = Vector3.Distance(a, b);
            if (segLen < 1e-6f) continue;
            var rot = Quaternion.LookRotation((b - a).normalized);
            for (float d = TieSpacing - carried; d <= segLen; d += TieSpacing)
                tieCombine.Add(new CombineInstance
                {
                    mesh = unitCube,
                    transform = Matrix4x4.TRS(Vector3.Lerp(a, b, d / segLen) + Vector3.up * TieLift, rot, TieScale)
                });
            carried = (carried + segLen) % TieSpacing;
        }
        AddCombined(root.transform, "Ties", tieCombine, tieMat);
        return root;
    }

    // 中心線(three系)のu位置における法線(xz平面、正規化済み)。offsetCurveの nx,nz の移植
    static Vector2 NormalAt(RailProfile profile, float u)
    {
        var tan = profile.TangentAt(u);
        return new Vector2(-tan.y, tan.x);
    }

    // center点列に平行な帯を、各点の局所接線から求めた法線方向へ横+縦オフセットして作る(レール2本用)
    static List<Vector3> OffsetParallel(List<Vector3> center, float lateralOff, float verticalOff)
    {
        var outPts = new List<Vector3>(center.Count);
        for (int i = 0; i < center.Count; i++)
        {
            Vector3 dir = i == 0 ? center[1] - center[0]
                : i == center.Count - 1 ? center[i] - center[i - 1]
                : center[i + 1] - center[i - 1];
            dir.y = 0;
            Vector3 side = Vector3.Cross(Vector3.up, dir.normalized) * lateralOff;
            outPts.Add(center[i] + side + Vector3.up * verticalOff);
        }
        return outPts;
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

    // 弧長パラメータ u(0..1) → 位置。列車走行用。track指定時はその線路の横オフセットに沿う
    public static List<Vector3> ResampledPath(RailProfile profile, RailProfile.Track? track = null, float step = 10f)
    {
        int n = Mathf.Max(2, Mathf.RoundToInt(profile.TotalLen / step));
        var outPts = new List<Vector3>(n + 1);
        for (int i = 0; i <= n; i++)
        {
            float u = (float)i / n;
            var pos2 = profile.PositionAt(u);
            if (track.HasValue) pos2 += NormalAt(profile, u) * profile.TrackOffset(track.Value, u);
            outPts.Add(new Vector3(pos2.x, profile.RailY(u), -pos2.y));
        }
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
    // ホーム位置は本線⇔待避線の中間(RailProfile.PlatformMid、複線分離後の実オフセット))
    public static GameObject BuildSakurajosuiStation(KeioData.Segment seg, RailProfile profile,
        Material platformMat, Material canopyMat, Material wallMat, Material glassMat, Material roofMat)
    {
        float uStn = profile.NearestU(seg.stations["sakurajosui"]);
        var root = new GameObject("SakurajosuiStation");

        const float platformLen = 210f; // 10両編成対応(実物と同じ全長)
        const float platformHalfW = 1.5f, canopyHalfW = 2.7f;
        const float platformLift = 1.0f, canopyLift = 4.4f;
        int n = 40;
        foreach (float side in new[] { 1f, -1f })
        {
            var slabPts = new List<Vector3>();
            var roofPts = new List<Vector3>();
            for (int i = 0; i <= n; i++)
            {
                float u = uStn - platformLen * 0.5f / profile.TotalLen + platformLen / profile.TotalLen * i / n;
                if (u < 0f || u > 1f) continue;
                var pos2 = profile.PositionAt(u) + NormalAt(profile, u) * (profile.PlatformMid(u) * side);
                var basePos = new Vector3(pos2.x, profile.RailY(u), -pos2.y);
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
