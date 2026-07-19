using UnityEngine;

// rail-sim game.js の railY()/groundProfile() の移植。
// 中心線(seg.points、約40m等間隔)を弧長パラメータ u(0..1) で扱う。
// 地上区間は実地形を300m窓で平滑化した勾配、上北沢の先から先はランプを経て
// 一定高さの高架(DECK_HEIGHT)になる(京王「笹塚〜仙川連続立体交差事業」の既完成区間)。
public class RailProfile
{
    Vector2[] pts;      // seg.points (x=東, y=zSouth)
    double[] cum;       // 各点までの累積弧長
    double totalLen;
    float[] smoothed;   // groundProfile用の平滑化サンプル(STEP間隔)
    int nSteps;
    float uKami, uSakura, uHachi;

    public float URampStart { get; private set; }
    public float URampEnd { get; private set; }
    public float DeckHeight { get; private set; }
    public float TotalLen => (float)totalLen;
    public float USakura => uSakura;   // 桜上水(待避駅)
    public float UHachi => uHachi;     // 八幡山(待避駅)

    // game.js の4本の線路(上り/下り本線・上り/下り待避線)
    public enum Track { UpThrough, DnThrough, UpLoop, DnLoop }

    public static RailProfile Build(KeioData.Segment seg, KeioData.TerrainGrid grid)
    {
        var p = new RailProfile { pts = seg.points.ToArray() };
        int n = p.pts.Length;
        p.cum = new double[n];
        for (int i = 1; i < n; i++)
            p.cum[i] = p.cum[i - 1] + Vector2.Distance(p.pts[i - 1], p.pts[i]);
        p.totalLen = p.cum[n - 1];

        const float step = 20f;
        p.nSteps = Mathf.Max(2, Mathf.RoundToInt(p.TotalLen / step));
        var raw = new float[p.nSteps + 1];
        for (int i = 0; i <= p.nSteps; i++)
        {
            var pos = p.PositionAt((float)i / p.nSteps);
            raw[i] = grid.HeightAt(pos.x, pos.y);
        }
        const int win = 15; // ±300m / 20mステップ
        p.smoothed = new float[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            float s = 0; int c = 0;
            for (int k = -win; k <= win; k++)
            {
                int j = i + k;
                if (j < 0 || j >= raw.Length) continue;
                s += raw[j]; c++;
            }
            p.smoothed[i] = s / c;
        }

        p.uKami = NearestU(p.pts, p.cum, p.totalLen, seg.stations["kamikitazawa"]);
        p.uSakura = NearestU(p.pts, p.cum, p.totalLen, seg.stations["sakurajosui"]);
        p.uHachi = NearestU(p.pts, p.cum, p.totalLen, seg.stations["hachimanyama"]);
        float uKami = p.uKami;
        float uTagFullHeight = Mathf.Min(1f, (float)(2270.0 / p.totalLen));   // OSM実測: この位置は橋として存在
        float uKamiPlatformEnd = Mathf.Min(1f, uKami + (float)(130.0 / p.totalLen)); // 上北沢ホーム端+余裕
        const float rampLen = 300f;
        float rampLenU = rampLen / p.TotalLen;
        p.URampEnd = Mathf.Max(uTagFullHeight, uKamiPlatformEnd + rampLenU);
        p.URampStart = Mathf.Max(uKamiPlatformEnd, p.URampEnd - rampLenU);
        const float viaductClearance = 6.5f; // 高架下に道路が通れる桁下有効高の目安
        p.DeckHeight = p.GroundProfile(p.URampEnd) + viaductClearance;
        return p;
    }

    public Vector2 PositionAt(float u)
    {
        double target = Mathf.Clamp01(u) * totalLen;
        int i = 0;
        while (i < cum.Length - 2 && cum[i + 1] < target) i++;
        double segLen = cum[i + 1] - cum[i];
        float t = segLen > 1e-9 ? (float)((target - cum[i]) / segLen) : 0f;
        return Vector2.Lerp(pts[i], pts[i + 1], t);
    }

    public float NearestU(Vector2 target) => NearestU(pts, cum, totalLen, target);

    public Vector2 TangentAt(float u)
    {
        float eps = 2f / TotalLen;
        Vector2 a = PositionAt(Mathf.Max(0f, u - eps));
        Vector2 b = PositionAt(Mathf.Min(1f, u + eps));
        var d = b - a;
        return d.sqrMagnitude > 1e-9f ? d.normalized : Vector2.right;
    }

    public float UAtIndex(int i) => (float)(cum[i] / totalLen);

    float GroundProfile(float u)
    {
        float fi = Mathf.Clamp(u * nSteps, 0, nSteps);
        int i0 = Mathf.FloorToInt(fi);
        float t = fi - i0;
        int i1 = Mathf.Min(nSteps, i0 + 1);
        return Mathf.Lerp(smoothed[i0], smoothed[i1], t);
    }

    public float RailY(float u)
    {
        if (u <= URampStart) return GroundProfile(u);
        if (u >= URampEnd) return DeckHeight;
        float t = Smoothstep((u - URampStart) / (URampEnd - URampStart));
        return Mathf.Lerp(GroundProfile(u), DeckHeight, t);
    }

    static float Smoothstep(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    // 中心線からの横オフセット(three系: +が上り側)。本線は上北沢の島式ホーム分だけ外へ開き(KamiBump)、
    // 待避線はさらに桜上水・八幡山の駅部分だけ外へふくらむ(LoopBump)。game.js offsetCurveの移植
    public float TrackOffset(Track track, float u)
    {
        float through = 2.2f + KamiBump(u);
        switch (track)
        {
            case Track.UpThrough: return through;
            case Track.DnThrough: return -through;
            case Track.UpLoop: return through + LoopBump(u);
            default: return -(through + LoopBump(u));
        }
    }

    // 桜上水・八幡山(待避駅)の島式ホームぶんの、本線⇔待避線の中間位置(側は呼び出し側で符号を付ける)
    public float PlatformMid(float u) => 2.2f + KamiBump(u) + LoopBump(u) * 0.5f;

    // 上北沢の島式ホームぶんのふくらみ:駅の前後で上下線が外へ開いて島を挟む(210m=10両編成全長+60mイーズ)
    float KamiBump(float u)
    {
        float d = Mathf.Abs(u - uKami) * TotalLen;
        if (d < 105f) return 1.3f;
        if (d < 165f) return Smoothstep(1f - (d - 105f) / 60f) * 1.3f;
        return 0f;
    }

    // 待避線のふくらみ:窓の外は本線と同じ、駅の中央で外へ開く(passing loop)。桜上水と八幡山の2駅が待避駅
    float LoopBump(float u) => LoopBumpAt(u, uSakura) + LoopBumpAt(u, uHachi);

    float LoopBumpAt(float u, float uStn)
    {
        float d = Mathf.Abs(u - uStn) * TotalLen;
        if (d < 110f) return 6.5f;
        if (d < 170f) return Smoothstep(1f - (d - 110f) / 60f) * 6.5f;
        return 0f;
    }

    static float NearestU(Vector2[] pts, double[] cum, double totalLen, Vector2 target)
    {
        double bestD = double.MaxValue, bestLen = 0;
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 a = pts[i], b = pts[i + 1];
            Vector2 ab = b - a;
            float abLenSq = ab.sqrMagnitude;
            float t = abLenSq > 1e-6f ? Mathf.Clamp01(Vector2.Dot(target - a, ab) / abLenSq) : 0f;
            Vector2 proj = a + ab * t;
            double d = (target - proj).sqrMagnitude;
            if (d < bestD) { bestD = d; bestLen = cum[i] + t * ab.magnitude; }
        }
        return (float)(bestLen / totalLen);
    }
}
