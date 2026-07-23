using UnityEngine;
using System.Collections;
using Fusion;

public class SkillHandler_SpeedUp : SkillHandler
{
    [Header("스피드업 효과 오브젝트")]
    public GameObject speedEffectObject;   // Inspector에 할당, 초기에는 비활성화 상태로 두세요

    [Header("버프 옵션")]
    public float speedMultiplier = 1.6f;   // 이동속도 및 애니메이션 속도 배율
    public float duration = 7f;            // 버프 지속 시간(초)

    private float originalMoveSpeed;
    private float originalAnimSpeed;

    protected override IEnumerator DoSkill()
    {
        var stat = controller.statData;

        // 1) 원래 이동속도와 애니메이션 속도 저장
        originalMoveSpeed = stat.moveSpeed;
        var netAnimComp = controller.GetComponent<NetworkMecanimAnimator>();
        if (netAnimComp != null && netAnimComp.Animator != null)
            originalAnimSpeed = netAnimComp.Animator.speed;
        else
            originalAnimSpeed = 1f;

        // 2) StateAuthority(호스트)에서만 RPC 호출 → 모든 클라이언트에 이펙트 활성화 & 애니메이션 속도 적용
        if (Object.HasStateAuthority)
        {
            RPC_ToggleSpeedEffect(true);
            RPC_ApplyAnimSpeed(speedMultiplier);
        }

        // 3) 이동속도 버프 적용
        stat.moveSpeed = originalMoveSpeed * speedMultiplier;

        // 4) 스킬 쿨다운 시작 및 UI 업데이트
        skillCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.skillCooldown);
        StartCoroutine(UpdateSkillCooldownUI());

        // 5) duration만큼 대기
        yield return new WaitForSeconds(duration);

        // 6) 이동속도 및 애니메이션 속도 원복, 이펙트 비활성화 RPC 호출
        stat.moveSpeed = originalMoveSpeed;
        if (Object.HasStateAuthority)
        {
            RPC_ApplyAnimSpeed(originalAnimSpeed);
            RPC_ToggleSpeedEffect(false);
        }

        // 7) 스킬 종료
        EndSkill();
    }

    // 모든 클라이언트에서 효과 오브젝트 활성/비활성화
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ToggleSpeedEffect(bool active)
    {
        if (speedEffectObject != null)
            speedEffectObject.SetActive(active);
    }

    // 모든 클라이언트에서 애니메이션 속도 적용
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ApplyAnimSpeed(float newSpeed)
    {
        var netAnimComp = controller.GetComponent<NetworkMecanimAnimator>();
        if (netAnimComp != null && netAnimComp.Animator != null)
            netAnimComp.Animator.speed = newSpeed;
    }

    // 스킬 버튼 UI의 FillAmount를 쿨다운 전체에 걸쳐 업데이트
    private IEnumerator UpdateSkillCooldownUI()
    {
        float total = statData.skillCooldown;
        while (skillCooldownTimer.IsRunning)
        {
            float rem = skillCooldownTimer.RemainingTime(Runner) ?? 0f;
            float fill = 1f - Mathf.Clamp01(rem / total);
            GamePlayUIController.Instance?.UpdateSkillButtonFill(fill);
            yield return null;
        }
        GamePlayUIController.Instance?.UpdateSkillButtonFill(1f);
    }
}
