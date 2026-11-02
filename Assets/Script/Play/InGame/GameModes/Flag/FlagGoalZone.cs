using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// FlagGoalZone : 득점 지점 (기존 FlagGoalZone 대체)
//   goalTeam 의 골 지점. goalTeam 소속 "내 캐릭터"가 적 깃발을 들고 들어오면 호스트에게 득점을 요청한다.
//   들어와 있는 동안에도 일정 간격으로 재요청한다. (요청이 유실되거나 호스트가 바뀐 경우 대비)
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider2D))]
public class FlagGoalZone : MonoBehaviour
{
    [Tooltip("이 골 지점으로 득점하는 팀")]
    public NetTeam goalTeam = NetTeam.Red;

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

    private void OnTriggerEnter2D(Collider2D other) { TryRequest(other); }
    private void OnTriggerStay2D(Collider2D other) { TryRequest(other); }

    private void TryRequest(Collider2D other)
    {
        var entity = other.GetComponentInParent<PlayerEntity>();
        if (entity == null || !entity.IsMine) return;
        if (entity.Team != goalTeam) return;

        var mode = FlagMode.Instance;
        if (mode != null) mode.RequestScore(goalTeam);
    }
}
