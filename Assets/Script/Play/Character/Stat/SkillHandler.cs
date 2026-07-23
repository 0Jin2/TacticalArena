using UnityEngine;
using Fusion;
using System.Collections;
using UnityEngine.Audio;

public class SkillHandler : NetworkBehaviour
{
    [Header("Skill Icon Sprite")]
    public Sprite skillIcon;

    public CharacterStatData statData;
    public PlayerAnimationHandler anim;
    public PlayerNetworkController controller;

    public AudioClip skillStartClip;
    public AudioMixerGroup skillMixerGroup;

    [Networked] protected TickTimer skillCooldownTimer { get; set; }
    [Networked] public bool isSkillActive { get; set; }

    public override void Spawned()
    {
        // Inspector에서 연결해야 함!
    }

    [Fusion.Rpc(Fusion.RpcSources.StateAuthority, Fusion.RpcTargets.All)]
    protected void RPC_PlaySkillSfx()
    {
        // PlayerNetworkController의 공용 재생 유틸 사용
        if (controller != null)
            controller.PlayOneShotSfx(skillStartClip, skillMixerGroup);
    }

    public virtual void TryUseSkill()
    {
        if (!Object.HasInputAuthority) return;
        if (!skillCooldownTimer.ExpiredOrNotRunning(Runner)) return;

        RPC_StartSkill();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    protected virtual void RPC_StartSkill()
    {
        if (isSkillActive) return;
        isSkillActive = true;
        skillCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.skillCooldown);
        RPC_PlaySkillSfx();
        StartCoroutine(DoSkill());
    }

    protected virtual IEnumerator DoSkill()
    {
        yield return new WaitForSeconds(statData.skillCooldown);
        EndSkill();
    }

    protected virtual void EndSkill()
    {
        isSkillActive = false;
        controller.SetMovementLock(false);
    }
}
