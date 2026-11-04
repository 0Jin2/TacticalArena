using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 메뉴 Tools > RigTest > Setup Grass
// 잔디 스프라이트와 머티리얼을 만들고 열려 있는 RigTest 씬에 배치한다. 다시 실행하면 배치를 새로 만든다.
public static class RigTestGrassSetup
{
    private const string Folder = "Assets/RigTest/Grass";
    private const string TallSpritePath = Folder + "/Sprites/grass_tall.png";
    private const string ShortSpritePath = Folder + "/Sprites/grass_short.png";
    private const string PixelMaterialPath = Folder + "/Materials/Grass_Pixel.mat";
    private const string SimulationShaderPath = Folder + "/Shaders/GrassVelocitySim.shader";

    private const int PixelsPerUnit = 64;
    private const int SidePadding = 10; // 잔디가 휘어도 잘리지 않을 여백
    private const int TopPadding = 4;

    private static readonly Color32 Dark = new Color32(95, 128, 84, 255);
    private static readonly Color32 Mid = new Color32(122, 151, 96, 255);
    private static readonly Color32 Light = new Color32(146, 170, 108, 255);

    [MenuItem("Tools/RigTest/Setup Grass")]
    public static void Setup()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TallSpritePath));
        Directory.CreateDirectory(Path.GetDirectoryName(PixelMaterialPath));

        Sprite tall = CreateTuftSprite(TallSpritePath, 28, 16, 2, 11);
        Sprite shortTuft = CreateTuftSprite(ShortSpritePath, 28, 12, 3, 23);
        Material pixel = CreateMaterial(PixelMaterialPath, "RigTest/Grass Pixel");

        BuildScene(tall, shortTuft, pixel);
    }

    private static void BuildScene(Sprite tall, Sprite shortTuft, Material pixel)
    {
        GameObject player = GameObject.Find("Player");
        Camera camera = Camera.main;
        if (player == null || camera == null)
        {
            Debug.LogError("[RigTest] RigTest 씬을 연 상태에서 실행하세요 (Player, Main Camera 필요)");
            return;
        }

        GameObject old = GameObject.Find("Grass");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("Grass");

        // 좌우 대칭으로 깔고 가운데는 비워 둔다
        float[] rows = { -0.14f, -0.42f, -0.72f };
        float[] columns = { 0.3f, 0.72f, 1.14f };
        for (int row = 0; row < rows.Length; row++)
        {
            for (int column = 0; column < columns.Length; column++)
            {
                float stagger = row % 2 == 0 ? 0f : 0.1f;
                float x = columns[column] + stagger;
                Sprite sprite = (row + column) % 2 == 0 ? tall : shortTuft;
                bool flip = column % 2 == 1;
                CreateTuft(root.transform, sprite, pixel, -x, rows[row], flip);
                CreateTuft(root.transform, sprite, pixel, x, rows[row], flip);
            }
        }

        var sort = root.AddComponent<GrassDepthSort>();
        SetField(sort, "target", player.transform);

        if (player.GetComponent<GrassEmitter>() == null) player.AddComponent<GrassEmitter>();

        var field = camera.GetComponent<GrassVelocityField>();
        if (field == null) field = camera.gameObject.AddComponent<GrassVelocityField>();
        SetField(field, "simulationShader", AssetDatabase.LoadAssetAtPath<Shader>(SimulationShaderPath));

        EditorSceneManager.MarkSceneDirty(root.scene);
        EditorSceneManager.SaveScene(root.scene);
        Debug.Log("[RigTest] 잔디 배치 완료. Play 후 WASD로 잔디를 지나가 보세요");
    }

    private static void CreateTuft(Transform parent, Sprite sprite, Material material, float x, float y, bool flip)
    {
        var tuft = new GameObject("Tuft");
        tuft.transform.SetParent(parent, false);
        tuft.transform.position = new Vector3(Snap(x), Snap(y), 0f);

        var renderer = tuft.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = material;
        renderer.flipX = flip;
        renderer.sortingOrder = -1;
    }

    private static float Snap(float value)
    {
        return Mathf.Round(value * PixelsPerUnit) / PixelsPerUnit;
    }

    private static Material CreateMaterial(string path, string shaderName)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        material = new Material(Shader.Find(shaderName));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Sprite CreateTuftSprite(string path, int tuftWidth, int tuftHeight, int bladeSpacing, int seed)
    {
        int width = tuftWidth + SidePadding * 2;
        int height = tuftHeight + TopPadding;
        File.WriteAllBytes(path, DrawTuft(width, height, tuftWidth, tuftHeight, bladeSpacing, seed));
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    private static byte[] DrawTuft(int width, int height, int tuftWidth, int tuftHeight, int bladeSpacing, int seed)
    {
        var random = new System.Random(seed);
        var pixels = new Color32[width * height]; // 기본값은 완전 투명

        int left = (width - tuftWidth) / 2;
        int right = left + tuftWidth - 1;

        for (int x = left; x <= right; x++)
        {
            pixels[x] = Dark;
            if (x > left && x < right && random.Next(3) > 0) pixels[width + x] = Dark;
        }

        for (int bladeX = left + 1; bladeX < right; bladeX += bladeSpacing + random.Next(2))
        {
            int bladeHeight = Mathf.Max(4, tuftHeight - random.Next(tuftHeight / 2));
            float lean = (float)(random.NextDouble() * 6.0 - 3.0);
            Color32 color = random.Next(3) == 0 ? Light : Mid;

            for (int y = 0; y < bladeHeight; y++)
            {
                float t = (float)y / bladeHeight;
                int x = bladeX + Mathf.RoundToInt(lean * t * t);
                if (x < left || x > right) continue;

                pixels[y * width + x] = y < bladeHeight / 3 ? Dark : color;
                if (t < 0.5f && x + 1 <= right) pixels[y * width + x + 1] = t < 0.25f ? Dark : Mid;
            }
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        return png;
    }

    private static void SetField(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
