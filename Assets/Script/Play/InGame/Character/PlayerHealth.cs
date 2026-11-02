using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerHealth : 체력 / 피격 / 사망 / 부활 무적  (기존 Health 대체)
//
//  체력의 "진짜 값"은 캐릭터 주인 기기가 가진다.
//   - 주인 기기  : Damage 패킷을 받으면 방어/보호막을 적용해 체력을 깎고, 0이 되면 사망을 알린다.
//   - 다른 기기  : 피격 연출만 재생하고, 체력 수치는 주인이 보내는 PlayerState 로 받는다.
//
//  사망 알림은 주인이 PlayerDied 패킷을 보내면 모든 기기(MatchState 포함)가 같은 순서로 처리한다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerHealth : MonoBehaviour, IDamageTarget
{
    [Header("피격 연출")]
    public AudioClip hitClip;
    public AudioMixerGroup hitMixer;
    public Color flashColor = new Color(1f, 0.4f, 0.4f, 1f);
    public float flashSeconds = 0.3f;

    [Header("부활 무적 연출")]
    public float blinkInterval = 0.2f;
    [Range(0f, 1f)] public float blinkAlpha = 0.5f;

    [Header("사망 처리")]
    [Tooltip("사망 후 땅에 닿으면 물리/충돌을 끈다.")]
    public bool disablePhysicsAfterLanding = true;
    public float landingWaitTimeout = 5f;

    [Header("킬 인정")]
    [Tooltip("가해자 없는 사망(낙사)일 때, 이 시간(초) 안에 마지막으로 때린 사람이 있으면 그 사람의 킬로 인정. 0 = 인정 안 함")]
    public float lastAttackerCreditSeconds = 0f;

    [Header("테스트")]
    [Tooltip("켜면 체력이 깎이지 않는다 (에디터 테스트용)")]
    public bool godMode;

    // ── 상태 ────────────────────────────────────────────────────────────────
    public int CurrentHP { get; private set; }
    public int MaxHP { get { return entity != null && entity.stat != null ? entity.stat.maxHP : 100; } }
    public bool IsDead { get; private set; }
    public bool IsInvulnerable { get { return Time.time < invulnerableUntil; } }
    public float InvulnerableRemaining { get { return Mathf.Max(0f, invulnerableUntil - Time.time); } }
    public float DamageReduction { get; private set; }        // 0~1
    public int TempShield { get; private set; }
    public uint LastAttacker { get; private set; }

    public event Action<int> OnHealthChanged;
    public event Action<int, uint> OnDamaged;      // (실제 받은 피해, 가해자)  주인 기기에서만
    public event Action<uint> OnDeath;             // (킬러)  모든 기기
    public event Action OnRevived;

    // ── IDamageTarget ───────────────────────────────────────────────────────
    public uint OwnerSession { get { return entity != null ? entity.SessionId : 0u; } }
    public byte Slot { get { return 0; } }
    public NetTeam Team { get { return entity != null ? entity.Team : NetTeam.None; } }
    public DamageTargetKind Kind { get { return DamageTargetKind.Player; } }
    public bool IsAlive { get { return !IsDead && entity != null && entity.IsPresent; } }
    public bool IsOwnerLocal { get { return entity != null && entity.IsMine; } }
    public Vector3 Position { get { return transform.position; } }

    private PlayerEntity entity;
    private float invulnerableUntil;
    private float lastAttackTime = -999f;
    private Coroutine invulnerableRoutine;
    private Coroutine flashRoutine;
    private Coroutine deathRoutine;
    private SpriteRenderer[] sprites = new SpriteRenderer[0];
    private readonly Dictionary<SpriteRenderer, Color> originalColors = new Dictionary<SpriteRenderer, Color>();
    private readonly Dictionary<Transform, int> originalLayers = new Dictionary<Transform, int>();
    private Rigidbody2D body;

    public void Setup(PlayerEntity owner)
    {
        entity = owner;
        body = GetComponent<Rigidbody2D>();
        CurrentHP = MaxHP;
        IsDead = false;

        sprites = GetComponentsInChildren<SpriteRenderer>(true);
        originalColors.Clear();
        foreach (var s in sprites)
            if (s != null && !originalColors.ContainsKey(s)) originalColors.Add(s, s.color);

        originalLayers.Clear();
        foreach (var t in GetComponentsInChildren<Transform>(true)) originalLayers[t] = t.gameObject.layer;

        DamageRegistry.Register(this);
        RaiseChanged();
    }

    private void OnDestroy()
    {
        DamageRegistry.Unregister(this);
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region 스킬이 쓰는 방어 효과

    /// <summary>받는 피해를 줄인다. percent: 0.5 = 50% 감소. (0으로 해제)</summary>
    public void SetDamageReduction(float percent) { DamageReduction = Mathf.Clamp01(percent); }

    public void AddTempShield(int hp) { TempShield = Mathf.Max(0, hp); }
    public void RemoveTempShield() { TempShield = 0; }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 피해 / 회복

    /// <summary>Damage 패킷이 도착하면 모든 기기에서 호출된다. (IDamageTarget)</summary>
    public void ReceiveDamage(DamageInfo info)
    {
        if (entity == null || !entity.IsInitialized) return;

        if (entity.IsMine) ApplyAuthoritative(info);
        else if (!info.IsHeal && !info.IsSilent && !IsDead && !IsInvulnerable && info.Amount > 0) PlayHitFeedback();
    }

    private void ApplyAuthoritative(DamageInfo info)
    {
        if (IsDead || !entity.IsPresent) return;

        if (info.IsHeal) { Heal(info.Amount); return; }
        if (godMode) return;
        if (IsInvulnerable && !info.IsTrue) return;

        int dmg = Mathf.Max(0, info.Amount);
        if (!info.IsTrue)
        {
            if (TempShield > 0)
            {
                int left = TempShield - dmg;
                if (left >= 0) { TempShield = left; dmg = 0; }
                else { dmg = -left; TempShield = 0; }
            }
            dmg = Mathf.RoundToInt(dmg * (1f - DamageReduction));
        }

        if (info.Attacker != 0 && info.Attacker != OwnerSession)
        {
            LastAttacker = info.Attacker;
            lastAttackTime = Time.time;
        }

        CurrentHP = Mathf.Max(CurrentHP - dmg, 0);
        if (!info.IsSilent) PlayHitFeedback();
        if (OnDamaged != null) OnDamaged(dmg, info.Attacker);
        RaiseChanged();
        if (entity.Sync != null) entity.Sync.SendNow();

        if (CurrentHP == 0) Die(info.Attacker);
    }

    /// <summary>[주인 기기] 체력 회복. 최대 체력을 넘지 않는다.</summary>
    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        int before = CurrentHP;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        if (CurrentHP == before) return;
        RaiseChanged();
    }

    /// <summary>[주인 기기] 즉사 (낙사 지역 등). 무적/방어 무시.</summary>
    public void Kill(uint attacker)
    {
        if (entity == null || !entity.IsMine || IsDead) return;
        CurrentHP = 0;
        RaiseChanged();
        Die(attacker);
    }

    private void RaiseChanged()
    {
        if (OnHealthChanged != null) OnHealthChanged(CurrentHP);
    }

    /// <summary>다른 기기: PlayerState 로 받은 체력 반영</summary>
    public void SetRemoteHp(int hp)
    {
        hp = Mathf.Clamp(hp, 0, MaxHP);
        if (hp == CurrentHP) return;
        CurrentHP = hp;
        RaiseChanged();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 사망

    /// <summary>최근 window 초 안에 나를 때린 사람 (없으면 0). 낙사 지역이 킬 인정에 쓴다.</summary>
    public uint GetRecentAttacker(float window)
    {
        if (window <= 0f || LastAttacker == 0) return 0;
        return Time.time - lastAttackTime <= window ? LastAttacker : 0;
    }

    private uint ResolveKiller(uint attacker)
    {
        if (attacker != 0) return attacker;
        if (lastAttackerCreditSeconds > 0f && LastAttacker != 0 && Time.time - lastAttackTime <= lastAttackerCreditSeconds)
            return LastAttacker;
        return 0;
    }

    private void Die(uint attacker)
    {
        if (IsDead) return;
        uint killer = ResolveKiller(attacker);

        EnterDeadState(killer);

        // 모든 기기(내 기기 포함)가 같은 순서로 킬/데스를 집계하도록 알린다.
        var net = InGameNet.Instance;
        if (net != null)
        {
            uint victim = OwnerSession;
            net.Send(NetPacketType.PlayerDied, w => { w.WriteUInt(victim); w.WriteUInt(killer); }, true);
        }
        if (entity.Sync != null) entity.Sync.SendNow();
    }

    /// <summary>다른 기기: PlayerDied 패킷 수신</summary>
    public void RemoteDie(uint killer)
    {
        if (IsDead) return;
        CurrentHP = 0;
        RaiseChanged();
        EnterDeadState(killer);
    }

    private void EnterDeadState(uint killer)
    {
        IsDead = true;
        invulnerableUntil = 0f;
        StopVisualRoutines();
        RestoreColors();

        if (entity.Combat != null) entity.Combat.ForceStopOnDeath();
        if (entity.Skill != null) entity.Skill.CancelOnDeath();
        if (entity.Motor != null) entity.Motor.ResetState();
        if (entity.Input != null) entity.Input.ClearHeld();
        if (entity.Anim != null) entity.Anim.PlayDie();

        GameLayers.SetRecursively(gameObject, GameLayers.DeadBody);

        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = StartCoroutine(Co_DisableAfterLanding());

        if (OnDeath != null) OnDeath(killer);
        entity.NotifyDeath(killer);
    }

    private IEnumerator Co_DisableAfterLanding()
    {
        float waited = 0f;
        while (IsDead && waited < landingWaitTimeout)
        {
            if (entity.Motor != null && entity.Motor.QueryGrounded()) break;
            waited += Time.deltaTime;
            yield return null;
        }
        if (!IsDead) yield break;

        if (disablePhysicsAfterLanding)
        {
            if (body != null && entity.IsMine) body.simulated = false;
            foreach (var col in GetComponents<Collider2D>()) col.enabled = false;
        }
        deathRoutine = null;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 부활

    /// <summary>
    /// 되살린다. (주인 기기와 다른 기기 모두 호출)
    /// 위치 이동은 PlayerMotor.Teleport 가 따로 한다.
    /// </summary>
    public void Revive(float invincibleSeconds, bool resetHp = true)
    {
        if (deathRoutine != null) { StopCoroutine(deathRoutine); deathRoutine = null; }

        if (body != null)
        {
            body.simulated = true;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        foreach (var col in GetComponents<Collider2D>()) col.enabled = true;
        RestoreLayers();

        IsDead = false;
        TempShield = 0;
        DamageReduction = 0f;
        if (resetHp) CurrentHP = MaxHP;
        RaiseChanged();

        if (entity.Anim != null) entity.Anim.ResetDie();

        StopVisualRoutines();
        RestoreColors();
        StartInvulnerable(invincibleSeconds);

        if (OnRevived != null) OnRevived();
    }

    /// <summary>재접속 스냅샷으로 상태를 복구한다. (사망 상태면 사망 자세로)</summary>
    public void RestoreState(int hp, bool dead, float invincibleRemaining)
    {
        hp = Mathf.Clamp(hp, 0, MaxHP);
        if (dead)
        {
            CurrentHP = 0;
            RaiseChanged();
            if (!IsDead) EnterDeadState(0);
        }
        else
        {
            if (IsDead) Revive(invincibleRemaining, false);
            CurrentHP = Mathf.Max(1, hp);
            RaiseChanged();
            if (invincibleRemaining > 0f) StartInvulnerable(invincibleRemaining);
        }
    }

    public void StartInvulnerable(float seconds)
    {
        if (seconds <= 0f) return;
        invulnerableUntil = Time.time + seconds;
        if (invulnerableRoutine != null) StopCoroutine(invulnerableRoutine);
        invulnerableRoutine = StartCoroutine(Co_Blink());
    }

    private IEnumerator Co_Blink()
    {
        while (Time.time < invulnerableUntil && !IsDead)
        {
            SetAlpha(blinkAlpha);
            yield return new WaitForSeconds(blinkInterval);
            SetAlpha(1f);
            yield return new WaitForSeconds(blinkInterval);
        }
        RestoreColors();
        invulnerableRoutine = null;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 연출

    private void PlayHitFeedback()
    {
        if (entity.Audio != null) entity.Audio.PlayOneShot(hitClip, hitMixer);
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(Co_Flash());
    }

    private IEnumerator Co_Flash()
    {
        foreach (var r in sprites) if (r != null) r.color = flashColor;
        yield return new WaitForSeconds(flashSeconds);
        flashRoutine = null;
        if (invulnerableRoutine != null) yield break;     // 무적 깜빡임 중이면 그쪽이 색을 관리한다
        RestoreColors();
    }

    private void SetAlpha(float alpha)
    {
        foreach (var r in sprites)
        {
            if (r == null) continue;
            Color c;
            if (!originalColors.TryGetValue(r, out c)) c = Color.white;
            c.a *= alpha;
            r.color = c;
        }
    }

    private void RestoreColors()
    {
        foreach (var r in sprites)
        {
            if (r == null) continue;
            Color c;
            if (originalColors.TryGetValue(r, out c)) r.color = c;
        }
    }

    private void StopVisualRoutines()
    {
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
        if (invulnerableRoutine != null) { StopCoroutine(invulnerableRoutine); invulnerableRoutine = null; }
    }

    private void RestoreLayers()
    {
        foreach (var kv in originalLayers)
            if (kv.Key != null) kv.Key.gameObject.layer = kv.Value;
    }

    #endregion
}
