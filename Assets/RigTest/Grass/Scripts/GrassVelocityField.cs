using UnityEngine;

// 카메라에 붙인다. 화면 영역을 덮는 속도 필드를 매 프레임 시뮬레이션해서 잔디 쉐이더에 넘긴다.
[DefaultExecutionOrder(100)] // GrassEmitter 이후에 돈다
[RequireComponent(typeof(Camera))]
public class GrassVelocityField : MonoBehaviour
{
    private const int MaxEmitters = 8; // GrassVelocitySim.shader 의 MAX_EMITTERS 와 같아야 한다
    private const int MarginPixels = 8;

    private static readonly int VelocityTextureId = Shader.PropertyToID("_GrassVelocityTexture");
    private static readonly int FieldRectGlobalId = Shader.PropertyToID("_GrassFieldRect");
    private static readonly int SimParamsId = Shader.PropertyToID("_SimParams");
    private static readonly int FieldRectId = Shader.PropertyToID("_FieldRect");
    private static readonly int ScrollUVId = Shader.PropertyToID("_ScrollUV");
    private static readonly int EmitterCountId = Shader.PropertyToID("_EmitterCount");
    private static readonly int EmitterRectsId = Shader.PropertyToID("_EmitterRects");
    private static readonly int EmitterVelocitiesId = Shader.PropertyToID("_EmitterVelocities");

    [Tooltip("스프라이트 PPU와 맞추기")]
    [Min(1)] [SerializeField] private int pixelsPerUnit = 64;
    [Tooltip("스프링 강성. 높을수록 빠르게 되돌아온다")]
    [Min(0f)] [SerializeField] private float stiffness = 70f;
    [Tooltip("감쇠. 낮을수록 오래 흔들린다")]
    [Min(0f)] [SerializeField] private float damping = 3f;
    [Tooltip("주변 픽셀로 번지는 정도")]
    [Range(0f, 1f)] [SerializeField] private float blur = 0.5f;
    [Tooltip("프레임이 떨어져도 시뮬레이션이 터지지 않게 막는 최대 dt")]
    [Min(0.001f)] [SerializeField] private float maxDeltaTime = 0.034f;
    [SerializeField] private Shader simulationShader;

    private readonly Vector4[] emitterRects = new Vector4[MaxEmitters];
    private readonly Vector4[] emitterVelocities = new Vector4[MaxEmitters];

    private Camera cam;
    private Material material;
    private RenderTexture current;
    private RenderTexture previous;
    private Vector2 origin;

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        material = new Material(simulationShader) { hideFlags = HideFlags.HideAndDontSave };
    }

    void OnDisable()
    {
        ReleaseTextures();
        Destroy(material);
        Shader.SetGlobalVector(FieldRectGlobalId, Vector4.zero);
    }

    void LateUpdate()
    {
        if (Time.deltaTime <= 0f) return;

        float viewHeight = cam.orthographicSize * 2f;
        int width = Mathf.CeilToInt(viewHeight * cam.aspect * pixelsPerUnit) + MarginPixels * 2;
        int height = Mathf.CeilToInt(viewHeight * pixelsPerUnit) + MarginPixels * 2;
        EnsureTextures(width, height);

        // 필드가 픽셀 격자를 따라 움직여야 카메라 이동 때 값이 뭉개지지 않는다
        float unitsPerPixel = 1f / pixelsPerUnit;
        Vector2 size = new Vector2(width, height) * unitsPerPixel;
        Vector2 target = (Vector2)transform.position - size * 0.5f;
        target.x = Mathf.Round(target.x * pixelsPerUnit) * unitsPerPixel;
        target.y = Mathf.Round(target.y * pixelsPerUnit) * unitsPerPixel;
        Vector2 scrollUV = (target - origin) / size;
        origin = target;

        Vector4 fieldRect = new Vector4(origin.x, origin.y, size.x, size.y);

        int count = Mathf.Min(GrassEmitter.All.Count, MaxEmitters);
        for (int i = 0; i < count; i++)
        {
            GrassEmitter emitter = GrassEmitter.All[i];
            Rect rect = emitter.WorldRect;
            emitterRects[i] = new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
            emitterVelocities[i] = emitter.Velocity;
        }

        material.SetVector(SimParamsId, new Vector4(stiffness, damping, blur, Mathf.Min(Time.deltaTime, maxDeltaTime)));
        material.SetVector(FieldRectId, fieldRect);
        material.SetVector(ScrollUVId, scrollUV);
        material.SetFloat(EmitterCountId, count);
        material.SetVectorArray(EmitterRectsId, emitterRects);
        material.SetVectorArray(EmitterVelocitiesId, emitterVelocities);

        (previous, current) = (current, previous);
        Graphics.Blit(previous, current, material);

        Shader.SetGlobalTexture(VelocityTextureId, current);
        Shader.SetGlobalVector(FieldRectGlobalId, fieldRect);
    }

    private void EnsureTextures(int width, int height)
    {
        if (current != null && current.width == width && current.height == height) return;

        ReleaseTextures();
        current = CreateTexture(width, height);
        previous = CreateTexture(width, height);
        origin = (Vector2)transform.position - new Vector2(width, height) * 0.5f / pixelsPerUnit;
    }

    private static RenderTexture CreateTexture(int width, int height)
    {
        var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };
        texture.Create();

        // 새 렌더 텍스처 내용은 보장되지 않으므로 비워 둔다
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(false, true, Color.clear);
        RenderTexture.active = active;
        return texture;
    }

    private void ReleaseTextures()
    {
        if (current != null) { current.Release(); Destroy(current); }
        if (previous != null) { previous.Release(); Destroy(previous); }
        current = null;
        previous = null;
    }
}
