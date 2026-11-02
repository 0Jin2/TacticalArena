using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// BerserkSkill : 레온 - 광폭화
//   duration 초 동안 공격 간격을 attackSpeedBoost 만큼 줄인다. (0.5 = 간격 절반 = 2배 빠름)
//   기존 코드는 공유 에셋(CharacterStatData)을 직접 고쳤지만, 여기서는 "배율"만 바꾸므로 에셋이 오염되지 않는다.
// ─────────────────────────────────────────────────────────────────────────────

public class BerserkSkill : SkillBase
{
    [Header("광폭화")]
    public GameObject berserkEffectObject;      // 효과 오브젝트 (평소 꺼 두기)
    public float duration = 5f;
    [Range(0f, 0.95f)] public float attackSpeedBoost = 0.5f;

    private const byte PhaseEffect = 1;         // [켜짐? bool]

    protected override void OnSetup()
    {
        if (berserkEffectObject != null) berserkEffectObject.SetActive(false);
    }

    protected override IEnumerator Run()
    {
        Broadcast(PhaseEffect, w => w.WriteBool(true));
        Entity.Combat.AttackCooldownMultiplier = 1f - attackSpeedBoost;

        yield return new WaitForSeconds(duration);

        Entity.Combat.AttackCooldownMultiplier = 1f;
        Broadcast(PhaseEffect, w => w.WriteBool(false));
    }

    protected override void OnNetEvent(byte phase, NetReader r)
    {
        if (phase == PhaseEffect && berserkEffectObject != null) berserkEffectObject.SetActive(r.ReadBool());
    }

    protected override void OnCancel()
    {
        if (berserkEffectObject != null) berserkEffectObject.SetActive(false);
        if (Entity != null && Entity.IsMine && Entity.Combat != null) Entity.Combat.AttackCooldownMultiplier = 1f;
    }
}
