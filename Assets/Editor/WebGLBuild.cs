using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// バッチモードから -executeMethod WebGLBuild.Run で呼ぶ。出力は Builds/WebGL。
// GitHub Pages は .br に Content-Encoding を付けないため decompressionFallback 必須。
public static class WebGLBuild
{
    public static void Run()
    {
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.runInBackground = true;

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            target = BuildTarget.WebGL,
            locationPathName = "Builds/WebGL",
        });
        Debug.Log($"WebGLBuild: {report.summary.result}, size={report.summary.totalSize / 1024 / 1024}MB, " +
                  $"errors={report.summary.totalErrors}, time={report.summary.totalTime}");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
