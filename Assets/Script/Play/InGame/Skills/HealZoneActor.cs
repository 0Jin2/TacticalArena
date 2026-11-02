using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// HealZoneActor : 윤서희 회복 구역 프리팹에 붙는 스크립트 (기존 HealZoneBehaviour 대체)
//
//  - 모든 기기가 같은 구역을 각자 만들어 연출을 보여 준다.
//  - 실제 회복은 "구역 주인의 기기"만 계산해서, 구역 안의 같은 팀 대상에게 회복 Damage 패킷을 보낸다.
//    (받은 캐릭터의 주인 기기가 체력을 올린다)
//  - 기존과 같이 시작 후 interval 마다 회복하며, duration 직전까지만 회복한다.
//  - 프리팹 요구사항: Collider2D(트리거로 자동 설정). 기존 NetworkObject / HealZoneBehaviour 제거.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider2D))]
public class HealZoneActor : MonoBehaviour
{
    [Header("회복 대상")]
    [Tooltip("true 면 아군 방벽 같은 설치물도 회복한다. (기존 동작 아님)")]
    public bool healDeployables = false;
    [Tooltip("회복 대상을 찾을 레이어. 비우면 모든 레이어")]
    public LayerMask targetLayers = ~0;

    public uint OwnerSession { get; private set; }
    public float Duration { get; private set; }
    public float Interval { get; private set; }
    public int HealAmount { get; private set; }

    private Collider2D zoneCollider;
    private readonly List<Collider2D> overlaps = new List<Collider2D>();
    private readonly HashSet<ulong> alreadyHealed = new HashSet<ulong>();

    private void Awake()
    {
        zoneCollider = GetComponent<Collider2D>();
        zoneCollider.isTrigger = true;
    }

    public void Initialize(uint owner, float duration, float interval, int amount)
    {
        OwnerSession = owner;
        Duration = duration;
        Interval = Mathf.Max(0.05f, interval);
        HealAmount = amount;

        var net = InGameNet.Instance;
        bool mine = net != null && net.LocalSessionId == owner;

        StartCoroutine(Co_Lifetime(mine));
    }

    private IEnumerator Co_Lifetime(bool isOwnerDevice)
    {
        float elapsed = 0f;
        while (elapsed < Duration)
        {
            yield return new WaitForSeconds(Interval);
            elapsed += Interval;
            // 기존 동작과 같게: 마지막 틱(지속시간 도달 시점)은 회복하지 않고 사라진다
            if (isOwnerDevice && elapsed < Duration) HealTick();
        }
        Destroy(gameObject);
    }

    private void HealTick()
    {
        var net = InGameNet.Instance;
        var ownerInfo = net != null ? net.GetPlayer(OwnerSession) : null;
        if (ownerInfo == null) return;

        var filter = new ContactFilter2D();
        filter.useTriggers = true;
        filter.useLayerMask = true;
        filter.layerMask = targetLayers;

        overlaps.Clear();
        zoneCollider.Overlap(filter, overlaps);

        alreadyHealed.Clear();
        for (int i = 0; i < overlaps.Count; i++)
        {
            var target = overlaps[i].GetComponentInParent<IDamageTarget>();
            if (target == null || !target.IsAlive) continue;
            if (target.Kind == DamageTargetKind.Deployable && !healDeployables) continue;
            if (target.Team != ownerInfo.Team) continue;

            ulong key = ((ulong)target.OwnerSession << 8) | target.Slot;
            if (!alreadyHealed.Add(key)) continue;       // 콜라이더가 여러 개여도 한 번만

            DamageRegistry.Send(target, OwnerSession, HealAmount, DamageFlags.Heal);
        }
    }
}
