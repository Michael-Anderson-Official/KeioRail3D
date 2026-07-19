using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// バッチモードから -executeMethod UrpSetup.Configure で呼ぶ(URPパッケージ導入後)
public static class UrpSetup
{
    public static void Configure()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, "Assets/Settings/URP-Renderer.asset");

        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(pipeline, "Assets/Settings/URP-Pipeline.asset");

        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        // モバイル前提の色空間設定
        PlayerSettings.colorSpace = ColorSpace.Linear;

        AssetDatabase.SaveAssets();
        Debug.Log("UrpSetup: URP configured OK");
        EditorApplication.Exit(0);
    }
}
