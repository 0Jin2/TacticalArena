using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

// ─────────────────────────────────────────────────────────────────────────────
// SkillBase : 모든 캐릭터 스킬의 공통 부모 (기존 SkillHandler 대체)
//
//  스킬 만드는 법 (새 캐릭터 스킬 추가 = 이 클래스를 상속한 스크립트 1개)
//    1) Run() 코루틴에 "스킬이 하는 일"을 쓴다.  (스킬을 쓴 사람의 기기에서만 실행됨)
//    2) 다른 기기에도 보여줘야 할 연출은 Broadcast(단계번호, 데이터) 로 알린다.
//    3) OnNetEvent(단계번호, 리더) 에서 연출을 처리한다.  (내 기기를 포함한 "모든 기기"에서 실행됨)
//    4) 사망/중단 시 되돌릴 것은 OnCancel() 에 쓴다.
//
//  쿨타임은 "스킬을 쓰는 순간" 시작된다. 수치는 CharacterStatData.skillCooldown (인스펙터에서 덮어쓸 수도 있음)
// ─────────────────────────────────────────────────────────────────────────────

public abstract class SkillBase : MonoBehaviour
{
    public const byte PhaseStart = 0;       // 스킬 시작 (효과음). 파생 클래스의 단계 번호는 1부터 쓴다.

    [Header("스킬 공통")]
    public Sprite skillIcon;
    public AudioClip skillStartClip;
    public AudioMixerGroup skillMixer;
    [Tooltip("0 이면 CharacterStatData.skillCooldown 을 사용한다.")]
    public float cooldownOverride = 0f;

    public PlayerEntity Entity { get; private set; }
    /// <summary>스킬이 진행 중인가 (주인 기기에서만 의미 있음)</summary>
    public bool IsActive { get; protected set; }
    /// <summary>true 인 동안 기본 공격이 막힌다. (차지샷 대기 중 등)</summary>
    public virtual bool BlocksBasicAttack { get { return false; } }

    public float Cooldown
    {
        get
        {
            if (cooldownOverride > 0f) return cooldownOverride;
            return Entity != null && Entity.stat != null ? Entity.stat.skillCooldown : 5f;
        }
    }

    public bool IsReady { get { return Time.time >= cooldownEndTime; } }
    public float CooldownRemaining { get { return Mathf.Max(0f, cooldownEndTime - Time.time); } }
    /// <summary>UI 버튼 채움 비율. 0 = 방금 씀, 1 = 사용 가능</summary>
    public float ReadyFill { get { return IsReady ? 1f : 1f - Mathf.Clamp01(CooldownRemaining / Mathf.Max(0.01f, Cooldown)); } }

    private float cooldownEndTime;
    private Coroutine runRoutine;

    public void Setup(PlayerEntity owner)
    {
        Entity = owner;
        OnSetup();
    }

    protected virtual void OnSetup() { }

    // ═════════════════════════════════════════════════════════════════════════
    #region 사용

    public bool SkillsAllowed
    {
        get
        {
            var flow = MatchFlow.Instance;
            return flow == null || flow.Config == null || flow.Config.skillsEnabled;
        }
    }

    /// <summary>스킬 버튼을 눌렀을 때 (내 캐릭터)</summary>
    public void TryUse()
    {
        if (Entity == null || !Entity.IsMine || !Entity.ControlEnabled) return;
        if (!SkillsAllowed) return;

        if (IsActive) { OnPressedWhileActive(); return; }
        if (!IsReady) return;
        if (!CanStart()) return;

        IsActive = true;
        cooldownEndTime = Time.time + Cooldown;
        Broadcast(PhaseStart);
        runRoutine = StartCoroutine(RunWrapper());
    }

    private IEnumerator RunWrapper()
    {
        yield return StartCoroutine(Run());
        IsActive = false;
        runRoutine = null;
    }

    /// <summary>추가 사용 조건 (예: 이미 다른 스킬 중이면 false)</summary>
    protected virtual bool CanStart() { return true; }

    /// <summary>스킬 진행 중 버튼을 다시 눌렀을 때 (차지샷 취소 등)</summary>
    protected virtual void OnPressedWhileActive() { }

    /// <summary>스킬 본체. 주인 기기에서만 실행된다.</summary>
    protected abstract IEnumerator Run();

    /// <summary>스킬이 중간에 끊길 때(사망 등) 되돌릴 것. 모든 기기에서 호출되며 여러 번 불려도 안전해야 한다.</summary>
    protected virtual void OnCancel() { }

    /// <summary>사망 시 PlayerHealth 가 호출. 진행 중인 효과를 모두 끝낸다. 쿨타임은 유지.</summary>
    public void CancelOnDeath()
    {
        if (runRoutine != null) { StopCoroutine(runRoutine); runRoutine = null; }
        IsActive = false;
        OnCancel();
    }

    /// <summary>부활 시</summary>
    public virtual void OnRespawn() { }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 네트워크

    /// <summary>
    /// 모든 기기(나 포함)에 연출/상태를 알린다.
    /// 받은 쪽은 OnNetEvent 에서 처리한다. extra 안에서는 난수를 만들지 말 것. (한 번만 실행되어 같은 값이 전달됨)
    /// </summary>
    protected void Broadcast(byte phase, Action<NetWriter> extra = null)
    {
        var w = new NetWriter(16);
        w.WriteByte(phase);
        if (extra != null) extra(w);
        byte[] data = w.ToArray();

        var net = InGameNet.Instance;
        if (net != null) net.Send(NetPacketType.SkillEvent, nw => nw.WriteBlob(data), true);

        ProcessEvent(new NetReader(data));       // 내 기기에는 기다리지 않고 바로 적용
    }

    /// <summary>CombatRouter 가 남의 SkillEvent 를 전달한다.</summary>
    public void HandleNetEvent(NetReader outer)
    {
        ProcessEvent(new NetReader(outer.ReadBlob()));
    }

    private void ProcessEvent(NetReader r)
    {
        byte phase = r.ReadByte();
        if (phase == PhaseStart && Entity != null && Entity.Audio != null)
            Entity.Audio.PlayOneShot(skillStartClip, skillMixer);
        OnNetEvent(phase, r);
    }

    /// <summary>모든 기기에서 실행되는 연출 처리</summary>
    protected virtual void OnNetEvent(byte phase, NetReader r) { }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재접속 복구

    public void RestoreCooldown(float remainingSeconds)
    {
        cooldownEndTime = Time.time + Mathf.Max(0f, remainingSeconds);
    }

    #endregion
}
