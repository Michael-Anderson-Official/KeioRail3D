using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// バッチモードから -executeMethod SceneSetup.CreateMainScene で呼ぶ。
// Main.unity を生成: ライト・カメラ・SceneBootstrap・URPマテリアル。
public static class SceneSetup
{
    public static void CreateMainScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0);
        sun.intensity = 1.2f;

        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.tag = "MainCamera";
        cam.farClipPlane = 8000f;
        camGo.AddComponent<OrbitCamera>();

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        var terrainMat = MakeMat("Assets/Materials/Terrain.mat", new Color(0.55f, 0.63f, 0.45f));
        var railMat = MakeMat("Assets/Materials/Rail.mat", new Color(0.35f, 0.35f, 0.38f));
        var trainMat = MakeMat("Assets/Materials/Train.mat", new Color(0.85f, 0.15f, 0.35f)); // 京王れーるカラー(仮)
        var embankMat = MakeMat("Assets/Materials/Embankment.mat", new Color(0.549f, 0.498f, 0.388f));
        var deckMat = MakeMat("Assets/Materials/Deck.mat", new Color(0.604f, 0.604f, 0.573f));
        var pierMat = MakeMat("Assets/Materials/Pier.mat", new Color(0.529f, 0.525f, 0.494f));

        var bootGo = new GameObject("SceneBootstrap");
        var boot = bootGo.AddComponent<SceneBootstrap>();
        boot.terrainMaterial = terrainMat;
        boot.railMaterial = railMat;
        boot.trainMaterial = trainMat;
        boot.embankmentMaterial = embankMat;
        boot.deckMaterial = deckMat;
        boot.pierMaterial = pierMat;

        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        AssetDatabase.SaveAssets();
        Debug.Log("SceneSetup: Main.unity created OK");
        EditorApplication.Exit(0);
    }

    static Material MakeMat(string path, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
