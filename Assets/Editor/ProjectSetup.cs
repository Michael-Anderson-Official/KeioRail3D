using System.Threading;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

// バッチモードから -executeMethod ProjectSetup.AddPackages で呼ぶ
public static class ProjectSetup
{
    public static void AddPackages()
    {
        var packages = new[]
        {
            "com.unity.render-pipelines.universal",
            "com.unity.cloud.gltfast",
            "com.unity.cloud.draco",
            "com.unity.nuget.newtonsoft-json",
        };
        AddAndRemoveRequest req = Client.AddAndRemove(packages, null);
        while (!req.IsCompleted)
        {
            Thread.Sleep(200);
        }
        if (req.Status == StatusCode.Success)
        {
            Debug.Log("ProjectSetup: packages added OK");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"ProjectSetup: failed - {req.Error?.message}");
            EditorApplication.Exit(1);
        }
    }
}
