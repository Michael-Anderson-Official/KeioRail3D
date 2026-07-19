using System.Collections.Generic;
using UnityEngine;

// シーン起動時に地形・線路・PLATEAU建物を実データから構築する。
public class SceneBootstrap : MonoBehaviour
{
    public Material terrainMaterial;
    public Material ballastMaterial;
    public Material railMaterial;
    public Material tieMaterial;
    public Material trainMaterial;
    public Material embankmentMaterial;
    public Material deckMaterial;
    public Material pierMaterial;
    public Material platformMaterial;
    public Material canopyMaterial;
    public Material stationWallMaterial;
    public Material stationGlassMaterial;
    public Material stationRoofMaterial;

    async void Start()
    {
        var seg = await KeioData.LoadSegment();
        var grid = await KeioData.LoadTerrain();
        var profile = RailProfile.Build(seg, grid);

        TerrainBuilder.Build(grid, terrainMaterial).transform.SetParent(transform, false);
        RailBuilder.BuildTracks(profile, ballastMaterial, railMaterial, tieMaterial).transform.SetParent(transform, false);
        RailBuilder.BuildViaduct(seg, grid, profile, embankmentMaterial, deckMaterial, pierMaterial)
            ?.transform.SetParent(transform, false);
        RailBuilder.BuildSakurajosuiStation(seg, profile, platformMaterial, canopyMaterial,
            stationWallMaterial, stationGlassMaterial, stationRoofMaterial).transform.SetParent(transform, false);

        // 列車(仮: 20m×2.8m×3.5mの箱)。下り本線(DnThrough)に沿って走らせる
        var path = RailBuilder.ResampledPath(profile, RailProfile.Track.DnThrough);
        var train = GameObject.CreatePrimitive(PrimitiveType.Cube);
        train.name = "Train";
        train.transform.localScale = new Vector3(2.8f, 3.5f, 20f);
        if (trainMaterial != null) train.GetComponent<MeshRenderer>().sharedMaterial = trainMaterial;
        var mover = train.AddComponent<TrainMover>();
        mover.path = path;
        train.transform.SetParent(transform, false);

        // PLATEAU建物タイル
        var plateauRoot = new GameObject("PlateauTiles");
        plateauRoot.transform.SetParent(transform, false);
        await PlateauLoader.LoadAll(seg, grid, plateauRoot.transform);
    }
}

// 経路に沿って往復する仮の列車制御(信号・台本は次スライスで移植)
public class TrainMover : MonoBehaviour
{
    public List<Vector3> path;
    public float speed = 15f; // m/s ≒ 各停の表定速度より少し速め
    float dist;
    int dir = 1;
    float totalLen;
    float[] cum;

    void Start()
    {
        cum = new float[path.Count];
        for (int i = 1; i < path.Count; i++)
            cum[i] = cum[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        totalLen = cum[^1];
    }

    void Update()
    {
        dist += speed * Time.deltaTime * dir;
        if (dist >= totalLen) { dist = totalLen; dir = -1; }
        if (dist <= 0) { dist = 0; dir = 1; }
        int i = System.Array.BinarySearch(cum, dist);
        if (i < 0) i = ~i - 1;
        i = Mathf.Clamp(i, 0, path.Count - 2);
        float t = Mathf.InverseLerp(cum[i], cum[i + 1], dist);
        Vector3 pos = Vector3.Lerp(path[i], path[i + 1], t);
        Vector3 fwd = (path[i + 1] - path[i]).normalized * dir;
        transform.SetPositionAndRotation(pos + Vector3.up * 1.75f, Quaternion.LookRotation(fwd));
    }
}

// 1本指ドラッグ=パン(移動)、2本指ドラッグ=回転、ピンチ/ホイール=ズームの簡易オービットカメラ
public class OrbitCamera : MonoBehaviour
{
    public Vector3 target = new(0, 45, 0);
    public float distance = 500f;
    public float yaw = 200f, pitch = 45f;
    Vector3 lastMousePos;

    void Update()
    {
        // タッチは実タッチのdeltaPositionを直接使う(WebGLのタッチ→マウス疑似入力は
        // ブラウザ実装依存で感度が暴れやすく、ドラッグの操作感が破綻する原因になる)
        if (Input.touchCount == 1)
        {
            var t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved) Pan(t.deltaPosition);
        }
        else if (Input.touchCount >= 2)
        {
            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);
            float cur = (t0.position - t1.position).magnitude;
            float prev = ((t0.position - t0.deltaPosition) - (t1.position - t1.deltaPosition)).magnitude;
            if (cur > 1f) distance = Mathf.Clamp(distance * (prev / cur), 30f, 3000f);

            // 2本指の平行移動で回転
            var avg = (t0.deltaPosition + t1.deltaPosition) * 0.5f;
            yaw += avg.x * 0.15f;
            pitch = Mathf.Clamp(pitch - avg.y * 0.15f, 5f, 89f);
        }
        else if (Input.GetMouseButton(1)) // 右ドラッグでパン(PC)
        {
            Pan((Vector2)Input.mousePosition - (Vector2)lastMousePos);
        }
        else if (Input.GetMouseButton(0))
        {
            yaw += Input.GetAxis("Mouse X") * 3f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 5f, 89f);
        }
        lastMousePos = Input.mousePosition;

        distance = Mathf.Clamp(distance * (1f - Input.mouseScrollDelta.y * 0.1f), 30f, 3000f);
        var rot = Quaternion.Euler(pitch, yaw, 0);
        transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -distance), rot);
    }

    void Pan(Vector2 screenDelta)
    {
        var yawRot = Quaternion.Euler(0, yaw, 0);
        Vector3 right = yawRot * Vector3.right;
        Vector3 fwdFlat = yawRot * Vector3.forward;
        float scale = distance * 0.0015f; // 見下ろす距離が遠いほど1ピクセルあたりの移動量を大きく
        target -= right * screenDelta.x * scale;
        target -= fwdFlat * screenDelta.y * scale;
    }
}
