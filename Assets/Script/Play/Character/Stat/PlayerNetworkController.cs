using Fusion;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Linq;
using System;
using UnityEngine.Audio;
using TMPro;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("총구 이펙트 & 사운드")]
    public GameObject muzzleFlashPrefab;      // 총구 VFX 프리팹
    public AudioClip shootAudioClip;          // 총알 발사 사운드 클립 (캐릭터별로 다른 MP3)
    public AudioMixerGroup shootMixerGroup;   // AudioMixerGroup (Inspector 에 할당)

    [Header("점프 사운드")]
    public AudioClip jumpAudioClip;
    public AudioMixerGroup jumpMixerGroup;

    [Header("Reload SFX")]
    public AudioClip reloadClip;                // 'Reload' 1회에 해당하는 짧은 삽입 사운드(샷건 셸 삽입 등)
    public AudioClip rackClip;                  // 'Rack' 1회(슬라이드/펌프 동작)
    public AudioMixerGroup reloadMixerGroup;    // 선택(없어도 됨)
    public AudioMixerGroup rackMixerGroup;      // 선택(없어도 됨)

    [Tooltip("0이면 자동 계산(= 부족한 탄 수). 1 이상이면 그 횟수만큼 Reload 사운드를 반복 재생")]
    public int reloadRepeatOverride = 0;

    public CharacterStatData statData;
    public PlayerLocalInput localInput;
    public PlayerAnimationHandler anim;
    public GameObject projectilePrefab;
    public Transform projectileSpawnPoint;

    private Rigidbody2D rb;
    private float initialScaleX;
    private bool isGrounded = false;
    private bool wasGrounded = false;
    private bool canDoubleJump = false;
    private SkillHandler mySkillHandler;

    [Header("NameTag")]
    public NameTagUI nameTagUI;  // Prefab > NameTagCanvas 오브젝트를 드래그

    [Networked(OnChanged = nameof(OnFlipChanged))] private float NetFlipX { get; set; }
    [Networked] private TickTimer attackCooldownTimer { get; set; }
    [Networked] private TickTimer reloadTimer { get; set; }
    [Networked] public int currentAmmo { get; set; }    // 현재 탄창 탄환 수
    [Networked] public bool isReloading { get; set; }   // 재장전 중 여부
    [Networked] public bool isUsingSkill { get; set; }
    [Networked] public bool isKnockBack { get; set; }

    public float NetFlipXValue => NetFlipX;
    private AudioSource shootAudioSource;     // 실행 시 생성될 AudioSource
    private float dropThroughTime = 0.4f;
    private Health health;

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        health = GetComponent<Health>();
        rb.isKinematic = false;
        rb.simulated = true;
        shootAudioSource = GetComponent<AudioSource>();
        initialScaleX = transform.localScale.x;

        if (shootAudioSource == null)
            shootAudioSource = gameObject.AddComponent<AudioSource>();

        if (Object.HasInputAuthority && !Object.HasStateAuthority)
        {
            Object.RequestStateAuthority();
            Debug.Log("⭐ StateAuthority 요청! (비마스터 유저)");
        }
        shootAudioSource.playOnAwake = false;
        shootAudioSource.outputAudioMixerGroup = shootMixerGroup;

        float spawnFlip = Mathf.Sign(transform.localScale.x);
        if (Mathf.Abs(spawnFlip) < 0.01f) spawnFlip = 1f; // 0 방지
        NetFlipX = spawnFlip; // ★ NetFlipX를 localScale.x에 맞게 세팅

        if (Object.HasInputAuthority)
        {
            if (!Runner.TryGetPlayerObject(Runner.LocalPlayer, out var existing))
            {
                Runner.SetPlayerObject(Runner.LocalPlayer, Object);
            }

            var skillHandlers = GetComponents<SkillHandler>();
            mySkillHandler = skillHandlers.FirstOrDefault(h => h.GetType() != typeof(SkillHandler))
                          ?? skillHandlers.FirstOrDefault();

            if (mySkillHandler != null)
                GamePlayUIController.Instance.InitSkillButton(mySkillHandler);
        }

        if (!Runner.ProvideInput)
            Runner.ProvideInput = true;

        if (anim == null)
            anim = GetComponent<PlayerAnimationHandler>();

        if (Object.HasInputAuthority && localInput == null)
            localInput = GetComponent<PlayerLocalInput>();

        if (Object.HasStateAuthority)
        {
            currentAmmo = statData.magazineSize;
            isReloading = false;
        }

        // 1. 본인 캐릭터만 UI 이벤트 바인딩
        if (Object.HasInputAuthority)
        {
            GamePlayUIController.Instance?.InitReloadButton(this);
            UpdateReloadButtonUI();
        }

        if (nameTagUI != null && nameTagUI.text == null)
            nameTagUI.text = nameTagUI.GetComponentInChildren<TMP_Text>();
    }

    public override void FixedUpdateNetwork()
    {
        if (health != null && health.IsDead)
        {
            if (anim != null) anim.SetShootState(false);
            return;
        }

        if (Object.HasStateAuthority)
        {
            if (GetInput(out PlayerInputData input))
            {
                HandleMove(input.moveX);
                if (anim != null) anim.SetShootState(input.attack);
                if (IsDownJump(input))
                {
                    if (TryDropDown())
                    {
                        return;
                    }
                }
                if (input.jump) HandleJump();
                if (input.attack) HandleAttack();
                if (input.skill) HandleSkill();
            }
        }

        bool grounded = IsGrounded();
        if (grounded && !wasGrounded)
            canDoubleJump = statData != null ? statData.allowDoubleJump : false;
        wasGrounded = grounded;
    }

    private void HandleMove(float moveX)
    {
        if (statData == null) return;
        if (!Object.HasStateAuthority) return;
        if (isKnockBack) return;

        float fixedSpeed = statData.moveSpeed;
        float inputDir = Mathf.Sign(moveX);
        if (Mathf.Abs(moveX) < 0.2f) inputDir = 0f;

        rb.linearVelocity = new Vector2(inputDir * fixedSpeed, rb.linearVelocity.y);

        if (inputDir != 0)
            NetFlipX = inputDir;

        if (anim != null && Object.HasStateAuthority)
            anim.SetMoveState(inputDir != 0 ? fixedSpeed : 0f, IsGrounded());
    }

    private void HandleJump()
    {
        if (statData == null) return;
        if (!Object.HasStateAuthority) return;
        if (isKnockBack) return;

        if (IsGrounded())
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, statData.jumpPower);
            canDoubleJump = statData.allowDoubleJump;
            RPC_PlayJumpSfx();
        }
        else if (canDoubleJump)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, statData.jumpPower);
            canDoubleJump = false;
            RPC_PlayJumpSfx();
        }
    }

    private bool IsDownJump(PlayerInputData input)
    {
        // 조이스틱(키보드) 아래로 충분히 입력 + 점프 버튼 동시
        return localInput != null &&
               localInput.moveInput.y < -0.5f &&
               localInput.jumpPressed;
    }

    private bool TryDropDown()
    {
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol == null) return false;

        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, 0.2f, LayerMask.GetMask("Ground"));
        if (hit.collider != null)
        {
            // 레이어와 태그가 모두 Ground일 때만 드롭다운 가능
            bool isGroundLayer = hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground");
            bool isGroundTag = hit.collider.CompareTag("Ground");

            if (isGroundLayer && isGroundTag)
            {
                StartCoroutine(TemporaryIgnoreGround(myCol, hit.collider));
                return true;
            }
        }
        return false;
    }

    private IEnumerator TemporaryIgnoreGround(Collider2D myCol, Collider2D groundCol)
    {
        Physics2D.IgnoreCollision(myCol, groundCol, true);
        yield return new WaitForSeconds(dropThroughTime);
        Physics2D.IgnoreCollision(myCol, groundCol, false);
    }

    private void HandleAttack()
    {
        if (!Object.HasStateAuthority) return;
        if (health != null && health.IsDead) return;
        if (isReloading) return;
        if (isUsingSkill) return; // 스킬 중에는 기본 총알 발사 차단

        if (currentAmmo <= 0)
        {
            TryReload();
            return;
        }
        if (!attackCooldownTimer.ExpiredOrNotRunning(Runner)) return;

        attackCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.attackCooldown);

        // 탄약 차감
        currentAmmo = Mathf.Max(0, currentAmmo - 1);
        UpdateReloadButtonUI();

        if (anim != null)
            anim.SetShootState(true);

        StartCoroutine(DelayedShootCoroutine());

        if (currentAmmo == 0)
            TryReload();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayShootEffects(Vector3 pos, Quaternion rot)
    {
        // 1) 총구 이펙트 생성
        if (muzzleFlashPrefab != null)
        {
            var flash = Instantiate(
                muzzleFlashPrefab,
                pos,
                rot
            );
            Destroy(flash, 1f);
        }

        // 2) 발사 사운드 재생
        if (shootAudioSource != null && shootAudioClip != null)
        {
            shootAudioSource.PlayOneShot(shootAudioClip);
        }
    }

    private IEnumerator DelayedShootCoroutine()
    {
        yield return new WaitForSeconds(0.01f);

        float baseDir = Mathf.Sign(NetFlipX);

        // 수정: 이펙트 프리팹이 위쪽(+Y) 방향이 기본이므로
        // 오른쪽 발사 시 –90°, 왼쪽 발사 시 +90° 회전
        float muzzleAngle = baseDir > 0f ? -90f : 90f;
        Quaternion rot = Quaternion.Euler(0f, 0f, muzzleAngle);

        RPC_PlayShootEffects(projectileSpawnPoint.position, rot);

        float spread = statData.spreadAngle;
        int pelletCount = Mathf.Max(1, statData.pelletCount);

        for (int i = 0; i < pelletCount; i++)
        {
            float angle = 0f;
            if (spread > 0f)
                angle = UnityEngine.Random.Range(-spread * 0.5f, spread * 0.5f);

            Vector2 fireDir = Quaternion.Euler(0, 0, angle) * Vector2.right * baseDir;

            Runner.Spawn(
                projectilePrefab,
                projectileSpawnPoint.position,
                Quaternion.FromToRotation(Vector2.right, fireDir),
                inputAuthority: null, // null이면 StateAuthority가 생성한 주체(보통 마스터)가 됨
                onBeforeSpawned: (runner, obj) =>
                {
                    var projectile = obj.GetComponent<Projectile>();
                    if (projectile != null)
                    {
                        projectile.direction = baseDir;
                        projectile.owner = Object.InputAuthority;
                    }
                    else
                    {
                        // 2️⃣ 없으면 KnockbackProjectile 시도
                        var knockback = obj.GetComponent<KnockbackProjectile>();
                        if (knockback != null)
                        {
                            knockback.direction = baseDir;
                            knockback.owner = Object.InputAuthority;
                        }
                    }
                }
            );
        }
    }

    // 재장전 시도 (버튼 클릭 or 자동)
    public void TryReload()
    {
        if (!Object.HasStateAuthority) return;
        if (isReloading) return;
        if (currentAmmo == statData.magazineSize) return; // 이미 만탄이면 불가

        isReloading = true;
        reloadTimer = TickTimer.CreateFromSeconds(Runner, statData.reloadTime);
        UpdateReloadButtonUI();

        // 🔊 리로드 SFX 패턴 계산 & 모든 클라에 재생 지시
        int repeats = CalcReloadRepeatCount();                  // Reload 반복 횟수
        float segment = statData.reloadTime / (repeats + 1f);   // Reload×repeats + Rack×1
        RPC_PlayReloadPattern(repeats, segment);

        StartCoroutine(ReloadCoroutine());
    }

    private int CalcReloadRepeatCount()
    {
        if (reloadRepeatOverride > 0)
            return reloadRepeatOverride;

        // 자동: 부족한 탄 수만큼
        int need = (statData != null) ? (statData.magazineSize - currentAmmo) : 1;
        return Mathf.Clamp(need <= 0 ? 1 : need, 1, 30);
    }

    [Fusion.Rpc(Fusion.RpcSources.StateAuthority, Fusion.RpcTargets.All)]
    private void RPC_PlayReloadPattern(int repeats, float segmentDuration)
    {
        if (shootAudioSource == null) return;
        StartCoroutine(Co_PlayReloadPattern(repeats, segmentDuration));
    }

    private IEnumerator Co_PlayReloadPattern(int repeats, float segmentDuration)
    {
        if (shootAudioSource == null) yield break;

        var prevGroup = shootAudioSource.outputAudioMixerGroup;
        float originalPitch = shootAudioSource.pitch;

        // Reload × repeats
        for (int i = 0; i < repeats; i++)
        {
            if (reloadClip != null)
            {
                if (reloadMixerGroup != null) shootAudioSource.outputAudioMixerGroup = reloadMixerGroup;
                // clip.length / segmentDuration 만큼 pitch를 올려서 딱 segmentDuration 안에 끝나게 함
                shootAudioSource.pitch = Mathf.Max(0.01f, reloadClip.length / Mathf.Max(0.01f, segmentDuration));
                shootAudioSource.PlayOneShot(reloadClip);
            }
            yield return new WaitForSeconds(segmentDuration);
        }

        // Rack × 1
        if (rackClip != null)
        {
            if (rackMixerGroup != null) shootAudioSource.outputAudioMixerGroup = rackMixerGroup;
            shootAudioSource.pitch = Mathf.Max(0.01f, rackClip.length / Mathf.Max(0.01f, segmentDuration));
            shootAudioSource.PlayOneShot(rackClip);
        }

        // 복원
        shootAudioSource.pitch = originalPitch;
        shootAudioSource.outputAudioMixerGroup = prevGroup;
    }

    private IEnumerator ReloadCoroutine()
    {
        float elapsed = 0f;
        float reloadTime = statData.reloadTime;

        while (elapsed < reloadTime)
        {
            elapsed += Time.deltaTime;
            float percent = Mathf.Clamp01(elapsed / reloadTime);

            // 본인만 Fill UI 갱신 (0~1)
            if (Object.HasInputAuthority)
                GamePlayUIController.Instance?.UpdateReloadButtonFill(percent);

            yield return null;
        }

        // 재장전 완료
        if (Object.HasStateAuthority)
        {
            currentAmmo = statData.magazineSize;
            isReloading = false;
        }

        if (Object.HasInputAuthority)
        {
            GamePlayUIController.Instance?.UpdateReloadButtonFill(1f);
            UpdateReloadButtonUI();
        }
    }

    // UI 텍스트/버튼 동기화
    public void UpdateReloadButtonUI()
    {
        if (Object.HasInputAuthority) // 본인만 UI 갱신
            GamePlayUIController.Instance?.UpdateReloadText(currentAmmo, statData.magazineSize);

        // 재장전중 버튼 비활성, 만탄때 버튼 비활성, 나머지 활성
        if (Object.HasInputAuthority)
            GamePlayUIController.Instance?.SetReloadButtonInteractable(!isReloading && currentAmmo < statData.magazineSize);
    }

    private void HandleSkill()
    {
        if (!Object.HasStateAuthority) return;
        if (mySkillHandler != null)
            mySkillHandler.TryUseSkill();
    }

    private void EndSkill()
    {
        isUsingSkill = false;
    }

    private bool IsGrounded()
    {
        Vector2 position = transform.position;
        Vector2 size = new Vector2(0.5f, 0.1f);
        RaycastHit2D hit = Physics2D.BoxCast(position, size, 0f, Vector2.down, 0.1f, LayerMask.GetMask("Ground"));
        Debug.DrawRay(position, Vector2.down * 0.1f, hit.collider ? Color.green : Color.red);
        return hit.collider != null;
    }

    public void SetMovementLock(bool locked)
    {
        if (Object.HasStateAuthority)
            RPC_SetMovementLock(locked);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SetMovementLock(bool locked)
    {
        isUsingSkill = locked;
        if (locked) rb.linearVelocity = Vector2.zero;
    }

    private static void OnFlipChanged(Changed<PlayerNetworkController> changed)
    {
        changed.Behaviour.ApplyFlip();
    }

    private void ApplyFlip()
    {
        float flipDir = Mathf.Sign(NetFlipX);
        if (Mathf.Abs(NetFlipX) < 0.01f) flipDir = 1f;

        Vector3 scale = transform.localScale;
        scale.x = Mathf.Abs(initialScaleX) * -flipDir;
        transform.localScale = scale;

        Debug.Log($"🌀 Flip 적용됨 → NetFlipX: {NetFlipX}, scale.x: {scale.x}");
    }

    public void ForceStopActionsOnDeath()
    {
        if (!Object.HasStateAuthority) return;

        // 공격/재장전/스킬 입력·코루틴·애니 전부 해제
        isReloading = false;
        isUsingSkill = false;
        attackCooldownTimer = TickTimer.None;
        reloadTimer = TickTimer.None;
        StopAllCoroutines();
        if (anim != null) anim.SetShootState(false);

        // 로컬 입력도 해제(홀드 중인 공격 버튼 등)
        RPC_ClearHeldInputsOnDeath();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_ClearHeldInputsOnDeath()
    {
        if (localInput != null)
        {
            localInput.isAttackButtonHeld = false;
            localInput.skillPressed = false;
            // 재장전 버튼/게이지 등은 UI 쪽에서 사망 시 Hide 됨
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplyKnockback(Vector2 fromBulletToTarget, float hopX, float hopY, float lockSeconds, bool opposite = true)
    {
        isKnockBack = true;

        // ✨ 점프처럼 "툭" 튀게: 기존 속도 제거 후 즉시 속도 세팅
        rb.linearVelocity = Vector2.zero;

        // fromBulletToTarget: (총알 → 타겟) 방향
        // opposite=true 면 총알 진행 반대(= 타겟 → 총알)로 넉백
        float xSign = Mathf.Sign(opposite ? -fromBulletToTarget.x : fromBulletToTarget.x);
        if (Mathf.Approximately(xSign, 0f)) xSign = 1f; // 수평 0이면 기본 우측

        // 수평은 고정량(hopX), 수직은 살짝 띄우기(hopY)
        rb.linearVelocity = new Vector2(xSign * hopX, hopY);

        // 잠깐 락 후 해제
        StartCoroutine(Co_UnlockMovementAfter(lockSeconds));
    }

    private IEnumerator Co_UnlockMovementAfter(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += (Runner != null ? Runner.DeltaTime : Time.deltaTime);
            yield return null;
        }
        isKnockBack = false;
    }

    public void PauseJumpAnim() => anim.PauseJumpAnim();

    public void PlayOneShotSfx(UnityEngine.AudioClip clip, UnityEngine.Audio.AudioMixerGroup mixerGroup)
    {
        if (shootAudioSource == null || clip == null) return;

        var prev = shootAudioSource.outputAudioMixerGroup;
        if (mixerGroup != null)
            shootAudioSource.outputAudioMixerGroup = mixerGroup;

        shootAudioSource.PlayOneShot(clip);

        // 원래 믹서로 복원
        shootAudioSource.outputAudioMixerGroup = prev;
    }

    [Fusion.Rpc(Fusion.RpcSources.StateAuthority, Fusion.RpcTargets.All)]
    private void RPC_PlayJumpSfx()
    {
        PlayOneShotSfx(jumpAudioClip, jumpMixerGroup);
    }
}