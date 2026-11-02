using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerEffects : 발 먼지 / 착지 먼지 (기존 PlayerEffectHandler 대체)
//
//  - SpawnFootDust() 는 달리기 애니메이션의 Animation Event 에서 호출한다.
//    (이벤트 함수 이름이 같으므로 기존 애니메이션 클립을 수정할 필요 없음)
//    → 이 컴포넌트는 "Animator 가 붙은 오브젝트"에 붙인다. (기존과 동일)
//  - 공중에서 땅으로 내려오는 순간 착지 먼지를 1번 만든다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerEffects : MonoBehaviour
{
    [Header("발 먼지")]
    public GameObject footDustEffectPrefab;
    public Transform footDustSocket;

    [Header("착지 먼지")]
    public GameObject landingDustEffectPrefab;
    public Transform landingEffectPoint;

    private PlayerEntity entity;
    private bool wasGrounded = true;

    private void Awake()
    {
        entity = GetComponentInParent<PlayerEntity>();
    }

    private void Update()
    {
        if (entity == null || !entity.IsInitialized || entity.Motor == null) return;
        if (entity.Health != null && entity.Health.IsDead) { wasGrounded = true; return; }

        bool grounded = entity.Motor.IsGrounded;
        if (!wasGrounded && grounded) SpawnLandingDust();
        wasGrounded = grounded;
    }

    /// <summary>Animation Event 용 (함수 이름 변경 금지)</summary>
    public void SpawnFootDust()
    {
        if (footDustEffectPrefab == null || footDustSocket == null) return;

        var dust = Instantiate(footDustEffectPrefab, footDustSocket.position, Quaternion.identity);

        // 캐릭터가 보는 방향에 맞춰 먼지도 좌우 반전
        float flip = entity != null && entity.Motor != null ? entity.Motor.Facing : 1f;
        var scale = dust.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * flip;
        dust.transform.localScale = scale;
    }

    private void SpawnLandingDust()
    {
        if (landingDustEffectPrefab == null || landingEffectPoint == null) return;
        Instantiate(landingDustEffectPrefab, landingEffectPoint.position, Quaternion.identity);
    }
}
