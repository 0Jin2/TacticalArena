using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerNetSync : 캐릭터 위치/상태 동기화
//
//  내 캐릭터   : 초당 sendRate 번 PlayerState 패킷을 보낸다. (위치, 속도, 방향, 체력, 지면/사격/사망 플래그)
//               가만히 있을 때는 idleSendInterval 마다만 보낸다. (전송량 절약 - 10KB/s 한도 보호)
//  남의 캐릭터 : 받은 상태를 목표로 부드럽게 따라간다. 너무 멀어지면 순간이동.
//
//  유실돼도 괜찮은 정보(위치)이므로 critical 이 아니다. 다음 패킷이 곧 덮어쓴다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerNetSync : MonoBehaviour
{
    [Header("전송 (내 캐릭터)")]
    [Range(5f, 30f)] public float sendRate = 15f;
    [Tooltip("움직임이 없을 때 전송 간격(초)")]
    public float idleSendInterval = 0.25f;
    public float movedEpsilon = 0.01f;

    [Header("보간 (남의 캐릭터)")]
    [Tooltip("클수록 목표 위치에 빨리 붙는다")]
    public float smoothing = 18f;
    [Tooltip("목표와 이 거리 이상 벌어지면 순간이동")]
    public float snapDistance = 4f;
    [Tooltip("패킷이 늦을 때 속도로 예측해 줄 최대 시간(초)")]
    public float maxExtrapolation = 0.2f;

    private const byte FlagGrounded = 1;
    private const byte FlagShooting = 2;
    private const byte FlagDead = 4;

    private PlayerEntity entity;
    private Rigidbody2D body;

    // 보내기
    private float sendTimer;
    private Vector2 lastSentPos;
    private byte lastSentFlags;
    private float lastSentTime;

    // 받기
    private bool hasState;
    private Vector2 targetPos;
    private Vector2 targetVel;
    private float lastReceiveTime;
    private bool remoteGrounded;

    public Vector2 LastKnownPosition { get { return entity != null && entity.IsMine ? (Vector2)transform.position : targetPos; } }
    public bool HasReceivedState { get { return hasState; } }

    public void Setup(PlayerEntity owner)
    {
        entity = owner;
        body = GetComponent<Rigidbody2D>();
        targetPos = transform.position;
        lastSentPos = targetPos;
        hasState = entity.IsMine;
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region 보내기

    private void Update()
    {
        if (entity == null || !entity.IsInitialized) return;

        if (entity.IsMine) UpdateSend();
        else UpdateRemote();
    }

    private void UpdateSend()
    {
        if (!entity.IsPresent) return;

        sendTimer += Time.deltaTime;
        float interval = 1f / Mathf.Max(1f, sendRate);
        if (sendTimer < interval) return;

        Vector2 pos = body != null ? body.position : (Vector2)transform.position;
        byte flags = BuildFlags();
        bool still = (pos - lastSentPos).sqrMagnitude < movedEpsilon * movedEpsilon && flags == lastSentFlags;
        if (still && Time.time - lastSentTime < idleSendInterval) return;

        sendTimer = 0f;
        SendNow();
    }

    private byte BuildFlags()
    {
        byte flags = 0;
        if (entity.Motor != null && entity.Motor.IsGrounded) flags |= FlagGrounded;
        if (entity.Combat != null && entity.Combat.IsShooting) flags |= FlagShooting;
        if (entity.Health != null && entity.Health.IsDead) flags |= FlagDead;
        return flags;
    }

    /// <summary>지금 상태를 즉시 전송 (피격 직후 체력 갱신, 사망 등)</summary>
    public void SendNow()
    {
        var net = InGameNet.Instance;
        if (net == null || entity == null || !entity.IsMine) return;

        Vector2 pos = body != null ? body.position : (Vector2)transform.position;
        Vector2 vel = body != null && body.simulated ? body.linearVelocity : Vector2.zero;
        float facing = entity.Motor != null ? entity.Motor.Facing : 1f;
        int hp = entity.Health != null ? entity.Health.CurrentHP : 0;
        byte flags = BuildFlags();

        net.Send(NetPacketType.PlayerState, w =>
        {
            w.WriteVector2(pos);
            w.WriteVector2(vel);
            w.WriteByte(facing < 0f ? (byte)0 : (byte)1);
            w.WriteUShort((ushort)Mathf.Clamp(hp, 0, ushort.MaxValue));
            w.WriteByte(flags);
        });

        lastSentPos = pos;
        lastSentFlags = flags;
        lastSentTime = Time.time;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 받기 (남의 캐릭터)

    public void ApplyState(NetReader r)
    {
        if (entity == null || entity.IsMine) return;

        targetPos = r.ReadVector2();
        targetVel = r.ReadVector2();
        float facing = r.ReadByte() == 0 ? -1f : 1f;
        int hp = r.ReadUShort();
        byte flags = r.ReadByte();

        lastReceiveTime = Time.time;

        if (!hasState)
        {
            hasState = true;
            SnapTo(targetPos);       // 첫 패킷: 스폰 위치에서 날아오지 않고 바로 이동
        }

        if (entity.Motor != null)
        {
            entity.Motor.SetFacing(facing);
            remoteGrounded = (flags & FlagGrounded) != 0;
            entity.Motor.SetRemoteGrounded(remoteGrounded);
        }

        if (entity.Combat != null) entity.Combat.SetRemoteShooting((flags & FlagShooting) != 0);

        if (entity.Health != null)
        {
            bool dead = (flags & FlagDead) != 0;
            if (dead && !entity.Health.IsDead) entity.Health.RemoteDie(0);
            else if (!dead && !entity.Health.IsDead) entity.Health.SetRemoteHp(hp);
        }
    }

    public void SnapTo(Vector2 position)
    {
        targetPos = position;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
        if (body != null) body.position = position;
    }

    private void UpdateRemote()
    {
        if (!hasState || !entity.IsPresent) return;

        float since = Mathf.Min(Time.time - lastReceiveTime, maxExtrapolation);
        Vector2 predicted = targetPos + targetVel * since;
        Vector2 current = transform.position;

        if ((predicted - current).sqrMagnitude > snapDistance * snapDistance)
            current = predicted;
        else
            current = Vector2.Lerp(current, predicted, 1f - Mathf.Exp(-smoothing * Time.deltaTime));

        transform.position = new Vector3(current.x, current.y, transform.position.z);

        // 애니메이션: 받은 속도로 흉내
        if (entity.Anim != null && entity.Health != null && !entity.Health.IsDead)
        {
            bool moving = Mathf.Abs(targetVel.x) > 0.1f;
            float moveX = moving ? Mathf.Abs(targetVel.x) : 0f;
            entity.Anim.SetLocomotion(moveX, remoteGrounded, targetVel.y);
        }
    }

    #endregion
}
