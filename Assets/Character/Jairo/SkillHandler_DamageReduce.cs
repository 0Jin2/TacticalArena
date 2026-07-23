using UnityEngine;
using System.Collections;
using Fusion;

public class SkillHandler_DamageReduce : SkillHandler
{
    [Header("피해 감소 이펙트")]
    public GameObject reduceEffectPrefab;
    public Transform effectSpawnPoint;

    private Health health;
    private GameObject spawnedEffect;       // 이펙트 참조 보관

    public float reducePercent = 0.5f;      // 50% 피해 감소
    public float duration = 3f;             // 이펙트/버프 유지 시간

    protected override IEnumerator DoSkill()
    {
        health = controller.GetComponent<Health>();

        // 1) 호스트에서만 Spawn RPC 호출 → 모든 클라이언트에서 Instantiate
        if (Object.HasStateAuthority)
            RPC_SpawnReduceEffect();

        // 2) 즉시 데미지 경감 적용
        health?.SetDamageReduction(reducePercent);

        // 3) 스킬 쿨다운 시작 (UI는 쿨다운 전체에 걸쳐 Update)
        skillCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.skillCooldown);
        StartCoroutine(UpdateSkillCooldownUI());

        // 4) duration 후 데미지 경감 해제 + Remove RPC 호출
        yield return new WaitForSeconds(duration);
        health?.SetDamageReduction(0f);

        if (Object.HasStateAuthority)
            RPC_RemoveReduceEffect();

        EndSkill();
    }

    // ─── Spawn RPC ───
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnReduceEffect()
    {
        if (reduceEffectPrefab == null) return;

        Vector3 pos = effectSpawnPoint != null
            ? effectSpawnPoint.position
            : controller.transform.position;

        spawnedEffect = Instantiate(
            reduceEffectPrefab,
            pos,
            Quaternion.identity,
            controller.transform
        );
    }

    // ─── Remove RPC ───
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_RemoveReduceEffect()
    {
        if (spawnedEffect != null)
        {
            Destroy(spawnedEffect);
            spawnedEffect = null;
        }
    }

    private IEnumerator UpdateSkillCooldownUI()
    {
        float total = statData.skillCooldown;
        while (skillCooldownTimer.IsRunning)
        {
            float rem = skillCooldownTimer.RemainingTime(Runner) ?? 0f;
            GamePlayUIController.Instance?.UpdateSkillButtonFill(1f - Mathf.Clamp01(rem / total));
            yield return null;
        }
        GamePlayUIController.Instance?.UpdateSkillButtonFill(1f);
    }
}