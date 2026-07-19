using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

// PLATEAU LOD2建物タイル(Draco圧縮GLB)を glTFast で読み込み、ENU変換で配置する。
// three.js版 game.js の tileMatrix() の移植。
// three系(x=東,y=上,z=南,右手系) → Unity(x=東,y=上,z=北,左手系)は z 反転で対応。
public static class PlateauLoader
{
    const double A = 6378137.0, E2 = 0.00669437999014;

    public static async Task LoadAll(KeioData.Segment seg, KeioData.TerrainGrid grid, Transform parent)
    {
        double la = seg.centerLat * Mathd.Deg2Rad, lo = seg.centerLng * Mathd.Deg2Rad;
        double sinLa = System.Math.Sin(la), cosLa = System.Math.Cos(la);
        double sinLo = System.Math.Sin(lo), cosLo = System.Math.Cos(lo);
        double nrad = A / System.Math.Sqrt(1 - E2 * sinLa * sinLa);
        // 原点のECEF座標
        double c0x = nrad * cosLa * cosLo, c0y = nrad * cosLa * sinLo, c0z = nrad * (1 - E2) * sinLa;
        // ENU基底(東・北・上)ベクトル
        var ev = new Vector3d(-sinLo, cosLo, 0);
        var nv = new Vector3d(-sinLa * cosLo, -sinLa * sinLo, cosLa);
        var uv = new Vector3d(cosLa * cosLo, cosLa * sinLo, sinLa);

        // 回転の導出: b3dm由来のglTFはy-up(ECEF z-up へは Rx+90°)、頂点はタイル中心からの
        // ECEF向きオフセット。ECEF→ローカルはENU基底との内積、glTFastはx反転で取り込む。
        // これらを合成した行列は正規直交・det=+1 の純回転になる(鏡映なし):
        //   M·ez = (-cosLo, -cosLa·sinLo, sinLa·sinLo),  M·ey = (0, sinLa, cosLa)
        var rot = Quaternion.LookRotation(
            new Vector3((float)(-cosLo), (float)(-cosLa * sinLo), (float)(sinLa * sinLo)),
            new Vector3(0f, (float)sinLa, (float)cosLa));

        foreach (var tile in await KeioData.LoadPlateauManifest())
        {
            // UninterruptedDeferAgent: 起動時一括ロードなのでフレーム分割不要(Editorバッチでも動く)
            var gltf = new GltfImport(deferAgent: new UninterruptedDeferAgent(),
                logger: new GLTFast.Logging.ConsoleLogger());
            var bytes = StripCesiumRtc(await KeioData.FetchBytes("plateau/" + tile.file));
            if (!await gltf.LoadGltfBinary(bytes))
            {
                Debug.LogWarning($"PLATEAU tile load failed: {tile.file}");
                continue;
            }
            // タイル中心のECEF→ローカルENU
            double dx = tile.centerEcef[0] - c0x, dy = tile.centerEcef[1] - c0y, dz = tile.centerEcef[2] - c0z;
            float east = (float)(ev.x * dx + ev.y * dy + ev.z * dz);
            float north = (float)(nv.x * dx + nv.y * dy + nv.z * dz);
            float up = (float)(uv.x * dx + uv.y * dy + uv.z * dz);

            var root = new GameObject(tile.file);
            root.transform.SetParent(parent, false);
            await gltf.InstantiateMainSceneAsync(root.transform);

            // 縦位置: g(タイル地面高3%ile)はENU上方向オフセット(up)込みの世界yで定義
            // されている(three.js版のM2と同じ)。upを落とすと全体が地下に沈むので注意。
            float zSouth = -north;
            float groundY = grid.HeightAt(east, zSouth);
            root.transform.SetLocalPositionAndRotation(
                new Vector3(east, up + groundY - tile.groundHeight, north), rot);
        }
    }

    // b3dm由来のGLBにはglTFastが未対応の必須拡張があり、そのままではロードを拒否される。
    // - CESIUM_RTC: RTC中心はマニフェストのcenterEcefと同値(検証済み)なので宣言ごと除去
    // - EXT_texture_webp: 一部タイルのみ。no-textureデータセットなのでテクスチャ参照ごと除去
    static byte[] StripCesiumRtc(byte[] glb)
    {
        uint jsonLen = System.BitConverter.ToUInt32(glb, 12);
        var root = Newtonsoft.Json.Linq.JObject.Parse(
            System.Text.Encoding.UTF8.GetString(glb, 20, (int)jsonLen));

        bool hadWebp = false;
        foreach (var key in new[] { "extensionsRequired", "extensionsUsed" })
        {
            if (root[key] is Newtonsoft.Json.Linq.JArray arr)
            {
                for (int i = arr.Count - 1; i >= 0; i--)
                {
                    var name = (string)arr[i];
                    if (name == "EXT_texture_webp") hadWebp = true;
                    if (name == "CESIUM_RTC" || name == "EXT_texture_webp") arr.RemoveAt(i);
                }
                if (arr.Count == 0) root.Remove(key);
            }
        }
        if (root["extensions"] is Newtonsoft.Json.Linq.JObject ext)
        {
            ext.Remove("CESIUM_RTC");
            if (!ext.HasValues) root.Remove("extensions");
        }
        if (hadWebp)
        {
            root.Remove("images");
            root.Remove("textures");
            root.Remove("samplers");
            if (root["materials"] is Newtonsoft.Json.Linq.JArray mats)
                foreach (var m in mats)
                {
                    if (m is not Newtonsoft.Json.Linq.JObject mo) continue;
                    mo.Remove("normalTexture");
                    mo.Remove("occlusionTexture");
                    mo.Remove("emissiveTexture");
                    if (mo["pbrMetallicRoughness"] is Newtonsoft.Json.Linq.JObject pbr)
                    {
                        pbr.Remove("baseColorTexture");
                        pbr.Remove("metallicRoughnessTexture");
                    }
                }
        }

        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(
            root.ToString(Newtonsoft.Json.Formatting.None));
        int padded = (jsonBytes.Length + 3) & ~3;         // JSONチャンクは4バイト境界、空白でパディング
        int restOffset = 20 + (int)jsonLen;               // BINチャンク以降はそのままコピー
        var outBytes = new byte[20 + padded + (glb.Length - restOffset)];

        System.Array.Copy(glb, outBytes, 12);
        System.BitConverter.GetBytes((uint)outBytes.Length).CopyTo(outBytes, 8);
        System.BitConverter.GetBytes((uint)padded).CopyTo(outBytes, 12);
        System.Array.Copy(glb, 16, outBytes, 16, 4);      // "JSON"チャンク型
        jsonBytes.CopyTo(outBytes, 20);
        for (int i = 20 + jsonBytes.Length; i < 20 + padded; i++) outBytes[i] = 0x20;
        System.Array.Copy(glb, restOffset, outBytes, 20 + padded, glb.Length - restOffset);
        return outBytes;
    }

    struct Vector3d
    {
        public double x, y, z;
        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    }

    static class Mathd { public const double Deg2Rad = System.Math.PI / 180.0; }
}
