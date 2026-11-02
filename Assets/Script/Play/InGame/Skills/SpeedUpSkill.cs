using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// SpeedUpSkill : 박진혁 - 스피드업
//   duration 초 동안 이동속도와 애니메이션 속도를 speedMultiplier 배로 올린다.
//   이동속도는 "배율"로만 바꾸므로 CharacterStatData 에셋은 건드리지 않는다.
// ─────────────────────────────────────────────────────────────────────────────

public class SpeedUpSkill : SkillBase
{
    [Header("스피드업")]
    public GameObject speedEffectObject;        // 효과 오브젝트 (평소 꺼 두기)
    public float speedMultiplier = 1.6f;
    public float duration = 7f;

    private const byte PhaseEffect = 1;         // [켜짐? bool]

    protected override void OnSetup()
    {
        if (speedEffectObject != null) speedEffectObject.SetActive(false);
    }

    protected override IEnumerator Run()
    {
        Entity.Motor.SpeedMultiplier = speedMultiplier;
        Broadcast(PhaseEffect, w => w.WriteBool(true));

        yield return new WaitForSeconds(duration);

        Entity.Motor.SpeedMultiplier = 1f;
        Broadcast(PhaseEffect, w => w.WriteBool(false));
    }

    protected override void OnNetEvent(byte phase, NetReader r)
    {
        if (phase != PhaseEffect) return;
        bool on = r.ReadBool();
        if (speedEffectObject != null) speedEffectObject.SetActive(on);
        if (Entity != null && Entity.Anim != null) Entity.Anim.Speed = on ? Entity.Anim.BaseSpeed * speedMultiplier : Entity.Anim.BaseSpeed;
    }

    protected override void OnCancel()
    {
        if (speedEffectObject != null) speedEffectObject.SetActive(false);
        if (Entity == null) return;
        if (Entity.Anim != null) Entity.Anim.Speed = Entity.Anim.BaseSpeed;
        if (Entity.IsMine && Entity.Motor != null) Entity.Motor.SpeedMultiplier = 1f;
    }
}
