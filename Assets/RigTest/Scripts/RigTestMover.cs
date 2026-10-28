using UnityEngine;

// 리깅 테스트용 간단 이동 (WASD, 벨트스크롤 방식)
public class RigTestMover : MonoBehaviour
{
    [SerializeField] private float speed = 1.2f;
    [SerializeField] private Vector2 minBounds = new Vector2(-1.3f, -0.85f);
    [SerializeField] private Vector2 maxBounds = new Vector2(1.3f, -0.05f);

    [SerializeField] private Transform visual;           // 좌우 반전할 대상
    [SerializeField] private bool spriteFacesRight = false; // 원본 그림이 오른쪽을 보고 있는지

    // 0(정지) ~ 1(최고 속도), 걷기 애니메이션이 읽어감
    public float NormalizedSpeed { get; private set; }

    void Update()
    {
        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector2 pos = (Vector2)transform.position + input * speed * Time.deltaTime;
        pos.x = Mathf.Clamp(pos.x, minBounds.x, maxBounds.x);
        pos.y = Mathf.Clamp(pos.y, minBounds.y, maxBounds.y);
        transform.position = pos;

        NormalizedSpeed = input.magnitude;

        if (visual != null && input.x != 0f)
        {
            float facing = Mathf.Sign(input.x) * (spriteFacesRight ? 1f : -1f);
            Vector3 s = visual.localScale;
            s.x = Mathf.Abs(s.x) * facing;
            visual.localScale = s;
        }
    }
}
