using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// rail-sim からエクスポートした JSON(StreamingAssets/keio)の読み込み。
// 座標系: エクスポート元は three.js 系(x=東, z=南)。Unity では z=北 なので z を反転する。
// WebGLではStreamingAssetsがURLになりFile IO不可のため、取得はFetchText/FetchBytes経由。
// (Editor/スタンドアロンではFile IOで同期完了するので、EditorからResultで受けても安全)
public static class KeioData
{
    public class Segment
    {
        public double centerLat;
        public double centerLng;
        public List<Vector2> points = new();          // x=東(m), y=Unityのz(北, m)
        public Dictionary<string, Vector2> stations = new();
    }

    public class TerrainGrid
    {
        public float ox, oz;      // グリッド原点(three系のx,z。zは読み込み時に反転済みではない点に注意)
        public float cell;
        public int cols, rows;
        public float[] h;         // rows*cols、行優先

        // three系座標(x, zSouth)で双線形補間。呼び出し側はUnity z を渡す前に反転すること
        public float HeightAt(float x, float zSouth)
        {
            float fx = (x - ox) / cell;
            float fz = (zSouth - oz) / cell;
            int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, cols - 2);
            int iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, rows - 2);
            float tx = Mathf.Clamp01(fx - ix);
            float tz = Mathf.Clamp01(fz - iz);
            float h00 = h[iz * cols + ix], h10 = h[iz * cols + ix + 1];
            float h01 = h[(iz + 1) * cols + ix], h11 = h[(iz + 1) * cols + ix + 1];
            return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
        }
    }

    public class PlateauTile
    {
        public string file;
        public double[] centerEcef; // [x,y,z]
        public float groundHeight;  // Draco地面高(3パーセンタイル)
    }

    public class Footprint
    {
        public List<Vector2> outline = new(); // three系 [x, zSouth] のまま保持
        public float height;                  // buildings のみ。roads は 0
    }

    static string Dir => Path.Combine(Application.streamingAssetsPath, "keio");

    public static async Task<byte[]> FetchBytes(string relPath)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        using var req = UnityEngine.Networking.UnityWebRequest.Get(Dir + "/" + relPath);
        var op = req.SendWebRequest();
        while (!op.isDone) await Task.Yield();
        if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
            throw new IOException($"fetch failed: {relPath}: {req.error}");
        return req.downloadHandler.data;
#else
        return await Task.FromResult(File.ReadAllBytes(Path.Combine(Dir, relPath)));
#endif
    }

    static async Task<JToken> Load(string name) =>
        JToken.Parse(System.Text.Encoding.UTF8.GetString(await FetchBytes(name)));

    public static async Task<Segment> LoadSegment()
    {
        var j = await Load("segment.json");
        var seg = new Segment
        {
            centerLat = (double)j["center"]["lat"],
            centerLng = (double)j["center"]["lng"],
        };
        foreach (var p in (JArray)j["points"])
            seg.points.Add(new Vector2((float)p[0], (float)p[1]));
        foreach (var kv in (JObject)j["stations"])
            seg.stations[kv.Key] = new Vector2((float)kv.Value[0], (float)kv.Value[1]);
        return seg;
    }

    public static async Task<TerrainGrid> LoadTerrain()
    {
        var j = await Load("terrain.json");
        var t = new TerrainGrid
        {
            ox = (float)j["ox"], oz = (float)j["oz"], cell = (float)j["cell"],
            cols = (int)j["cols"], rows = (int)j["rows"],
        };
        var arr = (JArray)j["h"];
        t.h = new float[arr.Count];
        for (int i = 0; i < arr.Count; i++) t.h[i] = (float)arr[i];
        return t;
    }

    public static async Task<List<PlateauTile>> LoadPlateauManifest()
    {
        var list = new List<PlateauTile>();
        foreach (var j in (JArray)(await Load("plateau_manifest.json"))["items"])
        {
            list.Add(new PlateauTile
            {
                file = (string)j["f"],
                centerEcef = new[] { (double)j["c"][0], (double)j["c"][1], (double)j["c"][2] },
                groundHeight = j["g"] != null ? (float)j["g"] : 80f,
            });
        }
        return list;
    }
}
