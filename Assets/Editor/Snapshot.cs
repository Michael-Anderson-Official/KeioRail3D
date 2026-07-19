using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// バッチモードから -executeMethod Snapshot.Run で呼ぶ。
// Main.unity を開き、SceneBootstrap と同じ手順でシーンを構築(Editモードなので手動実行)、
// PLATEAUロード完了を EditorApplication.update で待って複数視点のPNGを Snapshots/ に保存する。
public static class Snapshot
{
    const float TimeoutSec = 300f;
    static Task loadTask;
    static KeioData.Segment seg;
    static KeioData.TerrainGrid grid;
    static double t0;

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        seg = KeioData.LoadSegment().Result;   // Editorでは File IO なので同期完了する
        grid = KeioData.LoadTerrain().Result;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);

        var terrainMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Terrain.mat");
        var railMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Rail.mat");
        var root = new GameObject("SnapshotRoot");
        TerrainBuilder.Build(grid, terrainMat).transform.SetParent(root.transform, false);
        RailBuilder.Build(seg, grid, railMat).transform.SetParent(root.transform, false);
        var plateauRoot = new GameObject("PlateauTiles");
        plateauRoot.transform.SetParent(root.transform, false);

        loadTask = PlateauLoader.LoadAll(seg, grid, plateauRoot.transform);
        t0 = EditorApplication.timeSinceStartup;
        EditorApplication.update += Pump;
        Debug.Log("Snapshot: scene built, waiting for PLATEAU tiles...");
    }

    static void Pump()
    {
        if (loadTask.IsFaulted)
        {
            Debug.LogError($"Snapshot: PLATEAU load failed: {loadTask.Exception}");
            EditorApplication.Exit(1);
        }
        if (!loadTask.IsCompleted)
        {
            if (EditorApplication.timeSinceStartup - t0 > TimeoutSec)
            {
                Debug.LogError("Snapshot: timeout waiting for PLATEAU tiles");
                EditorApplication.Exit(2);
            }
            return;
        }
        EditorApplication.update -= Pump;

        var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Snapshots");
        Directory.CreateDirectory(outDir);

        // 診断: PLATEAUタイルがどこに配置されたか
        var plateauRoot = GameObject.Find("PlateauTiles");
        var rends = plateauRoot.GetComponentsInChildren<Renderer>();
        Debug.Log($"Snapshot: plateau children={plateauRoot.transform.childCount} renderers={rends.Length}");
        if (rends.Length > 0)
        {
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            Debug.Log($"Snapshot: plateau bounds center={b.center} size={b.size}");
            Debug.Log($"Snapshot: first tile '{rends[0].transform.root.name}' bounds={rends[0].bounds.center} size={rends[0].bounds.size} mat={rends[0].sharedMaterial?.shader?.name}");
            Shot(outDir, "plateau_bounds.png", b.center, Mathf.Max(b.size.x, b.size.z) * 0.9f, 45f, 200f);
        }

        // 視点: 全景 / 八幡山(高架区間)近景 / 下高井戸近景
        Shot(outDir, "overview.png", UnityPos("sakurajosui"), 2200f, 45f, 200f);
        Shot(outDir, "hachimanyama.png", UnityPos("hachimanyama"), 350f, 25f, 160f);
        Shot(outDir, "shimotakaido.png", UnityPos("shimotakaido"), 350f, 25f, 200f);

        Debug.Log("Snapshot: done");
        EditorApplication.Exit(0);
    }

    static Vector3 UnityPos(string station)
    {
        Vector2 p = seg.stations[station]; // three系 [x, zSouth]
        return new Vector3(p.x, grid.HeightAt(p.x, p.y) + 10f, -p.y);
    }

    static void Shot(string dir, string name, Vector3 target, float dist, float pitch, float yaw)
    {
        var camGo = new GameObject("SnapCam");
        var cam = camGo.AddComponent<Camera>();
        cam.farClipPlane = 12000f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.66f, 0.80f, 0.94f);
        var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -dist), rot);

        const int W = 1600, H = 900;
        var rt = new RenderTexture(W, H, 24);
        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req))
        {
            req.destination = rt;
            RenderPipeline.SubmitRenderRequest(cam, req);
        }
        else
        {
            cam.targetTexture = rt;
            cam.Render();
        }

        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        File.WriteAllBytes(Path.Combine(dir, name), tex.EncodeToPNG());
        RenderTexture.active = null;

        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo);
        Debug.Log($"Snapshot: wrote {name}");
    }
}
