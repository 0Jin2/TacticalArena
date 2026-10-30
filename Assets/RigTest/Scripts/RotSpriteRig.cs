using System.Collections.Generic;
using UnityEngine;

// 관절 각도를 angleStep 단위로 스냅하고, 파츠를 실제로 돌리는 대신 미리 구운 RotSprite 회전본으로 교체한다.
// → 손으로 돌려 그린 도트처럼 1px 계단이 깔끔하게 유지됨
// 걷기(CutoutWalk)가 Update에서 각도를 정한 뒤 LateUpdate에서 적용
public class RotSpriteRig : MonoBehaviour
{
    [SerializeField] private CutoutRig rig;
    [SerializeField] private RotSpriteSet set;
    [Tooltip("끄면 일반 회전으로 그린다 (비교용, 플레이 중 전환 가능)")]
    [SerializeField] private bool useRotSprite = true;
    [Tooltip("회전본을 몇 도 간격으로 구울지. 작을수록 회전이 부드럽지만 장 수가 늘어남 (15 → 24장, 7.5 → 48장).\n바꾼 뒤 '회전본 다시 굽기'를 눌러야 적용")]
    [SerializeField] private float bakeAngleStep = 15f;

    public float BakeAngleStep => bakeAngleStep;

    private class Slot
    {
        public string name;
        public string parent;
        public Transform joint;
        public Transform part;
        public SpriteRenderer renderer;
        public Sprite originalSprite;
        public Vector3 originalLocalPos;
        public Sprite[] sprites;
        public Vector2[] pivots; // 유닛 단위
        public float angle;      // 모델 기준 누적 각도
    }

    private readonly List<Slot> slots = new List<Slot>();
    private readonly Dictionary<string, Slot> slotByName = new Dictionary<string, Slot>();
    private bool applied;
    private int loadedVersion = -1;

    void Start()
    {
        if (rig == null || set == null) return;

        foreach (CutoutRig.Bone bone in rig.Bones)
        {
            Transform joint = rig.GetJoint(bone.name);
            Transform part = rig.GetPart(bone.name);
            if (joint == null || part == null) continue;

            var slot = new Slot
            {
                name = bone.name,
                parent = bone.parent,
                joint = joint,
                part = part,
                renderer = part.GetComponent<SpriteRenderer>(),
                originalLocalPos = part.localPosition,
            };
            slot.originalSprite = slot.renderer.sprite;
            slots.Add(slot);
            slotByName[bone.name] = slot;
        }
        LoadSprites();
    }

    // 구운 데이터로 회전본 스프라이트 생성 (다시 구우면 실행 중에도 다시 호출됨)
    private void LoadSprites()
    {
        loadedVersion = set.version;
        foreach (Slot slot in slots)
        {
            DestroySprites(slot);
            RotSpriteSet.Part data = set.Find(slot.name);
            if (data == null || set.atlas == null) continue;

            slot.sprites = new Sprite[data.frames.Count];
            slot.pivots = new Vector2[data.frames.Count];
            for (int i = 0; i < data.frames.Count; i++)
            {
                RotSpriteSet.Frame f = data.frames[i];
                var rect = new Rect(f.rect.x, f.rect.y, f.rect.width, f.rect.height);
                var pivot = new Vector2(f.pivotPx.x / rect.width, f.pivotPx.y / rect.height);
                slot.sprites[i] = Sprite.Create(set.atlas, rect, pivot, set.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                slot.sprites[i].name = $"{slot.name}_{i * set.angleStep}";
                slot.pivots[i] = f.pivotPx / set.pixelsPerUnit;
            }
        }
    }

    void LateUpdate()
    {
        if (slots.Count == 0) return;
        if (set.version != loadedVersion) LoadSprites();

        if (!useRotSprite)
        {
            if (applied) Restore();
            return;
        }

        Transform model = rig.Model;
        float step = set.angleStep;
        int count = set.FrameCount;
        float ppu = set.pixelsPerUnit;

        // 부모 관절부터 순서대로 처리 (Bones 순서 = 부모 먼저)
        foreach (Slot s in slots)
        {
            if (s.sprites == null) continue;
            float local = Mathf.Round(Mathf.DeltaAngle(0f, s.joint.localEulerAngles.z) / step) * step;
            s.joint.localRotation = Quaternion.Euler(0f, 0f, local);

            float parentAngle = 0f;
            if (!string.IsNullOrEmpty(s.parent) && slotByName.TryGetValue(s.parent, out Slot p)) parentAngle = p.angle;
            s.angle = parentAngle + local;

            int index = ((Mathf.RoundToInt(s.angle / step) % count) + count) % count;
            s.renderer.sprite = s.sprites[index];

            // 회전본 자체에 각도가 들어 있으므로 파츠는 모델 기준 0도로 되돌린다
            s.part.localRotation = Quaternion.Euler(0f, 0f, -s.angle);

            // 스프라이트 왼쪽 아래가 모델 픽셀 격자에 맞도록 위치 스냅
            Vector2 pivot = s.pivots[index];
            Vector2 jointPos = model.InverseTransformPoint(s.joint.position);
            Vector2 corner = jointPos - pivot;
            corner = new Vector2(Mathf.Round(corner.x * ppu), Mathf.Round(corner.y * ppu)) / ppu;
            s.part.position = model.TransformPoint(corner + pivot);
        }
        applied = true;
    }

    private void Restore()
    {
        foreach (Slot s in slots)
        {
            s.renderer.sprite = s.originalSprite;
            s.part.localRotation = Quaternion.identity;
            s.part.localPosition = s.originalLocalPos;
        }
        applied = false;
    }

    void OnDestroy()
    {
        foreach (Slot s in slots) DestroySprites(s);
    }

    private static void DestroySprites(Slot slot)
    {
        if (slot.sprites == null) return;
        foreach (Sprite sprite in slot.sprites) Destroy(sprite);
        slot.sprites = null;
    }
}
