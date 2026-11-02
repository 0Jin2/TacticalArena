using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerMotor : 이동 / 점프 / 아래점프 / 넉백 / 좌우 반전
//
//  내 캐릭터  : 입력(PlayerInput)을 읽어 물리(Rigidbody2D)로 움직인다. 결과는 PlayerNetSync 가 전송.
//  남의 캐릭터: 물리를 끄고(Kinematic) PlayerNetSync 가 받은 위치로만 움직인다.
//
//  수치는 CharacterStatData(moveSpeed, jumpPower, allowDoubleJump)에서 읽고,
//  스킬이 일시적으로 바꾸는 값은 "배율(SpeedMultiplier 등)"로 따로 둔다.
//  → 기존 코드처럼 공유 ScriptableObject 를 직접 수정하지 않으므로 에셋이 오염되지 않는다.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMotor : MonoBehaviour
{
    [Header("지면 판정")]
    [Tooltip("땅으로 취급할 레이어. 비어 있으면 'Ground' 레이어를 사용한다.")]
    public LayerMask groundLayers;
    public Vector2 groundCheckSize = new Vector2(0.5f, 0.1f);
    public float groundCheckDistance = 0.1f;

    [Header("아래 점프 (조이스틱을 아래로 + 점프)")]
    [Range(-1f, 0f)] public float dropStickThreshold = -0.5f;
    public float dropRayDistance = 0.2f;
    public float dropThroughSeconds = 0.4f;
    [Tooltip("이 태그와 Ground 레이어를 모두 가진 발판만 통과할 수 있다.")]
    public string dropGroundTag = "Ground";

    [Header("입력")]
    [Range(0f, 0.9f)] public float moveDeadZone = 0.2f;

    [Header("점프 효과음")]
    public AudioClip jumpClip;
    public AudioMixerGroup jumpMixer;

    // ── 스킬이 건드리는 임시 배율 (1 = 기본) ────────────────────────────────
    public float SpeedMultiplier { get; set; }
    public float JumpMultiplier { get; set; }

    // ── 상태 ────────────────────────────────────────────────────────────────
    public float Facing { get; private set; }               // +1 오른쪽, -1 왼쪽
    public bool IsGrounded { get; private set; }
    public bool IsKnockedBack { get; private set; }
    public bool MovementLocked { get; private set; }
    public Vector2 Velocity { get { return body != null ? body.linearVelocity : Vector2.zero; } }

    public float CurrentMoveSpeed
    {
        get { return (entity != null && entity.stat != null ? entity.stat.moveSpeed : 5f) * SpeedMultiplier; }
    }

    private PlayerEntity entity;
    private Rigidbody2D body;
    private Collider2D bodyCollider;
    private float initialScaleX = 1f;
    private bool canDoubleJump;
    private bool wasGrounded;
    private float knockbackEndTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        initialScaleX = Mathf.Abs(transform.localScale.x) < 0.0001f ? 1f : transform.localScale.x;
        SpeedMultiplier = 1f;
        JumpMultiplier = 1f;
        Facing = 1f;
    }

    private void Reset()
    {
        groundLayers = LayerMask.GetMask(GameLayers.GroundName);
    }

    public void Setup(PlayerEntity owner)
    {
        entity = owner;
        if (groundLayers.value == 0) groundLayers = LayerMask.GetMask(GameLayers.GroundName);

        if (entity.IsMine)
        {
            body.bodyType = RigidbodyType2D.Dynamic;
            body.simulated = true;
        }
        else
        {
            // 남의 캐릭터: 받은 위치로만 이동 (물리 힘을 받지 않는다)
            body.bodyType = RigidbodyType2D.Kinematic;
            body.linearVelocity = Vector2.zero;
        }
        ApplyFlip();
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region 지면 / 방향

    public bool QueryGrounded()
    {
        var hit = Physics2D.BoxCast((Vector2)transform.position, groundCheckSize, 0f, Vector2.down, groundCheckDistance, groundLayers);
        return hit.collider != null;
    }

    /// <summary>남의 캐릭터: 받은 패킷의 지면 상태 반영</summary>
    public void SetRemoteGrounded(bool grounded) { IsGrounded = grounded; }

    public void SetFacing(float facing)
    {
        float sign = facing < 0f ? -1f : 1f;
        if (Mathf.Approximately(sign, Facing)) return;
        Facing = sign;
        ApplyFlip();
    }

    /// <summary>
    /// 기존 규칙 그대로: scale.x = |초기 scale.x| * -방향.
    /// (스프라이트가 왼쪽을 보고 그려져 있어서, 오른쪽을 볼 땐 scale.x 가 음수)
    /// </summary>
    private void ApplyFlip()
    {
        var scale = transform.localScale;
        scale.x = Mathf.Abs(initialScaleX) * -Facing;
        transform.localScale = scale;
    }

    /// <summary>위치와 방향을 즉시 옮긴다. (스폰/부활)</summary>
    public void Teleport(Vector2 position, float facing)
    {
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        if (body != null)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        IsKnockedBack = false;
        MovementLocked = false;
        Facing = facing < 0f ? -1f : 1f;
        ApplyFlip();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 내 캐릭터 이동

    private void FixedUpdate()
    {
        if (entity == null || !entity.IsInitialized || !entity.IsMine) return;

        bool grounded = QueryGrounded();
        if (grounded && !wasGrounded)
            canDoubleJump = entity.stat != null && entity.stat.allowDoubleJump;
        wasGrounded = grounded;
        IsGrounded = grounded;

        if (IsKnockedBack && Time.time >= knockbackEndTime) IsKnockedBack = false;
        if (!body.simulated) return;

        var input = entity.Input;
        if (!entity.ControlEnabled || input == null)
        {
            // 조작 불가(사망/카운트다운/연결 끊김): 옆으로 미끄러지지 않게 정지
            if (!IsKnockedBack) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            UpdateAnimator(0f, grounded);
            return;
        }

        float mx = input.MoveInput.x;
        float dir = Mathf.Abs(mx) < moveDeadZone ? 0f : Mathf.Sign(mx);
        bool jumpRequested = input.ConsumeJump();

        if (MovementLocked)
        {
            body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
            UpdateAnimator(0f, grounded);
            return;
        }

        if (!IsKnockedBack)
        {
            body.linearVelocity = new Vector2(dir * CurrentMoveSpeed, body.linearVelocity.y);
            if (dir != 0f) SetFacing(dir);
        }

        UpdateAnimator(dir != 0f ? CurrentMoveSpeed : 0f, grounded);

        if (jumpRequested && !IsKnockedBack)
        {
            if (input.MoveInput.y < dropStickThreshold && TryDropDown()) return;
            DoJump(grounded);
        }
    }

    private void UpdateAnimator(float moveX, bool grounded)
    {
        if (entity.Anim != null) entity.Anim.SetLocomotion(moveX, grounded, body.linearVelocity.y);
    }

    private void DoJump(bool grounded)
    {
        if (entity.stat == null) return;
        float power = entity.stat.jumpPower * JumpMultiplier;

        if (grounded)
        {
            body.linearVelocity = new Vector2(body.linearVelocity.x, power);
            canDoubleJump = entity.stat.allowDoubleJump;
            OnJumped();
        }
        else if (canDoubleJump)
        {
            body.linearVelocity = new Vector2(body.linearVelocity.x, power);
            canDoubleJump = false;
            OnJumped();
        }
    }

    /// <summary>남의 캐릭터가 점프했다는 CombatEvent 를 받았을 때 효과음만 재생</summary>
    public void PlayRemoteJump()
    {
        if (entity != null && entity.Audio != null) entity.Audio.PlayOneShot(jumpClip, jumpMixer);
    }

    private void OnJumped()
    {
        if (entity.Audio != null) entity.Audio.PlayOneShot(jumpClip, jumpMixer);
        var net = InGameNet.Instance;
        if (net != null) net.Send(NetPacketType.CombatEvent, w => w.WriteByte(CombatEventKind.Jump));
    }

    private bool TryDropDown()
    {
        if (bodyCollider == null) return false;

        var hit = Physics2D.Raycast(transform.position, Vector2.down, dropRayDistance, groundLayers);
        if (hit.collider == null) return false;

        bool isGroundLayer = hit.collider.gameObject.layer == GameLayers.Ground;
        bool isGroundTag = string.IsNullOrEmpty(dropGroundTag) || hit.collider.CompareTag(dropGroundTag);
        if (!isGroundLayer || !isGroundTag) return false;

        StartCoroutine(Co_IgnoreGround(bodyCollider, hit.collider));
        return true;
    }

    private Collider2D droppedGround;

    private IEnumerator Co_IgnoreGround(Collider2D mine, Collider2D ground)
    {
        RestoreDroppedGround();
        droppedGround = ground;
        Physics2D.IgnoreCollision(mine, ground, true);
        yield return new WaitForSeconds(dropThroughSeconds);
        RestoreDroppedGround();
    }

    private void RestoreDroppedGround()
    {
        if (droppedGround != null && bodyCollider != null) Physics2D.IgnoreCollision(bodyCollider, droppedGround, false);
        droppedGround = null;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 스킬/넉백이 쓰는 제어

    /// <summary>스킬 중 이동 잠금. locked=true 면 가로 속도도 0으로 만든다.</summary>
    public void SetMovementLock(bool locked)
    {
        MovementLocked = locked;
        if (locked && body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
    }

    /// <summary>가로 속도만 한 번 멈춘다. (이동 잠금 없이 "멈칫" 만 줄 때)</summary>
    public void StopHorizontal()
    {
        if (body != null) body.linearVelocity = new Vector2(0f, body.linearVelocity.y);
    }

    /// <summary>
    /// 넉백 (내 기기에서만 호출). fromBulletToTarget 은 총알 → 대상 방향.
    /// opposite=true 면 총알 진행의 반대쪽으로 날아간다.
    /// </summary>
    public void ApplyKnockback(Vector2 fromBulletToTarget, float hopX, float hopY, float lockSeconds, bool opposite)
    {
        if (entity == null || !entity.IsMine || body == null || !body.simulated) return;

        IsKnockedBack = true;
        knockbackEndTime = Time.time + Mathf.Max(0f, lockSeconds);

        float xSign = Mathf.Sign(opposite ? -fromBulletToTarget.x : fromBulletToTarget.x);
        if (Mathf.Approximately(xSign, 0f)) xSign = 1f;

        body.linearVelocity = new Vector2(xSign * hopX, hopY);
    }

    /// <summary>사망/부활 등으로 모든 임시 상태를 초기화한다.</summary>
    public void ResetState()
    {
        StopAllCoroutines();
        RestoreDroppedGround();
        IsKnockedBack = false;
        MovementLocked = false;
        SpeedMultiplier = 1f;
        JumpMultiplier = 1f;
    }

    #endregion
}

/// <summary>CombatEvent 패킷의 종류 번호</summary>
public static class CombatEventKind
{
    public const byte Reload = 1;     // [반복 횟수 byte][구간 시간 float]
    public const byte Jump = 2;       // 점프 효과음
}
