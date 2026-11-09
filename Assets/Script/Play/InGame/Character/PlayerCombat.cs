using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerCombat : 기본 공격(발사) / 탄창 / 재장전
//
//  - 내 캐릭터: 공격 버튼을 누르고 있으면 attackCooldown 마다 발사. 탄이 0이면 자동 재장전.
//  - 발사하면 Fire 패킷을 보내고, 나는 즉시 총알을 만든다. (남의 기기는 패킷을 받아 같은 총알을 만든다)
//  - 수치는 CharacterStatData: attackCooldown / magazineSize / reloadTime / pelletCount / spreadAngle
//
//  스킬이 건드리는 값
//   AttackCooldownMultiplier : 버서커 (0.5 = 쿨타임 절반)
//   AttackOverride           : 차지샷 (다음 공격 버튼 입력을 스킬이 가로챈다)
// ─────────────────────────────────────────────────────────────────────────────

public interface IAttackOverride
{
    /// <summary>스킬이 공격 버튼을 가로채고 있을 때, 버튼을 누르면 호출된다.</summary>
    void OnAttackPressed();
}

public class PlayerCombat : MonoBehaviour
{
    [Header("발사")]
    public NetProjectile projectilePrefab;
    public Transform projectileSpawnPoint;
    [Tooltip("버튼을 누른 뒤 실제로 총알이 나가기까지의 지연(초)")]
    public float fireDelay = 0.01f;

    [Header("총구 이펙트")]
    public GameObject muzzleFlashPrefab;
    public float muzzleFlashLifetime = 1f;
    [Tooltip("오른쪽을 볼 때의 이펙트 회전각. 왼쪽을 볼 때는 부호가 반대로 적용된다.")]
    public float muzzleAngleRight = -90f;
    [Tooltip("총구 이펙트 프리팹이 맞춰져 있는 캐릭터 배율. 캐릭터 배율이 이 값과 다르면 같은 비율로 키우거나 줄인다. (FALL용 _FD = 3, 일반 = 0.5)")]
    public float muzzleReferenceScale = 3f;

    [Header("발사 효과음")]
    public AudioClip shootClip;
    public AudioMixerGroup shootMixer;

    [Header("재장전 효과음")]
    public AudioClip reloadClip;            // 탄을 1발 넣는 소리 (샷건 셸 등)
    public AudioClip rackClip;              // 마지막 슬라이드/펌프 소리
    public AudioMixerGroup reloadMixer;
    public AudioMixerGroup rackMixer;
    [Tooltip("0 = 부족한 탄 수만큼 반복. 1 이상이면 그 횟수만큼 반복")]
    public int reloadRepeatOverride = 0;
    public int reloadRepeatMax = 30;

    // ── 상태 ────────────────────────────────────────────────────────────────
    public int Ammo { get; private set; }
    public int MagazineSize { get { return entity != null && entity.stat != null ? entity.stat.magazineSize : 0; } }
    public bool IsReloading { get; private set; }
    public bool IsShooting { get; private set; }
    public float AttackCooldownMultiplier { get; set; }
    public IAttackOverride AttackOverride { get; set; }

    public float AttackCooldown
    {
        get { return (entity != null && entity.stat != null ? entity.stat.attackCooldown : 0.5f) * AttackCooldownMultiplier; }
    }

    // UI 용 이벤트 (내 캐릭터에서만 발생)
    public event Action<int, int> AmmoChanged;       // (현재 탄, 탄창 크기)
    public event Action<float> ReloadProgress;       // 0~1
    public event Action<bool> ReloadStateChanged;    // 재장전 중?

    private PlayerEntity entity;
    private float nextAttackTime;
    private Coroutine reloadRoutine;
    private Coroutine fireRoutine;

    private void Awake()
    {
        AttackCooldownMultiplier = 1f;
    }

    public void Setup(PlayerEntity owner)
    {
        entity = owner;
        Ammo = MagazineSize;
        IsReloading = false;
        AttackCooldownMultiplier = 1f;
        RaiseAmmo();
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region 내 캐릭터 조작

    private void Update()
    {
        if (entity == null || !entity.IsInitialized || !entity.IsMine) return;

        bool control = entity.ControlEnabled;
        var input = entity.Input;
        bool holding = control && input != null && input.AttackHeld && AttackOverride == null;

        IsShooting = holding && !BlockedBySkill;
        if (entity.Anim != null) entity.Anim.SetShoot(IsShooting);

        if (!control || input == null) return;

        // 스킬이 공격 버튼을 가로챈 상태 (차지샷)
        if (AttackOverride != null)
        {
            if (input.AttackDown) AttackOverride.OnAttackPressed();
            return;
        }

        if (input.AttackHeld) TryAttack();
    }

    /// <summary>남의 캐릭터: 받은 사격 상태를 애니메이션에 반영</summary>
    public void SetRemoteShooting(bool shooting)
    {
        IsShooting = shooting;
        if (entity != null && entity.Anim != null && (entity.Health == null || !entity.Health.IsDead))
            entity.Anim.SetShoot(shooting);
    }

    private bool BlockedBySkill { get { return entity.Skill != null && entity.Skill.BlocksBasicAttack; } }

    public bool TryAttack()
    {
        if (entity == null || !entity.IsMine) return false;
        if (entity.Health != null && entity.Health.IsDead) return false;
        if (IsReloading || BlockedBySkill) return false;

        if (Ammo <= 0) { TryReload(); return false; }
        if (Time.time < nextAttackTime) return false;

        nextAttackTime = Time.time + AttackCooldown;
        Ammo = Mathf.Max(0, Ammo - 1);
        RaiseAmmo();

        if (fireRoutine != null) StopCoroutine(fireRoutine);
        fireRoutine = StartCoroutine(Co_Fire());

        if (Ammo == 0) TryReload();
        return true;
    }

    private IEnumerator Co_Fire()
    {
        yield return new WaitForSeconds(fireDelay);
        fireRoutine = null;
        if (entity.Health != null && entity.Health.IsDead) yield break;
        FireNow();
    }

    private void FireNow()
    {
        if (projectilePrefab == null) { NetLog.Warn("PlayerCombat: projectilePrefab 이 비어 있습니다. " + gameObject.name); return; }

        float facing = entity.Motor != null ? entity.Motor.Facing : 1f;
        Vector2 origin = projectileSpawnPoint != null ? (Vector2)projectileSpawnPoint.position : (Vector2)transform.position;

        int pellets = Mathf.Max(1, entity.stat != null ? entity.stat.pelletCount : 1);
        float spread = entity.stat != null ? entity.stat.spreadAngle : 0f;
        var angles = new float[pellets];
        for (int i = 0; i < pellets; i++)
            angles[i] = spread > 0f ? UnityEngine.Random.Range(-spread * 0.5f, spread * 0.5f) : 0f;

        // 나는 기다리지 않고 바로 만든다
        SpawnShot(origin, facing, angles);

        var net = InGameNet.Instance;
        if (net != null)
        {
            net.Send(NetPacketType.Fire, w =>
            {
                w.WriteVector2(origin);
                w.WriteByte(facing < 0f ? (byte)0 : (byte)1);
                w.WriteByte((byte)angles.Length);
                for (int i = 0; i < angles.Length; i++) w.WriteFloat(angles[i]);
            });
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 총알 생성 (내 기기 / 남의 기기 공통)

    /// <summary>남의 Fire 패킷을 받아 같은 총알을 만든다.</summary>
    public void OnRemoteFire(NetReader r)
    {
        Vector2 origin = r.ReadVector2();
        float facing = r.ReadByte() == 0 ? -1f : 1f;
        int count = r.ReadByte();
        var angles = new float[count];
        for (int i = 0; i < count; i++) angles[i] = r.ReadFloat();
        SpawnShot(origin, facing, angles);
    }

    private void SpawnShot(Vector2 origin, float facing, float[] angles)
    {
        PlayShotEffects(origin, facing);
        if (projectilePrefab == null) return;

        for (int i = 0; i < angles.Length; i++)
        {
            Vector2 dir = Quaternion.Euler(0f, 0f, angles[i]) * Vector3.right * facing;
            var bullet = Instantiate(projectilePrefab, origin, Quaternion.FromToRotation(Vector3.right, (Vector3)dir));
            bullet.Launch(entity.SessionId, dir);
        }
    }

    /// <summary>총구 이펙트 + 발사음 (스킬 발사에서도 재사용)</summary>
    public void PlayShotEffects(Vector2 position, float facing)
    {
        if (muzzleFlashPrefab != null)
        {
            float angle = facing > 0f ? muzzleAngleRight : -muzzleAngleRight;
            var flash = Instantiate(muzzleFlashPrefab, position, Quaternion.Euler(0f, 0f, angle));
            // 이펙트는 캐릭터 밖(월드)에 생기므로 캐릭터 배율을 직접 반영한다.
            float ratio = muzzleReferenceScale > 0f ? Mathf.Abs(transform.lossyScale.y) / muzzleReferenceScale : 1f;
            flash.transform.localScale *= ratio;
            // 2D 조명 반경은 배율을 따라가지 않으므로 같이 맞춘다.
            foreach (var light in flash.GetComponentsInChildren<UnityEngine.Rendering.Universal.Light2D>(true))
            {
                light.pointLightOuterRadius *= ratio;
                light.pointLightInnerRadius *= ratio;
            }
            Destroy(flash, muzzleFlashLifetime);
        }
        if (entity != null && entity.Audio != null) entity.Audio.PlayOneShot(shootClip, shootMixer);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재장전

    public void TryReload()
    {
        if (entity == null || !entity.IsMine) return;
        if (entity.Health != null && entity.Health.IsDead) return;
        if (IsReloading) return;
        if (Ammo >= MagazineSize) return;

        float reloadTime = entity.stat != null ? entity.stat.reloadTime : 1f;
        int repeats = CalcReloadRepeats();
        float segment = reloadTime / (repeats + 1f);

        IsReloading = true;
        if (ReloadStateChanged != null) ReloadStateChanged(true);

        PlayReloadPattern(repeats, segment);
        var net = InGameNet.Instance;
        if (net != null)
        {
            net.Send(NetPacketType.CombatEvent, w =>
            {
                w.WriteByte(CombatEventKind.Reload);
                w.WriteByte((byte)Mathf.Clamp(repeats, 0, 255));
                w.WriteFloat(segment);
            });
        }

        if (reloadRoutine != null) StopCoroutine(reloadRoutine);
        reloadRoutine = StartCoroutine(Co_Reload(reloadTime));
    }

    private int CalcReloadRepeats()
    {
        if (reloadRepeatOverride > 0) return reloadRepeatOverride;
        int need = MagazineSize - Ammo;
        return Mathf.Clamp(need <= 0 ? 1 : need, 1, Mathf.Max(1, reloadRepeatMax));
    }

    private IEnumerator Co_Reload(float reloadTime)
    {
        float elapsed = 0f;
        while (elapsed < reloadTime)
        {
            elapsed += Time.deltaTime;
            if (ReloadProgress != null) ReloadProgress(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, reloadTime)));
            yield return null;
        }

        Ammo = MagazineSize;
        IsReloading = false;
        reloadRoutine = null;
        if (ReloadProgress != null) ReloadProgress(1f);
        if (ReloadStateChanged != null) ReloadStateChanged(false);
        RaiseAmmo();
    }

    /// <summary>남의 CombatEvent(Reload) 수신 → 소리만 재생</summary>
    public void OnRemoteReload(int repeats, float segment)
    {
        PlayReloadPattern(repeats, segment);
    }

    private void PlayReloadPattern(int repeats, float segment)
    {
        StartCoroutine(Co_ReloadSounds(repeats, segment));
    }

    private IEnumerator Co_ReloadSounds(int repeats, float segment)
    {
        var audio = entity != null ? entity.Audio : null;
        if (audio == null) yield break;

        for (int i = 0; i < repeats; i++)
        {
            if (reloadClip != null)
                audio.PlayOneShot(reloadClip, reloadMixer, reloadClip.length / Mathf.Max(0.01f, segment));
            yield return new WaitForSeconds(segment);
        }
        if (rackClip != null)
            audio.PlayOneShot(rackClip, rackMixer, rackClip.length / Mathf.Max(0.01f, segment));
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 상태 초기화

    private void RaiseAmmo()
    {
        if (entity != null && entity.IsMine && AmmoChanged != null) AmmoChanged(Ammo, MagazineSize);
    }

    /// <summary>사망 시: 발사/재장전/가로채기 모두 중단</summary>
    public void ForceStopOnDeath()
    {
        if (fireRoutine != null) { StopCoroutine(fireRoutine); fireRoutine = null; }
        if (reloadRoutine != null) { StopCoroutine(reloadRoutine); reloadRoutine = null; }
        IsReloading = false;
        IsShooting = false;
        AttackOverride = null;
        AttackCooldownMultiplier = 1f;
        if (entity != null && entity.IsMine)
        {
            if (ReloadStateChanged != null) ReloadStateChanged(false);
            if (ReloadProgress != null) ReloadProgress(1f);
        }
        if (entity != null && entity.Anim != null) entity.Anim.SetShoot(false);
    }

    /// <summary>부활 시: 탄창을 가득 채운다</summary>
    public void RefillOnRespawn()
    {
        Ammo = MagazineSize;
        IsReloading = false;
        nextAttackTime = 0f;
        RaiseAmmo();
    }

    /// <summary>재접속 복구용</summary>
    public void RestoreAmmo(int ammo)
    {
        Ammo = Mathf.Clamp(ammo, 0, MagazineSize);
        RaiseAmmo();
    }

    #endregion
}
