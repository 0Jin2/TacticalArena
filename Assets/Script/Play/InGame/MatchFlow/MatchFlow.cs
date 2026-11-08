using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
// MatchFlow.cs : 한 판의 "진행 순서"를 관리하는 지휘자
//
//   Loading → Intro → CharSelect → Countdown → Playing → Result
//   (로딩)   (인트로) (캐릭터선택) (5초 카운트) (본 게임)  (결과)
//
//   Countdown 단계 안의 순서
//     1) 5초 카운트다운 (게임 시작 대기 패널)
//     2) 카운트다운이 끝나고 패널이 닫히면 BeginSpawnStage() → 캐릭터 생성 + 카메라 줌인 (OnSpawnStage)
//     3) 각자 줌인이 끝나면 ZoomReady 신호 → 호스트가 전원 확인 (최대 ModeConfig.zoomWaitTimeout 초)
//     4) 호스트가 Playing 으로 → 본 게임 타이머 시작 + 조작 UI 켜짐
//
// 하는 일
//   1) 모든 유저의 씬 로딩이 끝나길 기다린다.               (호스트만 판단)
//   2) 시간이 다 되면 다음 단계로 넘어가라고 모두에게 알린다. (호스트만 판단)
//   3) 각 유저의 화면/스크립트는 OnPhaseChanged 이벤트를 보고 알아서 반응한다.
//   4) 캐릭터 선택 취합, 시간 내 못 고르면 자동 선택.
//   5) 재접속한 유저에게 "지금 몇 단계, 몇 초 남음"을 복구해 준다.
//
// 하지 않는 일: 점수 계산/승패 판정(→ 모드 스크립트), 결과 서버 전송(→ ResultReporter), 화면 그리기(→ UI)
// 배치: 플레이 씬(TDM/BOMB/FLAG/FALL)마다 빈 오브젝트 "GameRoot"에 붙이고 ModeConfig 에셋을 끼운다.
// ─────────────────────────────────────────────────────────────────────────────

public enum MatchPhase : byte { Loading = 0, Intro = 1, CharSelect = 2, Countdown = 3, Playing = 4, Result = 5 }

public class MatchFlow : MonoBehaviour, INetStateProvider
{
    public static MatchFlow Instance { get; private set; }

    [Header("설정")]
    [SerializeField] private ModeConfig config;
    [Tooltip("로비를 거치지 않고 이 씬만 실행했을 때 1인 테스트로 자동 시작")]
    [SerializeField] private bool allowOfflineTest = true;
    [SerializeField] private bool showDebugOverlay;

    private const float PickGraceSeconds = 1.5f;      // 선택 시간이 끝난 뒤 다른 유저의 선택 패킷을 기다려 주는 시간
    private const float ResendIntervalSeconds = 1f;   // 호스트가 같은 단계 전환을 재전송하는 최소 간격

    // ── 다른 스크립트가 읽는 값 ──────────────────────────────────────────────
    public ModeConfig Config { get { return config; } }
    public MatchPhase Phase { get; private set; }
    public float PhaseDuration { get; private set; }
    public NetTeam Winner { get; private set; }                       // Result 단계에서 유효 (None = 무승부)
    public bool IsPlaying { get { return Phase == MatchPhase.Playing; } }
    public bool HasTimeLimit { get { return PhaseDuration > 0f; } }
    public float RemainingSeconds { get { return Mathf.Max(0f, (float)(phaseEndAt - NetClock.Now)); } }
    /// <summary>Countdown 단계의 5초 카운트다운이 끝났는가</summary>
    public bool CountdownFinished { get { return Phase == MatchPhase.Countdown && PhaseDuration > 0f && NetClock.Now >= phaseEndAt; } }
    /// <summary>캐릭터 생성 + 줌인 구간이 시작됐는가 (Countdown 단계 안)</summary>
    public bool SpawnStageStarted { get { return spawnStageStarted; } }

    // ── 이벤트 (UI/모드 스크립트가 구독) ─────────────────────────────────────
    public event Action<MatchPhase, bool> OnPhaseChanged;   // (새 단계, 재접속 복구로 바뀐 것인가)
    public event Action OnTimeUp;                           // 호스트에서만: 본 게임 시간 종료. 모드가 EndMatch()를 호출해야 함
    public event Action<NetPlayer> OnCharacterPicked;       // 누군가 캐릭터를 골랐다/바꿨다
    public event Action OnMatchFinished;                    // 결과 화면 시간 종료 (→ 로비 복귀 시점, ResultReporter가 처리)
    public event Action OnSpawnStage;                       // 카운트다운이 끝나 캐릭터를 만들고 줌인할 시점 (PlayerSpawner 가 구독)

    /// <summary>시간 안에 못 고른 내 캐릭터를 정할 때 호출됨. 지정 안 하면 ModeConfig.defaultCharacterId 사용.</summary>
    public Func<string> autoPickProvider;

    // ── 내부 상태 ────────────────────────────────────────────────────────────
    private InGameNet net;
    private ushort serial;                       // 단계 전환 번호. 같거나 작은 번호는 무시(중복/역순 방지)
    private double phaseEndAt;                   // NetClock 기준 현재 단계 종료 시각
    private double loadStartAt;
    private float lastSendAt = -100f;
    private bool started;
    private bool waitingForRestore;              // 앱 재시작 복귀: 스냅샷을 받기 전에는 호스트 판단 금지
    private bool timeUpRaised, finished, autoPicked;
    private bool spawnStageStarted;              // Countdown 단계에서 캐릭터 생성/줌인 구간이 시작됐는가
    private double spawnStageAt;
    private bool zoomReportSent;
    private const float SpawnStageAutoSeconds = 3f;   // 카운트다운이 끝난 뒤 UI 가 시작하지 않아도 이 시간 뒤엔 자동으로 시작
    private readonly HashSet<uint> zoomReady = new HashSet<uint>();
    private readonly HashSet<uint> readySessions = new HashSet<uint>();
    private readonly HashSet<uint> confirmed = new HashSet<uint>();

    public ushort StateId { get { return NetStateIds.MatchFlow; } }

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        if (config == null)
        {
            NetLog.Error("MatchFlow: ModeConfig가 비어 있습니다. 기본값으로 진행합니다.");
            config = ScriptableObject.CreateInstance<ModeConfig>();
        }

        net = InGameNet.Ensure();
        if (!net.HasActiveMatch)
        {
            if (!allowOfflineTest) { NetLog.Error("MatchFlow: 진행 중인 매치 정보가 없습니다."); enabled = false; return; }
            net.PrepareOffline(BACKEND_MATCH.TacticalMatchMode.CLASSIC, SceneManager.GetActiveScene().name, "Tester");
        }

        waitingForRestore = net.IsResumedMatch;
        net.Connection.ApplySettings(config.reconnectTimeLimit, config.hostLossPolicy, config.minPlayersPerTeam);
        Phase = MatchPhase.Loading;
    }

    private void OnEnable()
    {
        if (net == null) return;
        net.Subscribe(NetPacketType.SceneReady, HandleSceneReady);
        net.Subscribe(NetPacketType.PhaseChange, HandlePhaseChange);
        net.Subscribe(NetPacketType.ZoomReady, HandleZoomReady);
        net.Subscribe(NetPacketType.CharacterSelect, HandleCharacterSelect);
        net.Subscribe(NetPacketType.CharacterReady, HandleCharacterReady);
        net.RegisterStateProvider(this);
        net.Connection.OnMatchAborted += HandleAborted;
    }

    private void OnDisable()
    {
        if (net == null) return;
        net.Unsubscribe(NetPacketType.SceneReady, HandleSceneReady);
        net.Unsubscribe(NetPacketType.PhaseChange, HandlePhaseChange);
        net.Unsubscribe(NetPacketType.ZoomReady, HandleZoomReady);
        net.Unsubscribe(NetPacketType.CharacterSelect, HandleCharacterSelect);
        net.Unsubscribe(NetPacketType.CharacterReady, HandleCharacterReady);
        net.UnregisterStateProvider(this);
        if (net.Connection != null) net.Connection.OnMatchAborted -= HandleAborted;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private IEnumerator Start()
    {
        // 한 프레임 기다린다: 같은 씬의 다른 스크립트들이 Start에서 Subscribe를 끝낼 시간을 준다.
        yield return null;

        net.SetSceneReady();                       // 그동안 보관된 패킷이 이때 한꺼번에 전달된다
        loadStartAt = NetClock.Now;
        started = true;
        if (!waitingForRestore) net.Send(NetPacketType.SceneReady, null, true);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 매 프레임: 호스트의 단계 진행 판단

    private void Update()
    {
        if (!started) return;

        switch (Phase)
        {
            case MatchPhase.Loading:
                UpdateLoading();
                break;

            case MatchPhase.Intro:
                if (Expired && CanAct()) HostSend(Phase + 1, DurationOf(Phase + 1), NetTeam.None, false);
                break;

            case MatchPhase.Countdown:
                UpdateCountdown();
                break;

            case MatchPhase.CharSelect:
                UpdateCharSelect();
                break;

            case MatchPhase.Playing:
                if (HasTimeLimit && !timeUpRaised && Expired && CanAct())
                {
                    timeUpRaised = true;
                    if (OnTimeUp != null) OnTimeUp();          // 모드가 승자를 정해 EndMatch 호출
                    else EndMatch(NetTeam.None);               // 구독자가 없으면 무승부 처리
                }
                break;

            case MatchPhase.Result:
                if (!finished && Expired) { finished = true; if (OnMatchFinished != null) OnMatchFinished(); }
                break;
        }
    }

    private bool Expired { get { return PhaseDuration > 0f && NetClock.Now >= phaseEndAt; } }

    /// <summary>"지금 내가 단계 전환을 결정해도 되는가": 호스트 + 연결 정상 + 복구 대기 아님</summary>
    private bool CanAct()
    {
        return !waitingForRestore && net.Connection.IsLocalPlayable && net.IsHost;
    }

    private void UpdateLoading()
    {
        if (waitingForRestore) return;
        bool timeout = NetClock.Now - loadStartAt > config.sceneLoadTimeout;
        if (!timeout && !AllReady()) return;
        if (CanAct()) HostSend(MatchPhase.Intro, config.introSeconds, NetTeam.None, false);
    }

    private void UpdateCharSelect()
    {
        // 1) 내 캐릭터를 시간 안에 못 골랐으면 자동 선택 (각자 자기 것만)
        if (!autoPicked && Expired)
        {
            autoPicked = true;
            AutoPickForLocal();
        }

        // 2) 호스트: 모두 확정했거나, 시간 종료 후 유예 시간이 지나면 카운트다운으로
        bool graceOver = HasTimeLimit && NetClock.Now >= phaseEndAt + PickGraceSeconds;
        if (!graceOver && !AllConfirmed()) return;
        if (CanAct()) HostSend(MatchPhase.Countdown, config.countdownSeconds, NetTeam.None, false);
    }

    /// <summary>
    /// Countdown 단계: 5초가 끝나면 캐릭터 생성/줌인 구간으로 넘어가고(UI 가 BeginSpawnStage 호출),
    /// 호스트는 모두의 줌인 완료를 확인한 뒤 Playing 으로 보낸다.
    /// </summary>
    private void UpdateCountdown()
    {
        if (!CountdownFinished) return;

        if (!spawnStageStarted)
        {
            // 보통은 게임 시작 대기 패널이 닫힌 뒤 CharSelectUI 가 BeginSpawnStage 를 부른다. UI 가 없어도 멈추지 않게 자동 시작.
            if (NetClock.Now >= phaseEndAt + SpawnStageAutoSeconds) BeginSpawnStage();
            return;
        }

        bool waitOver = NetClock.Now - spawnStageAt > config.zoomWaitTimeout;      // 오래 안 오는 유저는 기다리지 않는다
        if ((AllZoomReady() || waitOver) && CanAct())
            HostSend(MatchPhase.Playing, DurationOf(MatchPhase.Playing), NetTeam.None, false);
    }

    /// <summary>[CharSelectUI] 카운트다운이 끝나고 패널이 닫혔다 → 캐릭터 생성 + 카메라 줌인 시작. (한 번만)</summary>
    public void BeginSpawnStage()
    {
        if (Phase != MatchPhase.Countdown || spawnStageStarted) return;
        spawnStageStarted = true;
        spawnStageAt = NetClock.Now;
        if (OnSpawnStage != null) OnSpawnStage();
    }

    private bool AllZoomReady()
    {
        foreach (var p in net.Players)
        {
            if (!p.IsOnline || p.IsForfeited) continue;       // 끊긴 유저는 기다리지 않는다
            if (!zoomReady.Contains(p.SessionId)) return false;
        }
        return true;
    }

    /// <summary>[PlayerSpawner] 내 카메라 줌인이 끝났다고 모두에게 알린다. (Countdown 단계에서 한 번만)</summary>
    public void ReportZoomReady()
    {
        if (zoomReportSent || Phase != MatchPhase.Countdown) return;
        zoomReportSent = true;
        net.Send(NetPacketType.ZoomReady, null, true);
    }

    private bool AllReady()
    {
        foreach (var p in net.Players)
        {
            if (!p.IsOnline || p.IsForfeited) continue;       // 끊긴 유저는 기다리지 않는다
            if (!readySessions.Contains(p.SessionId)) return false;
        }
        return true;
    }

    private bool AllConfirmed()
    {
        foreach (var p in net.Players)
        {
            if (!p.IsOnline || p.IsForfeited) continue;
            if (!confirmed.Contains(p.SessionId)) return false;
        }
        return true;
    }

    private float DurationOf(MatchPhase phase)
    {
        switch (phase)
        {
            case MatchPhase.Intro: return config.introSeconds;
            case MatchPhase.CharSelect: return config.characterSelectSeconds;
            case MatchPhase.Countdown: return config.countdownSeconds;
            case MatchPhase.Playing: return config.matchSeconds;
            case MatchPhase.Result: return config.resultSeconds;
            default: return 0f;
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 보내기

    /// <summary>
    /// 호스트가 "다음 단계로!"를 모두에게 보낸다. (나도 서버 에코로 똑같이 받아서 처리 → 모두 같은 경로)
    /// 패킷: [번호 ushort][단계 byte][시간 float][승리팀 byte][확정 캐릭터 수 byte]{[세션 uint][캐릭터 string]}
    /// </summary>
    private void HostSend(MatchPhase next, float duration, NetTeam winner, bool force)
    {
        if (!force && Time.unscaledTime - lastSendAt < ResendIntervalSeconds) return;   // 에코 대기 중 연타 방지
        lastSendAt = Time.unscaledTime;

        // Playing / CharSelect 는 0(무제한)을 허용한다. (BOMB 본 게임, 튜토리얼 캐릭터 선택)
        if (next != MatchPhase.Playing && next != MatchPhase.CharSelect) duration = Mathf.Max(0.1f, duration);
        ushort nextSerial = (ushort)(serial + 1);

        net.Send(NetPacketType.PhaseChange, w =>
        {
            w.WriteUShort(nextSerial);
            w.WriteByte((byte)next);
            w.WriteFloat(duration);
            w.WriteByte((byte)winner);

            if (next == MatchPhase.Countdown)
            {
                // 캐릭터 최종 확정 목록: 못 고른 사람은 기본 캐릭터로 채워서 모두가 똑같이 알게 한다.
                var players = net.Players;
                w.WriteByte((byte)players.Count);
                for (int i = 0; i < players.Count; i++)
                {
                    string id = string.IsNullOrEmpty(players[i].CharacterId) ? config.defaultCharacterId : players[i].CharacterId;
                    w.WriteUInt(players[i].SessionId);
                    w.WriteString(id ?? string.Empty);
                }
            }
            else w.WriteByte(0);
        }, true);
    }

    /// <summary>[호스트만] 본 게임을 끝내고 결과 단계로. 모드 스크립트가 승패가 정해졌을 때 호출한다. (None = 무승부)</summary>
    public void EndMatch(NetTeam winner)
    {
        if (Phase != MatchPhase.Playing || !CanAct()) return;
        HostSend(MatchPhase.Result, config.resultSeconds, winner, true);
    }

    /// <summary>[캐릭터 선택 UI] 캐릭터를 고른다. 확정 전에는 몇 번이든 바꿀 수 있다.</summary>
    public void SelectCharacter(string characterId)
    {
        var me = net.LocalPlayer;
        if (Phase != MatchPhase.CharSelect || me == null || string.IsNullOrEmpty(characterId)) return;
        if (confirmed.Contains(me.SessionId)) return;           // 준비 중에는 변경 불가
        if (HasTimeLimit && Expired) return;
        SendSelect(characterId);
    }

    /// <summary>
    /// [캐릭터 선택 UI] 준비 완료/해제. 준비 중에는 캐릭터를 바꿀 수 없다.
    /// 전원이 준비하면 남은 시간을 기다리지 않고 바로 다음 단계로 간다.
    /// </summary>
    public void SetReady(bool ready)
    {
        var me = net.LocalPlayer;
        if (Phase != MatchPhase.CharSelect || me == null || string.IsNullOrEmpty(me.CharacterId)) return;
        if (HasTimeLimit && Expired) return;                    // 시간이 끝나면 더 못 바꾼다
        SendReady(ready);
    }

    public bool IsReady(uint sessionId) { return confirmed.Contains(sessionId); }
    public bool IsConfirmed(uint sessionId) { return confirmed.Contains(sessionId); }

    private void SendSelect(string characterId)
    {
        net.Send(NetPacketType.CharacterSelect, w => w.WriteString(characterId), true);
    }

    private void SendReady(bool ready)
    {
        net.Send(NetPacketType.CharacterReady, w => w.WriteBool(ready), true);
    }

    private void AutoPickForLocal()
    {
        var me = net.LocalPlayer;
        if (me == null || confirmed.Contains(me.SessionId)) return;

        string id = me.CharacterId;
        if (string.IsNullOrEmpty(id) && autoPickProvider != null) id = autoPickProvider();
        if (string.IsNullOrEmpty(id)) id = config.defaultCharacterId;
        if (string.IsNullOrEmpty(id)) { NetLog.Warn("MatchFlow: 자동 선택할 캐릭터가 없습니다. (ModeConfig.defaultCharacterId 확인)"); return; }

        SendSelect(id);
        SendReady(true);     // 선택 → 준비 순서는 같은 유저의 패킷이라 뒤바뀌지 않는다
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 받기

    private void HandleSceneReady(NetPacket packet)
    {
        readySessions.Add(packet.Sender);
    }

    private void HandleZoomReady(NetPacket packet)
    {
        zoomReady.Add(packet.Sender);
    }

    private void HandleCharacterSelect(NetPacket packet)
    {
        var player = net.GetPlayer(packet.Sender);
        if (player == null || confirmed.Contains(packet.Sender)) return;     // 확정한 유저의 변경은 무시

        string id = packet.OpenReader().ReadString();
        if (string.IsNullOrEmpty(id)) return;
        player.CharacterId = id;
        if (OnCharacterPicked != null) OnCharacterPicked(player);
    }

    private void HandleCharacterReady(NetPacket packet)
    {
        var player = net.GetPlayer(packet.Sender);
        if (player == null || string.IsNullOrEmpty(player.CharacterId)) return;
        bool ready = packet.PayloadLength == 0 || packet.OpenReader().ReadBool();
        bool changed = ready ? confirmed.Add(packet.Sender) : confirmed.Remove(packet.Sender);
        if (changed && OnCharacterPicked != null) OnCharacterPicked(player);
    }

    private void HandlePhaseChange(NetPacket packet)
    {
        if (Phase == MatchPhase.Result) return;                 // 결과 단계 이후에는 더 이상 바뀌지 않는다

        var reader = packet.OpenReader();
        ushort number = reader.ReadUShort();
        var next = (MatchPhase)reader.ReadByte();
        float duration = reader.ReadFloat();
        var winner = (NetTeam)reader.ReadByte();
        int pickCount = reader.ReadByte();

        if (number <= serial) return;                           // 이미 처리한 번호
        serial = number;

        for (int i = 0; i < pickCount; i++)
        {
            uint session = reader.ReadUInt();
            string characterId = reader.ReadString();
            var player = net.GetPlayer(session);
            if (player != null && !string.IsNullOrEmpty(characterId)) player.CharacterId = characterId;
        }

        EnterPhase(next, duration, duration, winner, false);
    }

    /// <summary>NetConnectionManager가 "상대가 모두 나갔다" 등으로 매치를 중단시켰을 때: 각자 바로 결과 단계로.</summary>
    private void HandleAborted(NetTeam winner, MatchAbortReason reason)
    {
        if (Phase == MatchPhase.Result) return;
        EnterPhase(MatchPhase.Result, config.resultSeconds, config.resultSeconds, winner, false);
    }

    private void EnterPhase(MatchPhase phase, float duration, float remaining, NetTeam winner, bool restored)
    {
        Phase = phase;
        PhaseDuration = duration;
        phaseEndAt = NetClock.Now + remaining;
        Winner = winner;

        timeUpRaised = false;
        finished = false;
        autoPicked = false;
        if (phase == MatchPhase.CharSelect) zoomReady.Clear();
        zoomReportSent = false;
        spawnStageStarted = false;
        if (phase == MatchPhase.CharSelect && !restored) confirmed.Clear();

        NetLog.Info("단계 전환: " + phase + " (" + duration + "s, 복구=" + restored + ")");
        if (OnPhaseChanged != null) OnPhaseChanged(phase, restored);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재접속 복구 (INetStateProvider)

    // 형식 v1: [버전][단계][번호][남은시간][전체시간][승리팀][확정 인원 수]{[세션]}
    public void WriteState(NetWriter writer)
    {
        writer.WriteByte(1);
        writer.WriteByte((byte)Phase);
        writer.WriteUShort(serial);
        writer.WriteFloat(RemainingSeconds);        // 절대 시각이 아니라 "남은 시간"으로 보낸다
        writer.WriteFloat(PhaseDuration);
        writer.WriteByte((byte)Winner);
        writer.WriteUShort((ushort)confirmed.Count);
        foreach (uint session in confirmed) writer.WriteUInt(session);
    }

    public void ReadState(NetReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 1) { NetLog.Warn("MatchFlow: 알 수 없는 스냅샷 버전 " + version); return; }

        var phase = (MatchPhase)reader.ReadByte();
        ushort number = reader.ReadUShort();
        float remaining = reader.ReadFloat();
        float duration = reader.ReadFloat();
        var winner = (NetTeam)reader.ReadByte();
        int count = reader.ReadUShort();

        confirmed.Clear();
        for (int i = 0; i < count; i++) confirmed.Add(reader.ReadUInt());

        serial = number;
        waitingForRestore = false;
        EnterPhase(phase, duration, remaining, winner, true);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 디버그 (혼자 테스트할 때)

    [ContextMenu("Debug/다음 단계로 (호스트)")]
    private void DebugNextPhase()
    {
        if (!Application.isPlaying || !started) return;
        if (Phase == MatchPhase.Playing) EndMatch(NetTeam.None);
        else if (Phase < MatchPhase.Playing) HostSend(Phase + 1, DurationOf(Phase + 1), NetTeam.None, true);
    }

    private void OnGUI()
    {
        if (!showDebugOverlay || net == null) return;
        GUI.Box(new Rect(10, 10, 260, 92), "MatchFlow");
        GUI.Label(new Rect(20, 32, 240, 20), "단계: " + Phase + "  남은: " + RemainingSeconds.ToString("F1") + "s");
        GUI.Label(new Rect(20, 52, 240, 20), "호스트: " + net.IsHost + "  연결: " + net.Connection.State);
        GUI.Label(new Rect(20, 72, 240, 20), "로딩완료: " + readySessions.Count + "/" + net.Players.Count + "  확정: " + confirmed.Count);
    }

    #endregion
}
