using System;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// GameModeBase : 모든 게임 모드(TDM / FLAG / BOMB ...)의 공통 부모  (기존 ModeHandlerBase 대체)
//
//  포톤: 모드 핸들러는 "서버(방장)의 네트워크 오브젝트"였고 [Networked] 변수와 RPC 로 상태를 퍼뜨렸다.
//  뒤끝: 모드 스크립트는 씬에 있는 평범한 컴포넌트이고, 판정은 "호스트(슈퍼 게이머)"만 한다.
//
//   ┌ 호스트 : 점수/조건을 보고 결과를 "결정" → ModeEvent 패킷으로 모두에게 알린다 (HostBroadcast)
//   └ 그 외  : 호스트에게 "요청" 만 보낸다 (ModeRequest) / 받은 ModeEvent 를 그대로 화면에 반영한다
//
//  호스트가 바뀌어도 "씬에 있는 상태 + 스냅샷(재접속)" 으로 새 호스트가 이어받을 수 있게
//  모든 판정은 호스트 여부를 매번 확인하는 방식(IsHostNow)으로 만든다.
//
//  배치: GameRoot (MatchFlow 와 같은 오브젝트). 한 씬에 모드 스크립트는 1개만 둔다.
// ─────────────────────────────────────────────────────────────────────────────

public abstract class GameModeBase : MonoBehaviour, INetStateProvider
{
    public static GameModeBase Current { get; private set; }

    protected InGameNet Net { get; private set; }
    protected MatchFlow Flow { get; private set; }
    protected MatchState State { get; private set; }
    protected PlayerSpawner Spawner { get { return PlayerSpawner.Instance; } }
    protected ModeConfig Config { get { return Flow != null ? Flow.Config : null; } }

    // ── 화면(UI)이 읽는 값 ──────────────────────────────────────────────────
    public GameModeType ModeType { get { return Config != null ? Config.modeType : GameModeType.TDM; } }

    /// <summary>라운드 중 부활을 허용하는가 (BOMB 은 false)</summary>
    public virtual bool AllowRespawn { get { return Config == null || Config.respawnEnabled; } }

    /// <summary>화면 위쪽 타이머에 보여 줄 남은 시간(초). BOMB 은 라운드 시간으로 바꾼다.</summary>
    public virtual float DisplaySeconds { get { return Flow != null && Flow.HasTimeLimit ? Flow.RemainingSeconds : 0f; } }

    /// <summary>타이머를 보여 줄 필요가 있는가 (시간 제한이 없으면 숨김)</summary>
    public virtual bool ShowTimer { get { return Flow != null && Flow.HasTimeLimit; } }

    /// <summary>목표 안내 문구. 예: "목표 처치 : 30킬"</summary>
    public virtual string GoalText { get { return string.Empty; } }

    /// <summary>"라운드 2 / 5" 처럼 보여 줄 문구. 없으면 빈 문자열 (BOMB 만 사용)</summary>
    public virtual string RoundText { get { return string.Empty; } }

    /// <summary>HUD 에 어떤 요소를 보여 줄지 (기존 InitTDMUI / InitFlagUI / InitBombUI 의 차이)</summary>
    public virtual bool ShowScoreUI { get { return true; } }
    public virtual bool ShowGoalUI { get { return !string.IsNullOrEmpty(GoalText); } }
    public virtual bool ShowRoundUI { get { return !string.IsNullOrEmpty(RoundText); } }

    /// <summary>점수판에 보여 줄 점수 (기본: MatchState 의 팀 점수)</summary>
    public virtual int ScoreOf(NetTeam team) { return State != null ? State.GetScore(team) : 0; }

    /// <summary>모드가 화면에 알리고 싶은 짧은 안내 ("깃발을 획득했습니다!" 등)</summary>
    public event Action<string> OnAlert;
    /// <summary>모드 상태가 바뀌어 HUD 를 다시 그려야 할 때</summary>
    public event Action OnModeChanged;

    // ── 호스트 판정 보조 ────────────────────────────────────────────────────
    /// <summary>"지금 내가 판정해도 되는 호스트인가" : 호스트 + 연결 정상</summary>
    protected bool IsHostNow
    {
        get { return Net != null && Net.IsHost && Net.Connection != null && Net.Connection.IsLocalPlayable; }
    }

    protected bool IsPlaying { get { return Flow != null && Flow.Phase == MatchPhase.Playing; } }

    private float lastEndTryAt = -100f;
    private bool subscribed;

    public ushort StateId { get { return NetStateIds.Mode; } }

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    protected virtual void Awake()
    {
        if (Current != null && Current != this)
        {
            NetLog.Warn("GameModeBase: 씬에 모드 스크립트가 2개 이상입니다. 하나는 제거하세요. (" + name + ")");
            enabled = false;
            return;
        }
        Current = this;
    }

    protected virtual void OnEnable()
    {
        if (Current != this) return;
        Net = InGameNet.Ensure();
        Net.RegisterStateProvider(this);
        Net.Subscribe(NetPacketType.ModeEvent, HandleModeEventPacket);
        Net.Subscribe(NetPacketType.ModeRequest, HandleModeRequestPacket);
        Net.Connection.OnHostChanged += HandleHostChanged;
        subscribed = true;
    }

    protected virtual void Start()
    {
        if (Current != this) return;
        Flow = MatchFlow.Instance;
        State = MatchState.Instance;
        if (Flow == null) { NetLog.Error("GameModeBase: MatchFlow 가 씬에 없습니다."); return; }
        if (State == null) NetLog.Warn("GameModeBase: MatchState 가 씬에 없습니다. 점수/킬 기록이 되지 않습니다.");

        if (State != null) State.SetScoreRule(ScoreRule);

        Flow.OnPhaseChanged += HandlePhaseChanged;
        Flow.OnTimeUp += HandleTimeUp;

        if (Flow.Phase == MatchPhase.Playing) OnPlayingStarted(true);
    }

    protected virtual void OnDisable()
    {
        if (!subscribed) return;
        subscribed = false;
        if (Net != null)
        {
            Net.UnregisterStateProvider(this);
            Net.Unsubscribe(NetPacketType.ModeEvent, HandleModeEventPacket);
            Net.Unsubscribe(NetPacketType.ModeRequest, HandleModeRequestPacket);
            if (Net.Connection != null) Net.Connection.OnHostChanged -= HandleHostChanged;
        }
        if (Flow != null)
        {
            Flow.OnPhaseChanged -= HandlePhaseChanged;
            Flow.OnTimeUp -= HandleTimeUp;
        }
    }

    protected virtual void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    private void Update()
    {
        if (Flow == null) return;
        if (Flow.Phase == MatchPhase.Playing)
        {
            OnPlayingUpdate();
            if (IsHostNow) OnHostUpdate();
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 자식이 오버라이드하는 훅

    /// <summary>이 모드가 쓰는 점수 규칙 (KillBased = 킬 1회가 팀 1점 / Manual = 모드가 직접 AddScore)</summary>
    protected virtual MatchState.ScoreRule ScoreRule { get { return MatchState.ScoreRule.KillBased; } }

    /// <summary>본 게임이 시작됐을 때 (모든 기기). restored=true 면 재접속/중간 시작 복구</summary>
    protected virtual void OnPlayingStarted(bool restored) { }

    /// <summary>결과 단계로 넘어갔을 때 (모든 기기)</summary>
    protected virtual void OnResultStarted() { }

    /// <summary>본 게임 중 매 프레임 (모든 기기) - 연출/표시 갱신용</summary>
    protected virtual void OnPlayingUpdate() { }

    /// <summary>본 게임 중 매 프레임 (호스트만) - 승패/조건 판정용</summary>
    protected virtual void OnHostUpdate() { }

    /// <summary>[호스트] 시간이 다 됐을 때 승자를 정해 TryEndMatch 를 호출한다. 기본: 점수 높은 팀, 같으면 무승부</summary>
    protected virtual void OnTimeUpHost()
    {
        TryEndMatch(WinnerByScore());
    }

    /// <summary>호스트가 바뀌었을 때 (내가 새 호스트면 isMe=true). 진행 중이던 호스트 작업을 이어받는 곳</summary>
    protected virtual void OnHostChangedHook(bool isMe) { }

    /// <summary>호스트가 보낸 ModeEvent 를 받았을 때 (모든 기기, 호스트 자신 포함)</summary>
    protected virtual void OnModeEvent(byte kind, NetReader reader, uint sender) { }

    /// <summary>누군가 보낸 ModeRequest 를 호스트가 받았을 때 (호스트만 호출됨)</summary>
    protected virtual void OnModeRequest(byte kind, NetReader reader, uint sender) { }

    /// <summary>재접속자에게 줄 모드 전용 상태를 기록 (호스트가 호출됨)</summary>
    protected virtual void WriteModeState(NetWriter writer) { }

    /// <summary>재접속 때 받은 모드 전용 상태를 읽어 복구</summary>
    protected virtual void ReadModeState(NetReader reader) { }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 자식이 쓰는 도구

    /// <summary>[호스트] 모두에게(나 포함) 모드 사건을 알린다. kind 는 모드가 정한 번호.</summary>
    protected void HostBroadcast(byte kind, Action<NetWriter> body = null, bool critical = true)
    {
        Net.Send(NetPacketType.ModeEvent, w =>
        {
            w.WriteByte(kind);
            if (body != null) body(w);
        }, critical);
    }

    /// <summary>[아무나] 호스트에게 무언가를 요청한다. (호스트가 판정 후 HostBroadcast 로 결과를 알림)</summary>
    protected void SendRequest(byte kind, Action<NetWriter> body = null, bool critical = true)
    {
        Net.Send(NetPacketType.ModeRequest, w =>
        {
            w.WriteByte(kind);
            if (body != null) body(w);
        }, critical);
    }

    /// <summary>[호스트] 경기 종료를 요청한다. 에코가 오기 전 매 프레임 재전송되지 않도록 간격을 둔다.</summary>
    protected void TryEndMatch(NetTeam winner)
    {
        if (!IsHostNow || !IsPlaying) return;
        if (Time.unscaledTime - lastEndTryAt < 1.2f) return;
        lastEndTryAt = Time.unscaledTime;
        Flow.EndMatch(winner);
    }

    protected NetTeam WinnerByScore()
    {
        int red = ScoreOf(NetTeam.Red), blue = ScoreOf(NetTeam.Blue);
        return red > blue ? NetTeam.Red : blue > red ? NetTeam.Blue : NetTeam.None;
    }

    protected void RaiseAlert(string message)
    {
        if (OnAlert != null && !string.IsNullOrEmpty(message)) OnAlert(message);
    }

    protected void RaiseModeChanged()
    {
        if (OnModeChanged != null) OnModeChanged();
    }

    public static NetTeam Opposite(NetTeam team)
    {
        return team == NetTeam.Red ? NetTeam.Blue : team == NetTeam.Blue ? NetTeam.Red : NetTeam.None;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 내부 처리

    private void HandlePhaseChanged(MatchPhase phase, bool restored)
    {
        if (phase == MatchPhase.Playing) OnPlayingStarted(restored);
        else if (phase == MatchPhase.Result) OnResultStarted();
    }

    private void HandleTimeUp()
    {
        // MatchFlow 가 호스트에서만 부른다. 이미 호스트가 아니게 됐다면 무시.
        if (!IsHostNow) return;
        OnTimeUpHost();
    }

    private void HandleHostChanged(bool isMe)
    {
        OnHostChangedHook(isMe);
    }

    private void HandleModeEventPacket(NetPacket packet)
    {
        var reader = packet.OpenReader();
        byte kind = reader.ReadByte();
        OnModeEvent(kind, reader, packet.Sender);
    }

    private void HandleModeRequestPacket(NetPacket packet)
    {
        if (!IsHostNow) return;                     // 호스트만 처리
        var reader = packet.OpenReader();
        byte kind = reader.ReadByte();
        OnModeRequest(kind, reader, packet.Sender);
    }

    // 스냅샷: [버전 byte] + 모드 전용 내용
    public void WriteState(NetWriter writer)
    {
        writer.WriteByte(1);
        WriteModeState(writer);
    }

    public void ReadState(NetReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 1) { NetLog.Warn("GameModeBase: 알 수 없는 스냅샷 버전 " + version); return; }
        ReadModeState(reader);
        RaiseModeChanged();
    }

    #endregion
}
