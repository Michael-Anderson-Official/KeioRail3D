using System.Collections.Generic;
using UnityEngine;

// シーン起動時に地形・線路・PLATEAU建物を実データから構築する。
public class SceneBootstrap : MonoBehaviour
{
    public Material terrainMaterial;
    public Material railMaterial;
    public Material trainMaterial;

    async void Start()
    {
        var seg = KeioData.LoadSegment();
        var grid = KeioData.LoadTerrain();

        TerrainBuilder.Build(grid, terrainMaterial).transform.SetParent(transform, false);
        RailBuilder.Build(seg, grid, railMaterial).transform.SetParent(transform, false);

        // 列車(仮: 20m×2.8m×3.5mの箱)
        var path = RailBuilder.ResampledPath(seg, grid);
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

// マウスドラッグで回転・ホイールでズームの簡易オービットカメラ
public class OrbitCamera : MonoBehaviour
{
    public Vector3 target = new(0, 45, 0);
    public float distance = 500f;
    float yaw = 30f, pitch = 35f;

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            yaw += Input.GetAxis("Mouse X") * 3f;
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, 5f, 89f);
        }
        distance = Mathf.Clamp(distance * (1f - Input.mouseScrollDelta.y * 0.1f), 30f, 3000f);
        var rot = Quaternion.Euler(pitch, yaw, 0);
        transform.SetPositionAndRotation(target + rot * new Vector3(0, 0, -distance), rot);
    }
}
