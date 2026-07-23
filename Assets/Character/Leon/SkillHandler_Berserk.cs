using UnityEngine;
using System.Collections;
using Fusion;

public class SkillHandler_Berserk : SkillHandler
{
    [Header("버서커 스킬 설정")]
    public GameObject berserkEffectObject;   // 인스펙터에서 할당할 효과 오브젝트 (비활성화 상태로 두세요)

    [Header("버프 옵션")]
    public float duration = 5f;              // 효과 지속 시간
    public float attackSpeedBoost = 0.5f;    // 연사속도 증가율 (예: 0.5 = 50% 증가)

    protected override IEnumerator DoSkill()
    {
        // 1) StateAuthority(호스트)에서만 모든 클라이언트에 효과 활성화 RPC 호출
        if (Object.HasStateAuthority)
            RPC_ToggleBerserkEffect(true);

        // 2) 즉시 공격 속도 버프 적용
        var stat = controller.statData;
        float originalCooldown = stat.attackCooldown;
        stat.attackCooldown = originalCooldown * (1f - attackSpeedBoost);

        // 3) 스킬 쿨다운 시작 및 UI 업데이트
        skillCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.skillCooldown);
        StartCoroutine(UpdateSkillCooldownUI());

        // 4) duration만큼 대기
        yield return new WaitForSeconds(duration);

        // 5) 버프 해제
        stat.attackCooldown = originalCooldown;

        // 6) StateAuthority에서만 모든 클라이언트에 효과 비활성화 RPC 호출
        if (Object.HasStateAuthority)
            RPC_ToggleBerserkEffect(false);

        // 7) 스킬 종료
        EndSkill();
    }

    // ─── 모든 클라이언트에서 호출: 효과 오브젝트 활성/비활성 토글 ───
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ToggleBerserkEffect(bool active)
    {
        if (berserkEffectObject != null)
            berserkEffectObject.SetActive(active);
    }

    // 스킬 버튼 UI FillAmount를 skillCooldown 전체에 걸쳐 업데이트
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
