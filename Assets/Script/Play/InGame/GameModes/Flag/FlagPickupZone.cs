using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// FlagPickupZone : 깃발이 놓인 자리 (기존 FlagPickupZone 대체)
//   ownerTeam 의 깃발 자리. 상대 팀 "내 캐릭터"가 minStaySeconds 이상 머물면 호스트에게 탈취를 요청한다.
//   (판정은 호스트가 한다. 이미 누가 들고 있으면 거절됨)
//   Collider2D 는 Trigger 로 자동 설정.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider2D))]
public class FlagPickupZone : MonoBehaviour
{
    [Tooltip("이 자리에 놓인 깃발의 주인 팀")]
    public NetTeam ownerTeam = NetTeam.Red;

    [Tooltip("자리에 이만큼 머물러야 탈취를 요청합니다. (너무 짧으면 스쳐 지나가도 탈취됨)")]
    public float minStaySeconds = 0.05f;

    private float stayStart = -1f;

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
        gameObject.layer = LayerMask.NameToLayer("Default");
    }

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        var entity = other.GetComponentInParent<PlayerEntity>();
        if (entity == null || !entity.IsMine) return;           // 내 캐릭터만 요청 (남의 캐릭터는 그쪽 기기가 요청)

        if (stayStart < 0f) stayStart = Time.time;
        if (Time.time - stayStart < minStaySeconds) return;

        var mode = FlagMode.Instance;
        if (mode != null) mode.RequestPickup(ownerTeam);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var entity = other.GetComponentInParent<PlayerEntity>();
        if (entity != null && entity.IsMine) stayStart = -1f;
    }

    private void OnDisable()
    {
        stayStart = -1f;
    }
}
