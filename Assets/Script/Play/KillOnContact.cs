using System.Collections.Generic;
using UnityEngine;
using Fusion;

/// <summary>
/// 이 오브젝트의 콜라이더와 '플레이어 프리팹(Health 보유)'가 접촉하면 즉시 사망 처리.
/// - 트리거/충돌 모두 대응 (OnTriggerEnter2D, OnCollisionEnter2D)
/// - 기본적으로 Player 레이어만 처리하도록 옵션 제공
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class KillOnContact : MonoBehaviour
{
    [Header("대상 필터")]
    [Tooltip("true면 'Player' 레이어만 처리합니다.")]
    [SerializeField] private bool onlyPlayerLayer = true;

    [Tooltip("사망 처리 후 같은 대상의 중복 호출을 막기 위한 쿨다운(초)")]
    [SerializeField] private float perTargetCooldown = 0.2f;

    [Header("공격자(가해자) 표시")]
    [Tooltip("가해자(공격자)로 표시할 PlayerRef. 없으면 기본값(default)으로 전달됩니다.")]
    [SerializeField] private PlayerRef attackerOverride = default;

    // 대상별 마지막 처리 시각
    private readonly Dictionary<NetworkObject, float> _lastHitTime = new Dictionary<NetworkObject, float>();

    private void Reset()
    {
        // 낙사지역/스파이크 같은 용도면 대개 트리거 사용 추천
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other) => TryKill(other);
    private void OnCollisionEnter2D(Collision2D collision) => TryKill(collision.collider);

    private void TryKill(Collider2D other)
    {
        // 레이어 필터 (옵션)
        if (onlyPlayerLayer && other.gameObject.layer != LayerMask.NameToLayer("Player"))
            return;

        // Health를 가진 '플레이어 프리팹' 찾기 (자식/부모 포함)
        var health = other.GetComponent<Health>() ?? other.GetComponentInParent<Health>();
        if (health == null) return;                                // 체력 없음
        if (health.IsDead) return;                                 // 이미 사망
        var targetNO = health.Object;                              // 대상 NetworkObject
        if (targetNO == null || !targetNO.IsValid) return;

        // 중복 호출 방지(클라이언트/물리 프레임 차이로 다중 진입 가능)
        if (_lastHitTime.TryGetValue(targetNO, out float lastT))
        {
            if (Time.time - lastT < perTargetCooldown) return;
        }
        _lastHitTime[targetNO] = Time.time;

        // ✅ 서버 권한에서 HP 감소 및 사망 트리거가 실행되도록 RPC 호출
        //    매우 큰 데미지를 넣어 즉시 0이 되게 처리
        //    (Health가 StateAuthority에서 currentHP를 깎고, 0이면 OnDeath 호출)
        health.RPC_ApplyDamage(int.MaxValue, attackerOverride);
    }
}
