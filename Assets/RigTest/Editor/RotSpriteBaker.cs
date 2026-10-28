using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 Tools > RigTest > Bake RotSprite
// 열린 씬의 CutoutRig 파츠들을 관절 기준으로 AngleStep마다 RotSprite 방식으로 회전시켜 아틀라스로 굽는다.
// RotSprite: Scale2x를 3번(8배) → 확대된 상태에서 회전 → 원래 크기로 샘플링. 도트 선이 1px 계단으로 깔끔하게 유지됨
public static class RotSpriteBaker
{
    private const float AngleStep = 15f;
    private const string OutFolder = "Assets/RigTest/RotSprite";
    private const string AtlasPath = OutFolder + "/rig_test_rot.png";
    private const string SetPath = OutFolder + "/rig_test_rot.asset";
    private const int AtlasWidth = 512;
    private const int Padding = 1;

    private class Baked
    {
        public string part;
        public Color32[] pixels;
        public int width, height;
        public Vector2 pivot;
        public RectInt rect;
    }

    [MenuItem("Tools/RigTest/Bake RotSprite")]
    public static void Bake()
    {
        if (Application.isPlaying)
        {
            Debug.LogError("[RotSprite] 플레이 모드에서는 구울 수 없습니다.");
            return;
        }

        var rig = Object.FindAnyObjectByType<CutoutRig>();
        if (rig == null)
        {
            Debug.LogError("[RotSprite] 열린 씬에 CutoutRig가 없습니다. RigTest 씬을 열고 실행하세요.");
            return;
        }

        int frameCount = Mathf.RoundToInt(360f / AngleStep);
        var baked = new List<Baked>();
        var partNames = new List<string>();

        foreach (CutoutRig.Bone bone in rig.Bones)
        {
            Transform part = CutoutRig.FindPart(rig.Model, bone.name);
            var sr = part != null ? part.GetComponent<SpriteRenderer>() : null;
            if (sr == null || sr.sprite == null)
            {
                Debug.LogWarning($"[RotSprite] '{bone.name}' 스프라이트를 찾을 수 없어 건너뜁니다.");
                continue;
            }

            Color32[] src = ReadSprite(sr.sprite, out int w, out int h);
            Color32[] up = Scale2x(Scale2x(Scale2x(src, w, h), w * 2, h * 2), w * 4, h * 4);

            // 캔버스 좌표(왼쪽 아래 원점, 픽셀). 임포트 피벗 = 캔버스 하단 중앙
            Vector2 origin = new Vector2(rig.CanvasSize.x * 0.5f, 0f) - sr.sprite.pivot;
            Vector2 joint = new Vector2(bone.pivotPx.x, rig.CanvasSize.y - bone.pivotPx.y);

            partNames.Add(bone.name);
            for (int i = 0; i < frameCount; i++)
            {
                Baked b = Rotate(up, w, h, origin, joint, i * AngleStep);
                b.part = bone.name;
                baked.Add(b);
            }
        }

        if (baked.Count == 0) return;

        Directory.CreateDirectory(OutFolder);
        Texture2D atlas = Pack(baked);
        File.WriteAllBytes(AtlasPath, atlas.EncodeToPNG());
        Object.DestroyImmediate(atlas);
        AssetDatabase.ImportAsset(AtlasPath);
        ConfigureAtlasImporter();

        var set = AssetDatabase.LoadAssetAtPath<RotSpriteSet>(SetPath);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<RotSpriteSet>();
            AssetDatabase.CreateAsset(set, SetPath);
        }
        set.atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        set.pixelsPerUnit = rig.PixelsPerUnit;
        set.angleStep = AngleStep;
        set.parts.Clear();
        foreach (string name in partNames)
        {
            var part = new RotSpriteSet.Part { name = name };
            foreach (Baked b in baked)
            {
                if (b.part == name) part.frames.Add(new RotSpriteSet.Frame { rect = b.rect, pivotPx = b.pivot });
            }
            set.parts.Add(part);
        }
        EditorUtility.SetDirty(set);
        AssetDatabase.SaveAssets();

        // 씬의 리그에 연결
        var rotRig = rig.GetComponent<RotSpriteRig>();
        if (rotRig == null) rotRig = rig.gameObject.AddComponent<RotSpriteRig>();
        var so = new SerializedObject(rotRig);
        so.FindProperty("rig").objectReferenceValue = rig;
        so.FindProperty("set").objectReferenceValue = set;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        EditorSceneManager.SaveScene(rig.gameObject.scene);

        Debug.Log($"[RotSprite] 파츠 {partNames.Count}개 × {frameCount}각도 굽기 완료: {AtlasPath}");
    }

    // 관절(joint)을 중심으로 angle도 회전 (반시계, 캔버스 좌표)
    private static Baked Rotate(Color32[] up, int w, int h, Vector2 origin, Vector2 joint, float angle)
    {
        float rad = angle * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);

        // 회전 후 경계
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (Vector2 corner in new[] { origin, origin + new Vector2(w, 0), origin + new Vector2(0, h), origin + new Vector2(w, h) })
        {
            Vector2 d = corner - joint;
            Vector2 r = joint + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            minX = Mathf.Min(minX, r.x); maxX = Mathf.Max(maxX, r.x);
            minY = Mathf.Min(minY, r.y); maxY = Mathf.Max(maxY, r.y);
        }
        int x0 = Mathf.FloorToInt(minX + 0.001f), y0 = Mathf.FloorToInt(minY + 0.001f);
        int ow = Mathf.CeilToInt(maxX - 0.001f) - x0, oh = Mathf.CeilToInt(maxY - 0.001f) - y0;

        int uw = w * 8, uh = h * 8;
        var outPx = new Color32[ow * oh];
        for (int y = 0; y < oh; y++)
        {
            for (int x = 0; x < ow; x++)
            {
                // 출력 픽셀 중심을 역회전해서 8배 이미지에서 샘플링
                Vector2 d = new Vector2(x0 + x + 0.5f, y0 + y + 0.5f) - joint;
                Vector2 q = joint + new Vector2(d.x * cos + d.y * sin, -d.x * sin + d.y * cos);
                int ux = Mathf.FloorToInt((q.x - origin.x) * 8f);
                int uy = Mathf.FloorToInt((q.y - origin.y) * 8f);
                if (ux >= 0 && ux < uw && uy >= 0 && uy < uh) outPx[y * ow + x] = up[uy * uw + ux];
            }
        }

        return Trim(outPx, ow, oh, joint - new Vector2(x0, y0));
    }

    // 투명한 가장자리 제거
    private static Baked Trim(Color32[] px, int w, int h, Vector2 pivot)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].a == 0) continue;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
        }
        if (maxX < 0) return new Baked { pixels = new Color32[1], width = 1, height = 1, pivot = pivot };

        int tw = maxX - minX + 1, th = maxY - minY + 1;
        var trimmed = new Color32[tw * th];
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++) trimmed[y * tw + x] = px[(y + minY) * w + x + minX];
        }
        return new Baked { pixels = trimmed, width = tw, height = th, pivot = pivot - new Vector2(minX, minY) };
    }

    // Scale2x (EPX): 픽셀아트 모양을 유지한 채 2배 확대
    private static Color32[] Scale2x(Color32[] src, int w, int h)
    {
        int dw = w * 2;
        var dst = new Color32[dw * h * 2];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Color32 p = src[y * w + x];
                Color32 up = src[Mathf.Min(y + 1, h - 1) * w + x];
                Color32 down = src[Mathf.Max(y - 1, 0) * w + x];
                Color32 left = src[y * w + Mathf.Max(x - 1, 0)];
                Color32 right = src[y * w + Mathf.Min(x + 1, w - 1)];

                Color32 tl = p, tr = p, bl = p, br = p;
                if (!Same(up, down) && !Same(left, right))
                {
                    if (Same(left, up)) tl = up;
                    if (Same(up, right)) tr = right;
                    if (Same(left, down)) bl = left;
                    if (Same(down, right)) br = right;
                }

                int dx = x * 2, dy = y * 2;
                dst[dy * dw + dx] = bl;
                dst[dy * dw + dx + 1] = br;
                dst[(dy + 1) * dw + dx] = tl;
                dst[(dy + 1) * dw + dx + 1] = tr;
            }
        }
        return dst;
    }

    private static bool Same(Color32 a, Color32 b)
    {
        return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    }

    // 임포트된 텍스처는 읽기 불가라 GPU로 복사해서 읽는다
    private static Color32[] ReadSprite(Sprite sprite, out int w, out int h)
    {
        Rect r = sprite.rect;
        w = (int)r.width;
        h = (int)r.height;

        Texture tex = sprite.texture;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(r.x, r.y, w, h), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] px = readable.GetPixels32();
        Object.DestroyImmediate(readable);

        // 완전 투명 픽셀은 색을 통일 (Scale2x 비교용)
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].a == 0) px[i] = new Color32(0, 0, 0, 0);
        }
        return px;
    }

    // 단순 선반(shelf) 방식으로 한 장에 배치
    private static Texture2D Pack(List<Baked> baked)
    {
        int x = Padding, y = Padding, rowHeight = 0;
        foreach (Baked b in baked)
        {
            if (x + b.width + Padding > AtlasWidth)
            {
                x = Padding;
                y += rowHeight + Padding;
                rowHeight = 0;
            }
            b.rect = new RectInt(x, y, b.width, b.height);
            x += b.width + Padding;
            rowHeight = Mathf.Max(rowHeight, b.height);
        }
        int height = y + rowHeight + Padding;

        var atlas = new Texture2D(AtlasWidth, height, TextureFormat.RGBA32, false);
        atlas.SetPixels32(new Color32[AtlasWidth * height]);
        foreach (Baked b in baked) atlas.SetPixels32(b.rect.x, b.rect.y, b.width, b.height, b.pixels);
        atlas.Apply();
        return atlas;
    }

    private static void ConfigureAtlasImporter()
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }
}
