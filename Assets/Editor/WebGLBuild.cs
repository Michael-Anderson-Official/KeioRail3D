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
        if (report.summary.result == BuildResult.Succeeded) InjectWebIcons("Builds/WebGL");

        Debug.Log($"WebGLBuild: {report.summary.result}, size={report.summary.totalSize / 1024 / 1024}MB, " +
                  $"errors={report.summary.totalErrors}, time={report.summary.totalTime}");
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    // WebIcons/(プロジェクト直下)のアイコン・manifestをビルド出力へコピーし、
    // index.htmlの<head>にタイトルとアイコンリンクを差し込む(ビルドごとに再生成されるため)
    static void InjectWebIcons(string outDir)
    {
        var iconsOut = System.IO.Path.Combine(outDir, "icons");
        System.IO.Directory.CreateDirectory(iconsOut);
        foreach (var f in System.IO.Directory.GetFiles("WebIcons", "icon-*.png"))
            System.IO.File.Copy(f, System.IO.Path.Combine(iconsOut, System.IO.Path.GetFileName(f)), true);
        System.IO.File.Copy("WebIcons/manifest.webmanifest",
            System.IO.Path.Combine(outDir, "manifest.webmanifest"), true);
        // iOSはサイト直下の apple-touch-icon.png も探しに行くのでフォールバックを置く
        System.IO.File.Copy("WebIcons/icon-180.png",
            System.IO.Path.Combine(outDir, "apple-touch-icon.png"), true);
        System.IO.File.Copy("WebIcons/icon-180.png",
            System.IO.Path.Combine(outDir, "apple-touch-icon-precomposed.png"), true);

        var htmlPath = System.IO.Path.Combine(outDir, "index.html");
        var html = System.IO.File.ReadAllText(htmlPath);
        html = System.Text.RegularExpressions.Regex.Replace(
            html, "<title>.*?</title>", "<title>京王線3D</title>");
        const string headExtra =
            "<link rel=\"icon\" type=\"image/png\" sizes=\"32x32\" href=\"icons/icon-32.png\">\n" +
            "    <link rel=\"apple-touch-icon\" sizes=\"180x180\" href=\"icons/icon-180.png\">\n" +
            "    <link rel=\"manifest\" href=\"manifest.webmanifest\">\n" +
            "    <meta name=\"apple-mobile-web-app-capable\" content=\"yes\">\n" +
            "    <meta name=\"apple-mobile-web-app-status-bar-style\" content=\"default\">\n" +
            "    <meta name=\"apple-mobile-web-app-title\" content=\"京王線3D\">\n" +
            "    <link rel=\"stylesheet\"";
        html = html.Replace("<link rel=\"stylesheet\"", headExtra);
        System.IO.File.WriteAllText(htmlPath, html);
        Debug.Log("WebGLBuild: web icons injected");
    }
}
