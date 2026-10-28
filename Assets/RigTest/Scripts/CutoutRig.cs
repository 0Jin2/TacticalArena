using System.Collections.Generic;
using UnityEngine;

// Aseprite에서 임포트한 파츠(레이어별 자식 오브젝트)를 관절 계층으로 재구성한다.
// 관절 위치는 Aseprite 캔버스 픽셀 좌표(왼쪽 위 원점)로 지정 → 그림을 고쳐도 숫자만 맞추면 됨
public class CutoutRig : MonoBehaviour
{
    [System.Serializable]
    public class Bone
    {
        public string name;     // 파츠 이름 = Aseprite 레이어 이름
        public string parent;   // 부모 관절 이름 (비우면 루트)
        public Vector2 pivotPx; // 회전 중심 (캔버스 픽셀, 왼쪽 위 원점)

        public Bone(string name, string parent, float x, float y)
        {
            this.name = name;
            this.parent = parent;
            pivotPx = new Vector2(x, y);
        }
    }

    [SerializeField] private Transform model;                       // 임포트된 aseprite 프리팹 인스턴스
    [SerializeField] private Vector2 canvasSize = new Vector2(32, 32);
    [SerializeField] private float pixelsPerUnit = 64f;

    // 부모가 먼저 오도록 나열
    [SerializeField] private List<Bone> bones = new List<Bone>
    {
        new Bone("torso",       "",            16f,   18f),
        new Bone("head",        "torso",       15f,   8.5f),
        new Bone("arm_upper_B", "torso",       15f,   10f),
        new Bone("arm_lower_B", "arm_upper_B", 13.5f, 13f),
        new Bone("arm_upper_F", "torso",       16.5f, 9.5f),
        new Bone("arm_lower_F", "arm_upper_F", 21.5f, 12.5f),
        new Bone("leg_upper_B", "",            15.5f, 18f),
        new Bone("leg_lower_B", "leg_upper_B", 13.5f, 22f),
        new Bone("leg_upper_F", "",            17.5f, 17.5f),
        new Bone("leg_lower_F", "leg_upper_F", 20f,   21f),
    };

    private readonly Dictionary<string, Transform> joints = new Dictionary<string, Transform>();
    private readonly Dictionary<string, Transform> parts = new Dictionary<string, Transform>();

    public Transform Model => model != null ? model : transform;
    public Vector2 CanvasSize => canvasSize;
    public float PixelsPerUnit => pixelsPerUnit;
    public IReadOnlyList<Bone> Bones => bones;

    public Transform GetJoint(string boneName)
    {
        joints.TryGetValue(boneName, out Transform joint);
        return joint;
    }

    public Transform GetPart(string boneName)
    {
        parts.TryGetValue(boneName, out Transform part);
        return part;
    }

    public static Transform FindPart(Transform root, string name)
    {
        return FindDeep(root, name);
    }

    void Awake()
    {
        Build();
    }

    private void Build()
    {
        if (model == null) model = transform;

        foreach (Bone bone in bones)
        {
            Transform part = FindDeep(model, bone.name);
            if (part == null)
            {
                Debug.LogWarning($"[CutoutRig] '{bone.name}' 파츠를 찾을 수 없습니다.");
                continue;
            }

            Transform parent = model;
            if (!string.IsNullOrEmpty(bone.parent) && !joints.TryGetValue(bone.parent, out parent))
            {
                Debug.LogWarning($"[CutoutRig] '{bone.name}'의 부모 '{bone.parent}' 관절이 없습니다.");
                parent = model;
            }

            Transform joint = new GameObject("j_" + bone.name).transform;
            joint.SetParent(parent, false);
            joint.position = model.TransformPoint(PixelToLocal(bone.pivotPx));
            part.SetParent(joint, true);
            joints[bone.name] = joint;
            parts[bone.name] = part;
        }
    }

    // 캔버스 픽셀 → 모델 로컬 좌표 (임포트 피벗 = 캔버스 하단 중앙)
    private Vector3 PixelToLocal(Vector2 px)
    {
        return new Vector3((px.x - canvasSize.x * 0.5f) / pixelsPerUnit,
                           (canvasSize.y - px.y) / pixelsPerUnit, 0f);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name) return child;
        }
        return null;
    }

    // 선택 시 관절 위치 표시 (좌표 맞출 때 확인용)
    void OnDrawGizmosSelected()
    {
        Transform root = model != null ? model : transform;
        Gizmos.color = Color.yellow;
        foreach (Bone bone in bones)
        {
            Vector3 p = Application.isPlaying && joints.TryGetValue(bone.name, out Transform j)
                ? j.position
                : root.TransformPoint(PixelToLocal(bone.pivotPx));
            Gizmos.DrawWireSphere(p, 1f / pixelsPerUnit);
        }
    }
}
