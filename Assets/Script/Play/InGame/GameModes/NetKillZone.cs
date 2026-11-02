using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// NetKillZone : 닿으면 즉사하는 지역 (낙사 구역 / 가시)  (기존 KillOnContact 대체)
//
//  - 판정은 "그 캐릭터의 주인 기기"에서만 한다. (다른 기기의 화면 속 캐릭터는 위치만 따라가므로 판정하지 않음)
//    주인 기기가 사망 처리하면 PlayerDied 패킷으로 모두에게 알려진다.
//  - 트리거(OnTriggerEnter2D)와 충돌(OnCollisionEnter2D) 둘 다 대응
//  - 기존과 달리 "최근에 때린 사람"을 가해자로 인정할 수 있다. (낙하 모드에서 밀어서 떨어뜨리면 킬 인정)
//    기존 동작(가해자 없음)으로 하려면 creditWindowSeconds 를 0 으로.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider2D))]
public class NetKillZone : MonoBehaviour
{
    [Header("대상 필터")]
    [Tooltip("true 면 Player 레이어만 처리합니다.")]
    [SerializeField] private bool onlyPlayerLayer = true;

    [Tooltip("같은 대상에게 이 시간 안에는 다시 처리하지 않습니다.")]
    [SerializeField] private float perTargetCooldown = 0.2f;

    [Header("킬 인정")]
    [Tooltip("이 시간(초) 안에 나를 공격한 사람이 있으면 그 사람의 킬로 인정합니다. 0 이면 인정 안 함(기존 동작)")]
    [SerializeField] private float creditWindowSeconds = 5f;

    private readonly Dictionary<PlayerEntity, float> lastHitTime = new Dictionary<PlayerEntity, float>();

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other) { TryKill(other); }
    private void OnTriggerStay2D(Collider2D other) { TryKill(other); }         // 무적이 끝난 뒤에도 안에 있으면 처리
    private void OnCollisionEnter2D(Collision2D collision) { TryKill(collision.collider); }

    private void TryKill(Collider2D other)
    {
        if (onlyPlayerLayer)
        {
            int playerLayer = LayerMask.NameToLayer(GameLayers.PlayerName);
            if (playerLayer >= 0 && other.gameObject.layer != playerLayer) return;
        }

        var entity = other.GetComponentInParent<PlayerEntity>();
        if (entity == null || !entity.IsMine) return;                // 내 캐릭터만 내 기기에서 처리
        var health = entity.Health;
        if (health == null || health.IsDead) return;

        float last;
        if (lastHitTime.TryGetValue(entity, out last) && Time.time - last < perTargetCooldown) return;
        lastHitTime[entity] = Time.time;

        // 매치 중에만 (카운트다운/결과 화면에서 죽지 않게)
        var flow = MatchFlow.Instance;
        if (flow != null && flow.Phase != MatchPhase.Playing) return;

        uint attacker = creditWindowSeconds > 0f ? health.GetRecentAttacker(creditWindowSeconds) : 0u;
        health.Kill(attacker);
    }
}
