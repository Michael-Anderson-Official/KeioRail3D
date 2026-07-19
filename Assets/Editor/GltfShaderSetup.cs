using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// glTFastはPLATEAU建物のマテリアル用シェーダーを実行時に名前で動的検索する
// (Shader.Find("Shader Graphs/glTF-pbrMetallicRoughness") 等)。ビルド時のシェーダー
// ストリッピングはシーンからの静的参照を見て判定するため、これらは「未使用」と誤判定
// され除外される→実機で建物がマゼンタ(シェーダー未検出色)になる。
// Always Included Shaders に恒久登録することで解決する(GUIDはglTFastの
// ShaderGraphMaterialGenerator.csのk_MetallicShaderGuid等と同一)。
public static class GltfShaderSetup
{
    static readonly (string guid, string name)[] Shaders =
    {
        ("b9d29dfa1474148e792ac720cbd45122", "glTF-pbrMetallicRoughness"),
        ("c87047c884d9843f5b0f4cce282aa760", "glTF-unlit"),
        ("9a07dad0f3c4e43ff8312e3b5fa42300", "glTF-pbrSpecularGlossiness"),
    };

    [MenuItem("Tools/glTFast/Register Shaders In Always Included")]
    public static void Configure()
    {
        var settings = GraphicsSettings.GetGraphicsSettings();
        var so = new SerializedObject(settings);
        var prop = so.FindProperty("m_AlwaysIncludedShaders");

        int added = 0;
        foreach (var (guid, name) in Shaders)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var shader = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                Debug.LogWarning($"GltfShaderSetup: shader not found for {name} (guid {guid})");
                continue;
            }

            bool already = false;
            for (int i = 0; i < prop.arraySize; i++)
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue == shader) { already = true; break; }
            if (already) continue;

            prop.InsertArrayElementAtIndex(prop.arraySize);
            prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = shader;
            added++;
        }

        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"GltfShaderSetup: {added} shader(s) added to Always Included Shaders");
    }

    // バッチモード用(-executeMethod)。EditorApplication.Exitまで行う。
    public static void ConfigureAndExit()
    {
        Configure();
        EditorApplication.Exit(0);
    }
}
