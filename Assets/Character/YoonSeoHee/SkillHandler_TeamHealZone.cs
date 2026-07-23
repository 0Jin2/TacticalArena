using System.Collections;
using UnityEngine;
using Fusion;

public class SkillHandler_TeamHealZone : SkillHandler
{
    [Header("힐존 스킬 (Network Prefab)")]
    public NetworkPrefabRef healZonePrefab;
    public Transform spawnPoint;

    [Header("옵션")]
    public float zoneDuration = 5f;
    public float healInterval = 1f;
    public int healAmount = 10;

    protected override IEnumerator DoSkill()
    {
        if (Object.HasStateAuthority && healZonePrefab != null)
        {
            Runner.Spawn(
                healZonePrefab,
                spawnPoint.position,
                Quaternion.identity,
                controller.Object.InputAuthority,
                onBeforeSpawned: (runner, netObj) =>
                {
                    // 1) HealZoneBehaviour 초기화
                    var zone = netObj.GetComponent<HealZoneBehaviour>();
                    zone.Initialize(
                        controller.Object.InputAuthority,
                        zoneDuration,
                        healInterval,
                        healAmount
                    );

                    // 2) 부모 제거 (혹시 남아있다면)
                    netObj.transform.SetParent(null);

                    // 3) 일관된 크기 적용: 로컬 스케일 그대로 (예: 8,8,1)
                    netObj.transform.localScale = spawnPoint.localScale;
                }
            );
        }

        // 쿨다운 UI (기존 로직)
        skillCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.skillCooldown);
        StartCoroutine(UpdateSkillCooldownUI());

        yield return null;
        EndSkill();
    }

    private IEnumerator UpdateSkillCooldownUI()
    {
        float total = statData.skillCooldown;
        while (skillCooldownTimer.IsRunning)
        {
            float rem = skillCooldownTimer.RemainingTime(Runner) ?? 0f;
            GamePlayUIController.Instance?.UpdateSkillButtonFill(
                1f - Mathf.Clamp01(rem / total)
            );
            yield return null;
        }
        GamePlayUIController.Instance?.UpdateSkillButtonFill(1f);
    }
}