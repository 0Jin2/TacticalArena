using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// HealInstantSkill : 강철우 - 자가 회복
//   healDuration 초에 걸쳐 totalHealAmount 만큼 체력을 회복한다. (최대 체력 초과 불가)
// ─────────────────────────────────────────────────────────────────────────────

public class HealInstantSkill : SkillBase
{
    [Header("회복")]
    public GameObject healEffectPrefab;       // 회복 이펙트 (애니메이션 끝에 스스로 사라지게: AnimationEventDestroy)
    public Transform effectSpawnPoint;
    public int totalHealAmount = 100;
    public float healDuration = 1f;

    private const byte PhaseEffect = 1;

    protected override IEnumerator Run()
    {
        Broadcast(PhaseEffect);

        float elapsed = 0f;
        float carry = 0f;                       // 소수점 회복량이 버려지지 않도록 모아 둔다
        float duration = Mathf.Max(0.01f, healDuration);

        while (elapsed < duration)
        {
            float delta = Time.deltaTime;
            carry += totalHealAmount * (delta / duration);
            int whole = Mathf.FloorToInt(carry);
            if (whole > 0)
            {
                Entity.Health.Heal(whole);
                carry -= whole;
            }
            elapsed += delta;
            yield return null;
        }

        int rest = Mathf.RoundToInt(carry);
        if (rest > 0) Entity.Health.Heal(rest);
    }

    protected override void OnNetEvent(byte phase, NetReader r)
    {
        if (phase != PhaseEffect || healEffectPrefab == null) return;
        Vector3 pos = effectSpawnPoint != null ? effectSpawnPoint.position : Entity.transform.position;
        Instantiate(healEffectPrefab, pos, Quaternion.identity, Entity.transform);
    }
}
