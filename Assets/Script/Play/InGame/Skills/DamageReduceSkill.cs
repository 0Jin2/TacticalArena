using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// DamageReduceSkill : 자이로 - 피해 감소
//   duration 초 동안 받는 피해를 reducePercent 만큼 줄이고, 방어 이펙트를 몸에 붙인다.
// ─────────────────────────────────────────────────────────────────────────────

public class DamageReduceSkill : SkillBase
{
    [Header("피해 감소")]
    public GameObject reduceEffectPrefab;                  // 피해 감소 이펙트 프리팹
    public Transform effectSpawnPoint;                     // 이펙트가 생길 위치
    [Range(0f, 1f)] public float reducePercent = 0.5f;     // 0.5 = 50% 감소
    public float duration = 3f;                            // 피해 감소 지속 시간(초)

    private const byte PhaseEffect = 1;     // [켜짐? bool]

    private GameObject spawnedEffect;

    protected override IEnumerator Run()
    {
        Broadcast(PhaseEffect, w => w.WriteBool(true));
        Entity.Health.SetDamageReduction(reducePercent);

        yield return new WaitForSeconds(duration);

        Entity.Health.SetDamageReduction(0f);
        Broadcast(PhaseEffect, w => w.WriteBool(false));
    }

    protected override void OnNetEvent(byte phase, NetReader r)
    {
        if (phase != PhaseEffect) return;
        if (r.ReadBool()) SpawnEffect(); else RemoveEffect();
    }

    private void SpawnEffect()
    {
        RemoveEffect();
        if (reduceEffectPrefab == null) return;
        Vector3 pos = effectSpawnPoint != null ? effectSpawnPoint.position : Entity.transform.position;
        spawnedEffect = Instantiate(reduceEffectPrefab, pos, Quaternion.identity, Entity.transform);
    }

    private void RemoveEffect()
    {
        if (spawnedEffect != null) Destroy(spawnedEffect);
        spawnedEffect = null;
    }

    protected override void OnCancel()
    {
        RemoveEffect();
        if (Entity != null && Entity.IsMine && Entity.Health != null) Entity.Health.SetDamageReduction(0f);
    }
}
