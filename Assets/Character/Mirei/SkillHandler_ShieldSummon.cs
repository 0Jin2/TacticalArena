using System.Collections;
using UnityEngine;
using Fusion;

public class SkillHandler_ShieldSummon : SkillHandler
{
    [Header("방어막 스킬 (NetworkPrefab)")]
    public NetworkPrefabRef shieldPrefab;   // 네트워크 등록된 Barrier 프리팹
    public Transform spawnPoint;            // 캐릭터의 Barrier 생성 위치

    [Header("Barrier 옵션")]
    public float barrierDuration = 3f;      // 지속시간

    protected override IEnumerator DoSkill()
    {
        if (Object.HasStateAuthority)
        {
            // 캐릭터가 좌우 어느 쪽을 보는지 네트워크로 전달하기 위해 bool flip 계산
            bool flip = controller.NetFlipXValue < 0f;

            // spawnPoint.position 사용 (전/후 생성 위치는 기존대로)
            Runner.Spawn(
                shieldPrefab,
                spawnPoint.position,
                spawnPoint.rotation,
                controller.Object.InputAuthority,
                onBeforeSpawned: (runner, netObj) =>
                {
                    var behaviour = netObj.GetComponent<BarrierBehaviour>();
                    behaviour.Initialize(
                        controller.Object.InputAuthority,
                        barrierDuration,
                        flip          // ← flip 정보 전달
                    );
                }
            );
        }

        // 기존 쿨다운 UI 로직
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