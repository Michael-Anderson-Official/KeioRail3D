using UnityEngine;

// TERRAINグリッド(15mセル)から地形メッシュを生成する。
// three系(x=東, z=南) → Unity(x=東, z=北)なので z を反転して頂点を置く。
public static class TerrainBuilder
{
    public static GameObject Build(KeioData.TerrainGrid grid, Material mat)
    {
        int cols = grid.cols, rows = grid.rows;
        var verts = new Vector3[cols * rows];
        var uvs = new Vector2[cols * rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float x = grid.ox + c * grid.cell;
                float zSouth = grid.oz + r * grid.cell;
                verts[r * cols + c] = new Vector3(x, grid.h[r * cols + c], -zSouth);
                uvs[r * cols + c] = new Vector2((float)c / (cols - 1), (float)r / (rows - 1));
            }
        }
        // z反転で巻き方向が裏返るため、三角形は反時計回りを維持する順で張る
        var tris = new int[(cols - 1) * (rows - 1) * 6];
        int t = 0;
        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < cols - 1; c++)
            {
                int i00 = r * cols + c, i10 = i00 + 1, i01 = i00 + cols, i11 = i01 + 1;
                tris[t++] = i00; tris[t++] = i10; tris[t++] = i01;
                tris[t++] = i10; tris[t++] = i11; tris[t++] = i01;
            }
        }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Terrain");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }
}
