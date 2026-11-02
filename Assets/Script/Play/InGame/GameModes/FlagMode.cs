using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// FlagMode : 깃발 뺏기(CTF) 모드  (기존 FlagModeHandler 대체)
//
//  규칙 (기존과 동일)
//   1) 상대 팀의 깃발 자리(FlagPickupZone)에 들어가면 그 깃발을 "들고" 간다. (이미 누가 들고 있으면 불가)
//   2) 깃발을 든 채로 우리 팀 골 지점(FlagGoalZone)에 들어가면 우리 팀 +1점, 깃발은 제자리로.
//   3) 깃발을 든 캐릭터가 죽으면 깃발은 즉시 제자리로 돌아간다. (깃발은 바닥에 떨어지지 않음)
//   4) 목표 점수(ModeConfig.scoreLimit)에 먼저 도달하면 승리. 시간이 끝나면 점수 높은 팀 승리, 같으면 무승부.
//   5) 죽으면 부활 대기 후 부활 (라운드 내내 부활 가능)
//
//  네트워크 방식
//   - 줍기/득점은 "내 기기의 캐릭터"가 존에 들어갔을 때 호스트에게 요청(ModeRequest)
//   - 호스트가 검사 후 결과를 ModeEvent 로 모두에게 알림 → 모든 기기가 같은 순서로 반영
//   - 추가 보강(기존에 없던 안전장치): 운반자가 매치에서 영구 이탈하면 깃발을 되돌린다.
// ─────────────────────────────────────────────────────────────────────────────

public class FlagMode : GameModeBase
{
    public static FlagMode Instance { get; private set; }

    [Header("표시 문구")]
    [Tooltip("{0} 자리에 목표 점수가 들어갑니다.")]
    public string goalFormat = "목표 점수 : {0}";

    [Header("안내 문구 (비워 두면 표시하지 않음)")]
    public string msgFlagStolen = "깃발을 뺏겼습니다!";           // 내 팀 깃발을 적이 가져감
    public string msgFlagTaken = "적 깃발을 탈취했습니다!";        // 우리 팀이 적 깃발을 가져옴
    public string msgScored = "득점 성공!";
    public string msgEnemyScored = "상대가 득점했습니다!";
    [Tooltip("깃발 운반자가 죽거나 이탈해서 깃발이 제자리로 돌아갔을 때 (기존에는 없던 안내, 기본 꺼짐)")]
    public string msgFlagReturned = "";

    [Header("요청 간격")]
    [Tooltip("같은 요청을 이 시간 안에 다시 보내지 않습니다. (존 안에 서 있는 동안 패킷 폭주 방지)")]
    public float requestIntervalSeconds = 0.5f;

    // ── 패킷 종류 ───────────────────────────────────────────────────────────
    private const byte ReqPickup = 1;       // [flagOwnerTeam byte]
    private const byte ReqScore = 2;        // [goalTeam byte]
    private const byte EvCarry = 1;         // [flagOwnerTeam byte][carrier uint]  (carrier 0 = 제자리)
    private const byte EvAlert = 2;         // [targetTeam byte (None=모두)][message byte]

    private const byte MsgStolen = 1, MsgTaken = 2, MsgScored = 3, MsgEnemyScored = 4, MsgReturned = 5;

    // 팀(=깃발 주인)별 운반자 세션. index = (int)NetTeam. 0 = 아무도 안 듦
    private readonly uint[] carrier = new uint[3];      // 화면에 반영된 값 (모든 기기, 이벤트로만 변경)
    private readonly uint[] decided = new uint[3];      // 호스트가 판정한 값 (에코 도착 전 중복 방지용)

    private float lastPickupReqAt = -100f, lastScoreReqAt = -100f;

    public int CaptureGoal { get { return Config != null ? Config.scoreLimit : 0; } }

    public override string GoalText
    {
        get { return CaptureGoal > 0 ? string.Format(goalFormat, CaptureGoal) : string.Empty; }
    }

    protected override MatchState.ScoreRule ScoreRule { get { return MatchState.ScoreRule.Manual; } }

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    protected override void Awake()
    {
        base.Awake();
        if (Current == this) Instance = this;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        PlayerEntity.OnSpawned += HandleEntitySpawned;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        PlayerEntity.OnSpawned -= HandleEntitySpawned;
    }

    protected override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    protected override void OnPlayingStarted(bool restored)
    {
        if (!restored)
        {
            for (int i = 0; i < carrier.Length; i++) { carrier[i] = 0; decided[i] = 0; }
        }
        RefreshAllVisuals();
        RaiseModeChanged();
    }

    protected override void OnResultStarted()
    {
        // 결과 화면에서는 깃발 표시를 모두 끈다
        for (int i = 0; i < carrier.Length; i++) { carrier[i] = 0; decided[i] = 0; }
        RefreshAllVisuals();
    }

    private void HandleEntitySpawned(PlayerEntity entity)
    {
        // 재접속/지연 생성으로 캐릭터가 나중에 만들어지는 경우: 들고 있던 깃발 표시를 맞춘다
        RefreshVisual(entity);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 조회 (존/HUD 가 사용)

    /// <summary>이 유저가 들고 있는 깃발의 주인 팀 (안 들었으면 None)</summary>
    public NetTeam CarriedFlagOf(uint session)
    {
        if (session == 0) return NetTeam.None;
        if (carrier[(int)NetTeam.Red] == session) return NetTeam.Red;
        if (carrier[(int)NetTeam.Blue] == session) return NetTeam.Blue;
        return NetTeam.None;
    }

    public bool IsCarryingFlag(uint session) { return CarriedFlagOf(session) != NetTeam.None; }

    /// <summary>flagOwnerTeam 의 깃발을 현재 들고 있는 유저 세션 (없으면 0)</summary>
    public uint CarrierOf(NetTeam flagOwnerTeam) { return carrier[(int)flagOwnerTeam]; }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 요청 보내기 (존에서 호출 - 내 기기)

    /// <summary>[내 기기] flagOwnerTeam 깃발 줍기를 호스트에게 요청한다.</summary>
    public void RequestPickup(NetTeam flagOwnerTeam)
    {
        if (!IsPlaying || flagOwnerTeam == NetTeam.None) return;
        var me = PlayerEntity.Local;
        if (me == null || me.Info == null || me.Health == null || me.Health.IsDead) return;
        if (me.Team == flagOwnerTeam) return;                        // 내 팀 깃발은 못 듦
        if (carrier[(int)flagOwnerTeam] != 0) return;                // 이미 누가 들고 있음
        if (Time.unscaledTime - lastPickupReqAt < requestIntervalSeconds) return;
        lastPickupReqAt = Time.unscaledTime;

        SendRequest(ReqPickup, w => w.WriteByte((byte)flagOwnerTeam));
    }

    /// <summary>[내 기기] 골 지점에 도착했음을 알리고 득점을 요청한다.</summary>
    public void RequestScore(NetTeam goalTeam)
    {
        if (!IsPlaying || goalTeam == NetTeam.None) return;
        var me = PlayerEntity.Local;
        if (me == null || me.Info == null || me.Health == null || me.Health.IsDead) return;
        if (me.Team != goalTeam) return;
        if (CarriedFlagOf(me.SessionId) == NetTeam.None) return;     // 깃발을 안 들었으면 요청 안 함
        if (Time.unscaledTime - lastScoreReqAt < requestIntervalSeconds) return;
        lastScoreReqAt = Time.unscaledTime;

        SendRequest(ReqScore, w => w.WriteByte((byte)goalTeam));
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 호스트: 요청 판정

    protected override void OnModeRequest(byte kind, NetReader reader, uint sender)
    {
        if (!IsPlaying) return;
        var player = Net.GetPlayer(sender);
        if (player == null || player.IsForfeited) return;
        if (State != null && State.IsDead(sender)) return;           // 죽은 유저의 요청은 무시

        switch (kind)
        {
            case ReqPickup: HostHandlePickup(player, (NetTeam)reader.ReadByte()); break;
            case ReqScore: HostHandleScore(player, (NetTeam)reader.ReadByte()); break;
        }
    }

    private void HostHandlePickup(NetPlayer player, NetTeam flagOwner)
    {
        if (flagOwner == NetTeam.None || player.Team == NetTeam.None) return;
        if (player.Team == flagOwner) return;                        // 같은 팀 깃발은 못 가져감
        if (decided[(int)flagOwner] != 0) return;                    // 이미 누가 들고 있음
        if (CarriedFlagOf(player.SessionId) != NetTeam.None || IsDecidedCarrier(player.SessionId)) return;   // 한 명이 깃발 2개는 불가

        decided[(int)flagOwner] = player.SessionId;
        HostBroadcast(EvCarry, w => { w.WriteByte((byte)flagOwner); w.WriteUInt(player.SessionId); });
        HostAlert(flagOwner, MsgStolen);
        HostAlert(Opposite(flagOwner), MsgTaken);
    }

    private void HostHandleScore(NetPlayer player, NetTeam goalTeam)
    {
        if (player.Team != goalTeam) return;
        NetTeam enemyFlag = Opposite(goalTeam);
        if (decided[(int)enemyFlag] != player.SessionId) return;      // 적 깃발을 든 사람만 득점

        decided[(int)enemyFlag] = 0;
        HostBroadcast(EvCarry, w => { w.WriteByte((byte)enemyFlag); w.WriteUInt(0); });
        if (State != null) State.HostAddScore(goalTeam, 1);
        HostAlert(goalTeam, MsgScored);
        HostAlert(enemyFlag, MsgEnemyScored);
    }

    private bool IsDecidedCarrier(uint session)
    {
        return decided[(int)NetTeam.Red] == session || decided[(int)NetTeam.Blue] == session;
    }

    private void HostReturnFlag(NetTeam flagOwner)
    {
        decided[(int)flagOwner] = 0;
        HostBroadcast(EvCarry, w => { w.WriteByte((byte)flagOwner); w.WriteUInt(0); });
        HostAlert(NetTeam.None, MsgReturned);
    }

    private void HostAlert(NetTeam target, byte messageId)
    {
        HostBroadcast(EvAlert, w => { w.WriteByte((byte)target); w.WriteByte(messageId); });
    }

    protected override void OnHostUpdate()
    {
        // (1) 운반자가 죽었거나 이탈했으면 깃발을 제자리로
        for (int t = 1; t <= 2; t++)
        {
            uint session = decided[t];
            if (session == 0) continue;
            var p = Net.GetPlayer(session);
            bool gone = p == null || p.IsForfeited;
            bool dead = State != null && State.IsDead(session);
            if (gone || dead) HostReturnFlag((NetTeam)t);
        }

        // (2) 목표 점수 도달 → 종료 (점수는 한 번에 1씩만 오르므로 동점 도달은 없음)
        int goal = CaptureGoal;
        if (goal > 0)
        {
            int red = ScoreOf(NetTeam.Red), blue = ScoreOf(NetTeam.Blue);
            if ((red >= goal || blue >= goal) && red != blue)
                TryEndMatch(red > blue ? NetTeam.Red : NetTeam.Blue);
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 모든 기기: 이벤트 반영

    protected override void OnModeEvent(byte kind, NetReader r, uint sender)
    {
        switch (kind)
        {
            case EvCarry:
            {
                var owner = (NetTeam)r.ReadByte();
                uint session = r.ReadUInt();
                ApplyCarrier(owner, session);
                break;
            }
            case EvAlert:
            {
                var target = (NetTeam)r.ReadByte();
                byte id = r.ReadByte();
                var local = Net.LocalPlayer;
                if (target == NetTeam.None || (local != null && local.Team == target))
                    RaiseAlert(MessageOf(id));
                break;
            }
        }
    }

    private string MessageOf(byte id)
    {
        switch (id)
        {
            case MsgStolen: return msgFlagStolen;
            case MsgTaken: return msgFlagTaken;
            case MsgScored: return msgScored;
            case MsgEnemyScored: return msgEnemyScored;
            case MsgReturned: return msgFlagReturned;
            default: return string.Empty;
        }
    }

    private void ApplyCarrier(NetTeam owner, uint session)
    {
        if (owner != NetTeam.Red && owner != NetTeam.Blue) return;
        int idx = (int)owner;

        uint old = carrier[idx];
        carrier[idx] = session;
        decided[idx] = session;                  // 호스트가 바뀌어도 이어갈 수 있게 모든 기기가 같이 기록

        if (old != 0 && old != session) SetVisual(old, owner, false);
        if (session != 0) SetVisual(session, owner, true);
        RaiseModeChanged();
    }

    private void SetVisual(uint session, NetTeam flagOwner, bool active)
    {
        var entity = PlayerEntity.Get(session);
        if (entity == null) return;
        var visual = entity.GetComponent<FlagCarrierVisual>();
        if (visual == null) return;
        if (active) visual.SetCarry(flagOwner, true);
        else visual.Clear();
    }

    private void RefreshVisual(PlayerEntity entity)
    {
        if (entity == null) return;
        var visual = entity.GetComponent<FlagCarrierVisual>();
        if (visual == null) return;

        NetTeam carried = CarriedFlagOf(entity.SessionId);
        if (carried == NetTeam.None) visual.Clear();
        else visual.SetCarry(carried, true);
    }

    private void RefreshAllVisuals()
    {
        var list = PlayerEntity.All;
        for (int i = 0; i < list.Count; i++) RefreshVisual(list[i]);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재접속 복구 / 호스트 교체

    // v1: [레드 깃발 운반자 uint][블루 깃발 운반자 uint]
    protected override void WriteModeState(NetWriter writer)
    {
        writer.WriteUInt(decided[(int)NetTeam.Red]);
        writer.WriteUInt(decided[(int)NetTeam.Blue]);
    }

    protected override void ReadModeState(NetReader reader)
    {
        uint red = reader.ReadUInt();
        uint blue = reader.ReadUInt();
        carrier[(int)NetTeam.Red] = decided[(int)NetTeam.Red] = red;
        carrier[(int)NetTeam.Blue] = decided[(int)NetTeam.Blue] = blue;
        RefreshAllVisuals();
    }

    #endregion
}
