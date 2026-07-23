using UnityEngine;

public class PlayerEffectHandler : MonoBehaviour
{
    [Header("발 먼지 이펙트 프리팹 및 위치")]
    public GameObject footDustEffectPrefab;
    public Transform footDustSocket;

    [Header("착지 먼지 이펙트 프리팹 및 위치")]
    public GameObject landingDustEffectPrefab;
    public Transform landingEffectPoint;

    private bool wasGrounded = true;
    private PlayerAnimationHandler animHandler; // 부모~자식 참조 위해
    private PlayerNetworkController netController;

    private void Awake()
    {
        animHandler = GetComponent<PlayerAnimationHandler>();
        if (animHandler == null)
            animHandler = GetComponentInParent<PlayerAnimationHandler>();

        netController = GetComponent<PlayerNetworkController>();
        if (netController == null)
            netController = GetComponentInParent<PlayerNetworkController>();
    }

    private void Update()
    {
        // 애니메이션이 갱신한 isGrounded 값을 감시(플레이어마다 다르게 구현되어있을 수 있음)
        bool grounded = false;
        if (animHandler != null)
            grounded = animHandler.isGrounded;

        // ground false→true로 변할 때 착지 이펙트 1회 생성
        if (!wasGrounded && grounded)
        {
            SpawnLandingDust();
        }
        wasGrounded = grounded;
    }

    public void SpawnFootDust()
    {
        if (footDustEffectPrefab == null || footDustSocket == null)
            return;

        // Instantiate: 부모 없이, 위치는 소켓, 회전은 항상 바닥 기준
        GameObject dust = Instantiate(
            footDustEffectPrefab,
            footDustSocket.position,
            Quaternion.identity // 바닥 평행
        );

        // flip 방향 적용 (캐릭터 좌우반전 따라감)
        float flipX = 1f;
        if (netController != null)
            flipX = Mathf.Sign(netController.NetFlipXValue);

        Vector3 scale = dust.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * flipX;
        dust.transform.localScale = scale;
    }

    private void SpawnLandingDust()
    {
        if (landingDustEffectPrefab == null || landingEffectPoint == null) return;
        Instantiate(
            landingDustEffectPrefab,
            landingEffectPoint.position,
            Quaternion.identity
        );
    }
}
