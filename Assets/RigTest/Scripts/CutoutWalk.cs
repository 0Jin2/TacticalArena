using System.Collections.Generic;
using UnityEngine;

// 키 포즈 방식 걷기 사이클 + 대기(숨쉬기).
// 도트 애니메이션처럼 정해진 포즈를 계단식으로 넘기고, 이동 속도에 맞춰 넘겨서 발이 미끄러지지 않게 한다.
// 포즈는 반 사이클(앞다리 기준)만 적으면 나머지 반은 앞/뒤를 바꿔 자동 생성
// 모든 값은 플레이 중 인스펙터에서 바꾸면 바로 반영된다.
public class CutoutWalk : MonoBehaviour
{
    // 각도는 "앞으로 = +" 기준 (캐릭터가 보는 방향). 무릎은 뒤로 접힐수록 -, 팔꿈치는 앞으로 접힐수록 +
    [System.Serializable]
    public class Pose
    {
        [Tooltip("구분용 이름")]
        public string name;
        [Tooltip("이 포즈를 몇 칸 유지할지. 2 이상이면 그 포즈에서 멈칫해 보인다")]
        [Min(1)] public int hold = 1;

        [Tooltip("앞다리 허벅지 각도 (+ = 앞으로)")] public float thighF;
        [Tooltip("앞다리 무릎 각도 (- = 뒤로 접힘)")] public float kneeF;
        [Tooltip("뒷다리 허벅지 각도 (+ = 앞으로)")] public float thighB;
        [Tooltip("뒷다리 무릎 각도 (- = 뒤로 접힘)")] public float kneeB;
        [Tooltip("앞팔 위팔 각도 (+ = 앞으로)")] public float armF;
        [Tooltip("앞팔 팔꿈치 각도 (+ = 앞으로 접힘)")] public float elbowF;
        [Tooltip("뒷팔 위팔 각도 (+ = 앞으로)")] public float armB;
        [Tooltip("뒷팔 팔꿈치 각도 (+ = 앞으로 접힘)")] public float elbowB;
        [Tooltip("몸통 높이 (픽셀, - = 아래)")] public int bodyPx;

        public Pose(string name, int hold, float thighF, float kneeF, float thighB, float kneeB,
                    float armF, float elbowF, float armB, float elbowB, int bodyPx)
        {
            this.name = name;
            this.hold = hold;
            this.thighF = thighF; this.kneeF = kneeF; this.thighB = thighB; this.kneeB = kneeB;
            this.armF = armF; this.elbowF = elbowF; this.armB = armB; this.elbowB = elbowB;
            this.bodyPx = bodyPx;
        }

        // 앞/뒤 팔다리를 바꾼 포즈 (반대쪽 발 차례)
        public Pose Mirrored()
        {
            return new Pose(name + "'", hold, thighB, kneeB, thighF, kneeF, armB, elbowB, armF, elbowF, bodyPx);
        }
    }

    [Header("연결")]
    [SerializeField] private CutoutRig rig;
    [SerializeField] private RigTestMover mover;

    [Header("미리보기")]
    [Tooltip("켜면 키 입력 없이 제자리에서 계속 걷는다 (값 조절용)")]
    [SerializeField] private bool previewWalk;

    [Header("걷기 - 속도")]
    [Tooltip("한 사이클(두 걸음) 동안 몸이 나아가는 거리(픽셀). 클수록 같은 이동 속도에서 걸음이 느려진다. 발이 미끄러지면 이 값을 조절")]
    [Min(1f)] [SerializeField] private float strideLengthPx = 20f;
    [Tooltip("스프라이트 PPU (임포트 설정과 같아야 함)")]
    [SerializeField] private float pixelsPerUnit = 64f;

    [Header("걷기 - 시간차 (포즈 칸 수)")]
    [Tooltip("아래팔이 위팔보다 몇 칸 늦게 따라올지")]
    [Range(0, 3)] [SerializeField] private int elbowLag = 1;
    [Tooltip("머리가 몸통 들썩임보다 몇 칸 늦게 따라올지. 1 이상이면 덜그럭거려 보일 수 있음")]
    [Range(0, 3)] [SerializeField] private int headLag = 0;

    [Header("걷기 - 키 포즈 (반 사이클)")]
    [Tooltip("접지 → 하강 → 교차 → 상승. 나머지 반 사이클은 앞/뒤를 바꿔 자동 생성.\n" +
             "같은 각도가 연달아 나오면 그 칸에서 멈춰 보임. 각도는 RotSprite 각도 단위(기본 15)로 스냅됨")]
    [SerializeField] private List<Pose> halfCycle = new List<Pose>
    {
        new Pose("contact", 1,  30,   0, -30, -15,  -30, 15,  30, 15,  0),
        new Pose("down",    1,  15, -15, -15, -30,  -15, 15,  15, 15, -1),
        new Pose("passing", 1,   0,   0,   0, -45,    0, 15,   0, 15,  0),
        new Pose("up",      1, -15,   0,  15, -30,   15, 15, -15, 15,  1),
    };

    [Header("대기 (숨쉬기)")]
    [Tooltip("대기 한 칸의 길이(초). 클수록 숨쉬기가 느려진다")]
    [Min(0.02f)] [SerializeField] private float idleFrameTime = 0.2f;
    [Tooltip("칸별 몸통 높이(픽셀, - = 아래). 칸 수를 늘리거나 줄여도 됨")]
    [SerializeField] private int[] idleBodyPx = { 0, 0, 0, 0, -1, -1, -1, -1 };
    [Tooltip("칸별 머리 높이(픽셀, 원래 위치 기준). 몸통과 따로 움직인다. 비우면 몸통과 같이 움직임.\n" +
             "Pixel Perfect 카메라는 1px 단위로만 그리므로 소수는 반올림된다 → '살짝'은 1px을 짧게 주는 식으로")]
    [SerializeField] private int[] idleHeadPx = { 0, 0, 0, 0, 0, -1, -1, 0 };

    private Transform torso, head, armUpperF, armLowerF, armUpperB, armLowerB;
    private Transform legUpperF, legLowerF, legUpperB, legLowerB;
    private Vector3 torsoRestPos, headRestPos;

    private readonly List<Pose> steps = new List<Pose>(); // hold를 펼친 전체 사이클
    private float phase;      // 0~1 사이클 진행도
    private int lastStep = -1;
    private float idleTimer;
    private int idleStep = -1;
    private bool started;

    void Start()
    {
        torso = rig.GetJoint("torso");
        head = rig.GetJoint("head");
        armUpperF = rig.GetJoint("arm_upper_F");
        armLowerF = rig.GetJoint("arm_lower_F");
        armUpperB = rig.GetJoint("arm_upper_B");
        armLowerB = rig.GetJoint("arm_lower_B");
        legUpperF = rig.GetJoint("leg_upper_F");
        legLowerF = rig.GetJoint("leg_lower_F");
        legUpperB = rig.GetJoint("leg_upper_B");
        legLowerB = rig.GetJoint("leg_lower_B");
        if (torso != null) torsoRestPos = torso.localPosition;
        if (head != null) headRestPos = head.localPosition;

        BuildSteps();
        started = true;
    }

    // 플레이 중 인스펙터 값을 바꾸면 다음 프레임에 다시 적용
    void OnValidate()
    {
        if (!started) return;
        BuildSteps();
        if (lastStep != -1) lastStep = -2; // 같은 칸이어도 다시 적용되게
        idleStep = -1;
    }

    private void BuildSteps()
    {
        steps.Clear();
        foreach (Pose p in halfCycle) AddHeld(p);
        foreach (Pose p in halfCycle) AddHeld(p.Mirrored());
    }

    private void AddHeld(Pose p)
    {
        for (int i = 0; i < Mathf.Max(1, p.hold); i++) steps.Add(p);
    }

    void Update()
    {
        if (steps.Count == 0) return;

        float speed = previewWalk ? (mover != null ? mover.MaxSpeed : 0.5f) : (mover != null ? mover.Speed : 0f);
        if (speed < 0.01f)
        {
            if (lastStep != -1) ApplyRest();
            UpdateIdle();
            return;
        }
        idleStep = -1;
        idleTimer = 0f;

        // 이동 거리만큼 사이클을 넘김 → 발이 땅에 고정된 것처럼 보임
        float stride = strideLengthPx / pixelsPerUnit;
        phase = Mathf.Repeat(phase + speed * Time.deltaTime / stride, 1f);

        int step = Mathf.Min(Mathf.FloorToInt(phase * steps.Count), steps.Count - 1);
        if (step == lastStep) return;
        lastStep = step;
        ApplyStep(step);
    }

    private void ApplyStep(int step)
    {
        Pose p = steps[step];
        Pose elbowPose = steps[Wrap(step - elbowLag)];
        Pose headPose = steps[Wrap(step - headLag)];
        float fwd = mover == null || mover.SpriteFacesRight ? 1f : -1f; // 오른쪽 보는 그림이면 앞 = 반시계(+)

        SetAngle(legUpperF, fwd * p.thighF);
        SetAngle(legLowerF, fwd * p.kneeF);
        SetAngle(legUpperB, fwd * p.thighB);
        SetAngle(legLowerB, fwd * p.kneeB);

        SetAngle(armUpperF, fwd * p.armF);
        SetAngle(armUpperB, fwd * p.armB);
        SetAngle(armLowerF, fwd * elbowPose.elbowF);
        SetAngle(armLowerB, fwd * elbowPose.elbowB);

        if (torso != null) torso.localPosition = torsoRestPos + new Vector3(0f, p.bodyPx / pixelsPerUnit, 0f);
        // 머리는 몸통에 붙어 있으므로, 이전 포즈 높이에 머물도록 차이만큼 반대로 이동
        if (head != null) head.localPosition = headRestPos + new Vector3(0f, (headPose.bodyPx - p.bodyPx) / pixelsPerUnit, 0f);
    }

    private void ApplyRest()
    {
        lastStep = -1;
        phase = 0f;
        foreach (Transform joint in new[] { legUpperF, legLowerF, legUpperB, legLowerB, armUpperF, armLowerF, armUpperB, armLowerB })
        {
            SetAngle(joint, 0f);
        }
        if (torso != null) torso.localPosition = torsoRestPos;
        if (head != null) head.localPosition = headRestPos;
    }

    private void UpdateIdle()
    {
        if (idleBodyPx == null || idleBodyPx.Length == 0) return;

        idleTimer += Time.deltaTime;
        int step = Mathf.FloorToInt(idleTimer / idleFrameTime) % idleBodyPx.Length;
        if (step == idleStep) return;
        idleStep = step;

        int body = idleBodyPx[step];
        bool headSeparate = idleHeadPx != null && idleHeadPx.Length > 0;
        int headPx = headSeparate ? idleHeadPx[step % idleHeadPx.Length] : body;
        if (torso != null) torso.localPosition = torsoRestPos + new Vector3(0f, body / pixelsPerUnit, 0f);
        // 머리는 몸통 자식이므로 몸통 이동분을 빼서 원하는 높이에 둔다
        if (head != null) head.localPosition = headRestPos + new Vector3(0f, (headPx - body) / pixelsPerUnit, 0f);
    }

    private int Wrap(int i)
    {
        return ((i % steps.Count) + steps.Count) % steps.Count;
    }

    private static void SetAngle(Transform joint, float angle)
    {
        if (joint == null) return;
        joint.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
