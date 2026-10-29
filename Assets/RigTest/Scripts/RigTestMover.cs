using UnityEngine;

// 리깅 테스트용 간단 이동 (WASD, 벨트스크롤 방식)
public class RigTestMover : MonoBehaviour
{
    [Tooltip("이동 속도 (유닛/초, 64px = 1유닛)")]
    [Min(0f)] [SerializeField] private float speed = 0.5f;
    [Tooltip("이동 가능 범위 (왼쪽 아래)")]
    [SerializeField] private Vector2 minBounds = new Vector2(-1.3f, -0.85f);
    [Tooltip("이동 가능 범위 (오른쪽 위)")]
    [SerializeField] private Vector2 maxBounds = new Vector2(1.3f, -0.05f);

    [Tooltip("좌우 반전할 대상")]
    [SerializeField] private Transform visual;
    [Tooltip("원본 그림이 오른쪽을 보고 있으면 체크. A/D 방향이 반대로 보이면 바꾸기")]
    [SerializeField] private bool spriteFacesRight = false;

    // 실제 이동 속도 (유닛/초), 걷기 애니메이션이 발 미끄러짐 없게 맞추는 데 씀
    public float Speed { get; private set; }
    public float MaxSpeed => speed;
    public bool SpriteFacesRight => spriteFacesRight;

    void Update()
    {
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector2 before = transform.position;
        Vector2 pos = before + input * speed * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, minBounds.x, maxBounds.x);
        pos.y = Mathf.Clamp(pos.y, minBounds.y, maxBounds.y);
        transform.position = pos;

        // 벽(경계)에 막히면 실제로 움직인 만큼만
        Speed = Time.deltaTime > 0f ? Vector2.Distance(before, pos) / Time.deltaTime : 0f;

        if (visual != null && input.x != 0f)
        {
            float facing = Mathf.Sign(input.x) * (spriteFacesRight ? 1f : -1f);
            Vector3 s = visual.localScale;
            s.x = Mathf.Abs(s.x) * facing;
            visual.localScale = s;
        }
    }
}
