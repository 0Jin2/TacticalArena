using UnityEngine;

// 코드로 만드는 걷기 사이클.
// 픽셀아트 느낌을 위해 포즈를 animFps 간격으로만 갱신하고(계단식), 각도/위치를 스냅한다.
public class CutoutWalk : MonoBehaviour
{
    [SerializeField] private CutoutRig rig;
    [SerializeField] private RigTestMover mover;

    [SerializeField] private bool previewWalk;              // 켜면 입력 없이 제자리 걷기 (값 조절용)

    [Header("타이밍")]
    [SerializeField] private float cyclesPerSecond = 1.5f; // 최고 속도일 때 1초당 걸음 사이클 수
    [SerializeField] private float animFps = 12f;          // 포즈 갱신 빈도 (0이면 매 프레임 = 부드럽게)

    [Header("스냅")]
    [SerializeField] private float angleStep = 0f;         // 각도 스냅 단위 (0이면 끔, 예: 15)
    [SerializeField] private float pixelsPerUnit = 64f;    // 몸 들썩임 픽셀 스냅용

    [Header("동작 크기 (도)")]
    [SerializeField] private float thighSwing = 25f;
    [SerializeField] private float kneeBend = 35f;
    [SerializeField] private float armSwing = 25f;
    [SerializeField] private float elbowBend = 15f;
    [SerializeField] private float bodyBobPx = 1f;         // 몸통 위아래 들썩임 (픽셀)
    [SerializeField] private float bendDirection = 1f;     // 무릎이 반대로 꺾이면 -1

    private Transform torso, armUpperF, armLowerF, armUpperB, armLowerB;
    private Transform legUpperF, legLowerF, legUpperB, legLowerB;
    private Vector3 torsoRestPos;

    private float phase;       // 0~1 걸음 사이클 진행도
    private float weight;      // 0(서 있음) ~ 1(걷는 중), 시작/정지 시 부드럽게 전환
    private float frameTimer;

    void Start()
    {
        torso = rig.GetJoint("torso");
        armUpperF = rig.GetJoint("arm_upper_F");
        armLowerF = rig.GetJoint("arm_lower_F");
        armUpperB = rig.GetJoint("arm_upper_B");
        armLowerB = rig.GetJoint("arm_lower_B");
        legUpperF = rig.GetJoint("leg_upper_F");
        legLowerF = rig.GetJoint("leg_lower_F");
        legUpperB = rig.GetJoint("leg_upper_B");
        legLowerB = rig.GetJoint("leg_lower_B");
        if (torso != null) torsoRestPos = torso.localPosition;
    }

    void Update()
    {
        float speed = previewWalk ? 1f : (mover != null ? mover.NormalizedSpeed : 0f);
        weight = Mathf.MoveTowards(weight, speed > 0f ? 1f : 0f, Time.deltaTime * 6f);
        phase = Mathf.Repeat(phase + Time.deltaTime * cyclesPerSecond * speed, 1f);

        // 계단식 갱신: animFps 간격이 될 때만 포즈를 바꾼다
        if (animFps > 0f)
        {
            frameTimer += Time.deltaTime;
            if (frameTimer < 1f / animFps) return;
            frameTimer %= 1f / animFps;
        }

        ApplyPose();
    }

    private void ApplyPose()
    {
        float t = phase * Mathf.PI * 2f;
        float swing = Mathf.Sin(t);
        float w = weight;

        // 다리: 앞/뒤 반대로 흔들고, 다리가 뒤로 가며 들릴 때 무릎을 굽힌다
        SetAngle(legUpperF, thighSwing * swing * w);
        SetAngle(legUpperB, -thighSwing * swing * w);
        SetAngle(legLowerF, -bendDirection * kneeBend * Mathf.Max(0f, Mathf.Cos(t)) * w);
        SetAngle(legLowerB, -bendDirection * kneeBend * Mathf.Max(0f, -Mathf.Cos(t)) * w);

        // 팔: 같은 쪽 다리와 반대로
        SetAngle(armUpperF, -armSwing * swing * w);
        SetAngle(armUpperB, armSwing * swing * w);
        SetAngle(armLowerF, bendDirection * elbowBend * (0.5f + 0.5f * swing) * w);
        SetAngle(armLowerB, bendDirection * elbowBend * (0.5f - 0.5f * swing) * w);

        // 몸통: 발이 땅을 디딜 때(다리가 벌어질 때) 내려가고, 교차할 때 올라간다
        if (torso != null)
        {
            float bob = Mathf.Round(Mathf.Abs(Mathf.Cos(t)) * bodyBobPx * w);
            torso.localPosition = torsoRestPos + new Vector3(0f, bob / pixelsPerUnit, 0f);
        }
    }

    private void SetAngle(Transform joint, float angle)
    {
        if (joint == null) return;
        if (angleStep > 0f) angle = Mathf.Round(angle / angleStep) * angleStep;
        joint.localRotation = Quaternion.Euler(0f, 0f, angle);
    }
}
