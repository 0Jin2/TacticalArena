using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

[RequireComponent(typeof(Collider2D))]
public class HealZoneBehaviour : NetworkBehaviour
{
    [Networked] public PlayerRef owner { get; private set; }    // 스킬 사용 주체
    public float duration = 5f;       // 존 유지 시간
    public float interval = 1f;    // 회복 간격
    public int healAmount = 10;       // 1회 회복량

    private Collider2D triggerCol;
    private readonly List<Health> playersInZone = new List<Health>();

    /// <summary>
    /// Runner.Spawn 의 onBeforeSpawned 에서 호출
    /// </summary>
    public void Initialize(PlayerRef ownerRef, float dur, float interv, int amount)
    {
        owner = ownerRef;
        duration = dur;
        interval = interv;
        healAmount = amount;
    }

    public override void Spawned()
    {
        // 트리거 콜라이더 세팅
        triggerCol = GetComponent<Collider2D>();
        triggerCol.isTrigger = true;

        // 8초 뒤 파괴
        if (Object.HasStateAuthority)
            StartCoroutine(HealLoop());
    }

    private IEnumerator HealLoop()
    {
        float elapsed = 0f;
        var teams = TeamAssigner.Instance;

        while (elapsed < duration)
        {
            if (Object.HasStateAuthority)
            {
                // 현재 존에 있는 플레이어들에 대해 회복 처리
                foreach (var hpComp in playersInZone.ToArray())
                {
                    if (hpComp == null) continue;
                    var netObj = hpComp.GetComponent<NetworkObject>();
                    if (netObj == null) continue;

                    // 같은 팀 체크
                    if (teams != null
                        && teams.Teams.TryGet(owner, out var ownerTeam)
                        && teams.Teams.TryGet(netObj.InputAuthority, out var targetTeam)
                        && ownerTeam == targetTeam)
                    {
                        // 음수 데미지로 회복 (Health.TakeDamage 내부에서 HasStateAuthority 체크)
                        hpComp.TakeDamage(-healAmount, owner);
                    }
                }
            }

            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }

        // 존 파괴
        if (Object.HasStateAuthority)
            Runner.Despawn(Object);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!Object.HasStateAuthority) return;
        var hpComp = other.GetComponent<Health>();
        if (hpComp != null)
            playersInZone.Add(hpComp);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!Object.HasStateAuthority) return;
        var hpComp = other.GetComponent<Health>();
        if (hpComp != null)
            playersInZone.Remove(hpComp);
    }
}
