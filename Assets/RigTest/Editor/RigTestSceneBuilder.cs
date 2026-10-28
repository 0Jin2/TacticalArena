using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 메뉴 Tools > RigTest > Create Test Scene
// 땅 + 카메라 + 리깅 캐릭터가 있는 테스트 씬을 만들어 Assets/RigTest/RigTest.unity 에 저장한다.
public static class RigTestSceneBuilder
{
    private const string Folder = "Assets/RigTest";
    private const string ScenePath = Folder + "/RigTest.unity";
    private const string CharacterPath = Folder + "/rig_test.aseprite";
    private const string SquarePath = Folder + "/Square.png";
    private const string RotSpriteSetPath = Folder + "/RotSprite/rig_test_rot.asset";

    [MenuItem("Tools/RigTest/Create Test Scene")]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        GameObject characterAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPath);
        if (characterAsset == null)
        {
            EditorUtility.DisplayDialog("RigTest", $"{CharacterPath} 의 프리팹을 찾을 수 없습니다.\nAseprite 임포터의 Generate Model Prefab 이 켜져 있는지 확인하세요.", "확인");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라
        var cameraObj = new GameObject("Main Camera");
        cameraObj.tag = "MainCamera";
        var camera = cameraObj.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 0.8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.55f, 0.75f, 0.9f);
        cameraObj.transform.position = new Vector3(0f, -0.1f, -10f);

        // 저해상도(192x108)로 렌더링 후 확대 → 회전한 파츠도 같은 픽셀 격자로 다시 찍힌다
        var pixelPerfect = cameraObj.AddComponent<PixelPerfectCamera>();
        pixelPerfect.assetsPPU = 64;
        pixelPerfect.refResolutionX = 192;
        pixelPerfect.refResolutionY = 108;
        pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;

        // 땅
        var ground = new GameObject("Ground");
        var groundRenderer = ground.AddComponent<SpriteRenderer>();
        groundRenderer.sprite = GetSquareSprite();
        groundRenderer.color = new Color(0.35f, 0.55f, 0.3f);
        groundRenderer.sortingOrder = -100;
        ground.transform.position = new Vector3(0f, -0.55f, 0f);
        ground.transform.localScale = new Vector3(12f, 1.1f, 1f);

        // 캐릭터: Player(이동) > Visual(좌우 반전 + 리그 + 걷기) > aseprite 모델
        var player = new GameObject("Player");
        var visual = new GameObject("Visual");
        visual.transform.SetParent(player.transform, false);

        var model = (GameObject)PrefabUtility.InstantiatePrefab(characterAsset);
        model.transform.SetParent(visual.transform, false);
        // 임포터가 붙인 애니메이터가 파츠를 덮어쓰지 않도록 끈다
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;

        var mover = player.AddComponent<RigTestMover>();
        var rig = visual.AddComponent<CutoutRig>();
        var walk = visual.AddComponent<CutoutWalk>();

        SetField(mover, "visual", visual.transform);
        SetField(rig, "model", model.transform);
        SetField(walk, "rig", rig);
        SetField(walk, "mover", mover);

        // RotSprite 회전본이 구워져 있으면 연결 (없으면 Tools > RigTest > Bake RotSprite)
        var rotSet = AssetDatabase.LoadAssetAtPath<RotSpriteSet>(RotSpriteSetPath);
        if (rotSet != null)
        {
            var rotRig = visual.AddComponent<RotSpriteRig>();
            SetField(rotRig, "rig", rig);
            SetField(rotRig, "set", rotSet);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = visual;
        Debug.Log($"[RigTest] 테스트 씬 생성: {ScenePath}  (Play 후 WASD로 이동)");
    }

    private static void SetField(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 1유닛 크기 흰색 사각형 스프라이트 (없으면 생성)
    private static Sprite GetSquareSprite()
    {
        if (!File.Exists(SquarePath))
        {
            var tex = new Texture2D(4, 4);
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            File.WriteAllBytes(SquarePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(SquarePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(SquarePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 4;
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
    }
}
