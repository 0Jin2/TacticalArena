using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;
using UnityEngine.Audio;

public class Health : NetworkBehaviour
{
    [Networked(OnChanged = nameof(OnHPChanged))]
    public int currentHP { get; set; }
    [Networked(OnChanged = nameof(OnIsDeadChanged))]
    public bool IsDead { get; private set; }
    [Networked] private PlayerRef lastDamagedBy { get; set; }

    public CharacterStatData statData;
    public event System.Action<int> OnHealthChanged;
    public event System.Action OnDeath;
    public static System.Action<PlayerRef, NetworkObject> OnAnyDeath;

    // === 💥 피격 연출(오디오/색상) ===
    [Header("Hit Feedback")]
    [Tooltip("플레이어 피격 시 재생할 오디오 소스")]
    public AudioSource playerHitAudioSource;
    public AudioClip playerHitClip;
    public AudioMixerGroup playerHitMixerGroup;

    [Tooltip("베리어 피격 시 재생할 오디오 소스")]
    public AudioSource barrierHitAudioSource;
    public AudioClip barrierHitClip;
    public AudioMixerGroup barrierHitMixerGroup;

    [Tooltip("플레이어 피격 시 잠시 바꿀 색상")]
    public Color playerFlashColor = new Color(1f, 0.4f, 0.4f, 1f);

    [Tooltip("플레이어 피격 색상 유지 시간(초)")]
    public float playerFlashSeconds = 0.3f;

    private PlayerAnimationHandler animHandler;
    private float damageReduction = 0f;
    private int tempShieldHP = 0;
    private bool isDead = false;
    private Coroutine deathRoutine;
    private bool invulnerable = false;   // ✅ 무적 여부
    private Coroutine invulRoutine;
    private Coroutine flashRoutine;
    private Dictionary<SpriteRenderer, Color> originalColors = new Dictionary<SpriteRenderer, Color>();

    public override void Spawned()
    {
        animHandler = GetComponent<PlayerAnimationHandler>();
        if (Object.HasStateAuthority)
            currentHP = statData.maxHP;

        var bar = GetComponentInChildren<HealthBar>();
        if (bar != null) bar.Init(this);

        // 오디오 소스 자동 보정(없으면 붙여줌)
        if (playerHitAudioSource == null)
            playerHitAudioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        if (barrierHitAudioSource == null)
            barrierHitAudioSource = gameObject.AddComponent<AudioSource>();

        if (playerHitMixerGroup != null) playerHitAudioSource.outputAudioMixerGroup = playerHitMixerGroup;
        if (barrierHitMixerGroup != null) barrierHitAudioSource.outputAudioMixerGroup = barrierHitMixerGroup;

        OnDeath += HandleDeath;

        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalColors.Clear();
        foreach (var r in renderers)
        {
            if (r != null && !originalColors.ContainsKey(r))
                originalColors.Add(r, r.color);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ApplyDamage(int dmg, PlayerRef attacker)
    {
        TakeDamage(dmg, attacker);
    }

    public void SetDamageReduction(float percent)
    {
        damageReduction = percent;
    }

    public void AddTempShield(int hp)
    {
        tempShieldHP = hp;
    }

    public void RemoveTempShield()
    {
        tempShieldHP = 0;
    }

    public void TakeDamage(int dmg, PlayerRef attacker)
    {
        if (!Object.HasStateAuthority) return;
        if (invulnerable) return;

        lastDamagedBy = attacker;

        if (tempShieldHP > 0)
        {
            int left = tempShieldHP - dmg;
            if (left >= 0) { tempShieldHP = left; dmg = 0; }
            else { dmg = -left; tempShieldHP = 0; }
        }

        dmg = Mathf.RoundToInt(dmg * (1f - damageReduction));
        currentHP = Mathf.Max(currentHP - dmg, 0);

        // 💥 피격 연출을 모든 클라에서 동기화
        RPC_PlayHitFeedback();

        if (currentHP == 0 && !isDead)
        {
            isDead = true;
            IsDead = true;
            OnDeath?.Invoke();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayHitFeedback()
    {
        int myLayer = gameObject.layer;
        bool isPlayerLayer = myLayer == LayerMask.NameToLayer("Player");
        bool isBarrierLayer = myLayer == LayerMask.NameToLayer("Barrier");

        if (isPlayerLayer)
        {
            if (playerHitClip != null && playerHitAudioSource != null)
                playerHitAudioSource.PlayOneShot(playerHitClip);

            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(Co_FlashPlayerDamage(playerFlashColor, playerFlashSeconds));
        }
        else if (isBarrierLayer)
        {
            if (barrierHitClip != null && barrierHitAudioSource != null)
                barrierHitAudioSource.PlayOneShot(barrierHitClip);
        }
    }

    private IEnumerator Co_FlashPlayerDamage(Color flashColor, float seconds)
    {
        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        // 1) 플래시 색상 적용
        foreach (var r in renderers)
        {
            if (r != null) r.color = flashColor;
        }

        // 2) 대기
        yield return new WaitForSeconds(seconds);

        // 3) 원래 색상 복원
        foreach (var r in renderers)
        {
            if (r != null && originalColors.ContainsKey(r))
                r.color = originalColors[r];
        }
    }

    public static void OnHPChanged(Changed<Health> changed)
    {
        changed.Behaviour.OnHealthChanged?.Invoke(changed.Behaviour.currentHP);
    }

    private bool IsPlayerObject()
    {
        // PlayerNetworkController가 붙어 있으면 '플레이어'로 간주
        return GetComponent<PlayerNetworkController>() != null;
    }

    private void HandleDeath()
    {
        // ✅ 플레이어일 때만 UI 숨김
        if (IsPlayerObject())
        {
            var netObj = GetComponent<NetworkObject>();
            if (netObj != null && netObj.HasInputAuthority)
            {
                GamePlayUIController.Instance?.HideGameplayUI();
                GamePlayUIController.Instance?.ShowDeathUI();
            }
        }

        // ✅ PlayerObject 매핑 해제 (Respawn 필수)
        if (Runner != null && Object != null && Object.HasInputAuthority)
        {
            var player = Object.InputAuthority;
            if (Runner.TryGetPlayerObject(player, out var mapped) && mapped == Object)
            {
                Runner.SetPlayerObject(player, null);
                Debug.Log($"🗑 PlayerObject 매핑 해제 완료: {player}");
            }
        }

        // 🔹 컨트롤러의 모든 코루틴/입력/사격을 즉시 정지
        var ctrl = GetComponent<PlayerNetworkController>();
        if (ctrl != null)
        {
            // 코루틴/사격/재장전/입력 해제까지 서버에서 보장
            ctrl.ForceStopActionsOnDeath();
        }

        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = StartCoroutine(DelayedDieTrigger());

        // 사망 레이어 지정(방벽에도 안전)
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("DeadBody"));

        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = StartCoroutine(DelayedDieTrigger());

        // ✅ 킬/데스 집계는 '플레이어'만
        if (IsPlayerObject())
        {
            MatchStatusTracker.Instance?.RPC_RegisterDeath(Object.InputAuthority);
            MatchStatusTracker.Instance?.RPC_RegisterKill(lastDamagedBy, Object.InputAuthority);
        }
    }

    public void ForceKillWithoutFeedback(PlayerRef attacker)
    {
        if (!Object.HasStateAuthority) return;

        lastDamagedBy = attacker;
        currentHP = 0;

        if (!isDead)
        {
            isDead = true;
            IsDead = true;
            OnDeath?.Invoke();   // ✅ 기존 죽음 처리 흐름(애니메이션, UI 등)은 그대로 실행
        }
    }

    private IEnumerator DelayedDieTrigger()
    {
        animHandler?.PlayDieTrigger();
        yield return StartCoroutine(WaitForGroundAndDisable());
    }

    public static void OnIsDeadChanged(Changed<Health> changed)
    {
        changed.LoadOld();
        bool wasDead = changed.Behaviour.IsDead;

        changed.LoadNew();
        bool isNowDead = changed.Behaviour.IsDead;

        if (!wasDead && isNowDead)
        {
            var beh = changed.Behaviour;

            // ✅ 플레이어만 OnAnyDeath 알림
            if (beh.GetComponent<PlayerNetworkController>() != null)
            {
                var netObj = beh.GetComponent<NetworkObject>();
                OnAnyDeath?.Invoke(beh.Object.InputAuthority, netObj);
            }
        }
    }

    private IEnumerator WaitForGroundAndDisable()
    {
        while (IsDead)
        {
            if (IsGrounded()) break;
            yield return null;
        }

        if (!IsDead) yield break;

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.simulated = false;
        foreach (var col in GetComponents<Collider2D>()) col.enabled = false;
    }

    private bool IsGrounded()
    {
        Vector2 position = transform.position;
        Vector2 size = new Vector2(0.5f, 0.1f);
        return Physics2D.BoxCast(position, size, 0f, Vector2.down, 0.1f, LayerMask.GetMask("Ground"));
    }

    private void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, newLayer);
    }

    public void ServerRespawn(Vector3 pos, Quaternion rot)
    {
        if (!Object.HasStateAuthority) return;

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }

        // 1) 물리/콜라이더 정상화
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.simulated = true;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        foreach (var col in GetComponents<Collider2D>()) col.enabled = true;
        SetLayerRecursively(gameObject, LayerMask.NameToLayer("Player"));

        // 2) 위치/회전 복구
        if (rb != null)
        {
            rb.position = pos;
            transform.rotation = rot;
        }
        else
        {
            transform.SetPositionAndRotation(pos, rot);
        }

        // 3) 체력/플래그 리셋
        if (statData != null)
            currentHP = statData.maxHP;

        isDead = false;
        IsDead = false;

        RPC_StartInvulnerability();

        // ✅ 4) PlayerObject 재매핑
        if (Runner != null && Runner.IsRunning)
        {
            var player = Object.InputAuthority;
            if (!Runner.TryGetPlayerObject(player, out var mapped) || mapped != Object)
            {
                Runner.SetPlayerObject(player, Object);
                Debug.Log($"🔁 PlayerObject 재매핑 완료: {player}");
            }
        }

        // 5) 부가 플래그/UI
        MatchStatusTracker.Instance?.RPC_ClearDeathFlag(Object.InputAuthority);
        animHandler?.ResetDieState();
        RPC_OnRespawned(); // 입력권자 UI 표시
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.InputAuthority)]
    private void RPC_OnRespawned()
    {
        GamePlayUIController.Instance?.HideDeathUI();
        GamePlayUIController.Instance?.ShowGameplayUI();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_StartInvulnerability()
    {
        if (invulRoutine != null) StopCoroutine(invulRoutine);
        invulRoutine = StartCoroutine(Co_RespawnInvulnerability());
    }

    private IEnumerator Co_RespawnInvulnerability()
    {
        invulnerable = true;

        float elapsed = 0f;
        float duration = 2f;
        float blinkInterval = 0.2f;

        var renderers = GetComponentsInChildren<SpriteRenderer>(true);

        while (elapsed < duration)
        {
            // 반투명
            foreach (var r in renderers)
                if (r != null) r.color = new Color(1f, 1f, 1f, 0.5f);

            yield return new WaitForSeconds(blinkInterval);

            // 불투명
            foreach (var r in renderers)
                if (r != null) r.color = new Color(1f, 1f, 1f, 1f);

            yield return new WaitForSeconds(blinkInterval);

            elapsed += blinkInterval * 2f;
        }

        // ✅ 원래 색상으로 복구
        foreach (var r in renderers)
        {
            if (r != null && originalColors.ContainsKey(r))
                r.color = originalColors[r];
        }

        invulnerable = false;
        invulRoutine = null;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRespawn(Vector3 pos, Quaternion rot)
    {
        ServerRespawn(pos, rot);
    }
}