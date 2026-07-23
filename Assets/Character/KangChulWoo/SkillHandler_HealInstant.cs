using UnityEngine;
using System.Collections;
using Fusion;

public class SkillHandler_HealInstant : SkillHandler
{
    [Header("즉시 회복 스킬 설정")]
    public GameObject healEffectPrefab;        // 회복 이펙트 프리팹
    public Transform effectSpawnPoint;         // 이펙트 생성 위치 (Inspector에서 할당)

    [Header("회복/쿨다운 옵션")]
    public int totalHealAmount = 100;          // 총 회복량
    public float healDuration = 1f;            // 몇 초에 걸쳐서 회복할지

    protected override IEnumerator DoSkill()
    {
        // 1) 호스트에서만 모든 클라이언트에 이펙트 생성 RPC 호출
        if (Object.HasStateAuthority)
            RPC_SpawnHealEffect();

        // 2) 호스트에서만 체력 회복
        var health = controller.GetComponent<Health>();
        if (health != null && health.Object.HasStateAuthority)
        {
            float elapsed = 0f;
            while (elapsed < healDuration)
            {
                float delta = Time.deltaTime;
                int healTick = Mathf.RoundToInt(totalHealAmount * (delta / healDuration));
                if (healTick > 0)
                {
                    health.currentHP = Mathf.Min(
                        health.statData.maxHP,
                        health.currentHP + healTick
                    );
                }
                elapsed += delta;
                yield return null;
            }
        }

        // 3) 스킬 쿨타임 시작 및 UI 업데이트
        skillCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.skillCooldown);
        StartCoroutine(UpdateSkillCooldownUI());

        EndSkill();
    }

    // ─── 모든 클라이언트에서 호출 ───
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SpawnHealEffect()
    {
        if (healEffectPrefab == null) return;

        Vector3 pos = effectSpawnPoint != null
            ? effectSpawnPoint.position
            : controller.transform.position;

        Instantiate(
            healEffectPrefab,
            pos,
            Quaternion.identity,
            controller.transform
        );
        // DestroySelfOnAnimationEnd 컴포넌트가 Animator 종료 후 자동 파괴합니다.
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