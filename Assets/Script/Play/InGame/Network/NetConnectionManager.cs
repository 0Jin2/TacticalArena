using BackEnd;
using BackEnd.Tcp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
// NetConnectionManager : 연결 상태 감시 + 재접속 + 이탈/호스트 교체 처리
// (InGameNet과 같은 오브젝트에 자동 부착되어 씬이 바뀌어도 유지된다)
//
// 담당
//  1) 내 연결 끊김 감지     : OnSessionOffline / OnLeaveInGameServer / 주기적 에코 확인(Ping) /
//                             인터넷 상태 변화(Wi-Fi↔LTE, 끊김) / 앱 백그라운드 복귀
//  2) 상대 이탈 감지        : OnSessionOffline(타 유저) → 유예시간 후 이탈 확정(Forfeit)
//  3) 재접속 시도 + 제한 시간 : 벽시계(NetClock) 기준. 앱이 백그라운드에 있던 시간도 포함해서 계산
//  4) 재접속 성공 시 복구   : 호스트에게 스냅샷 요청 → INetStateProvider들에 나눠서 적용 (게임을 처음부터 다시 시작하지 않음)
//  5) 재접속 실패           : OnLocalReconnectFailed 이벤트 (패배/종료 처리는 구독자가 결정)
//  6) 호스트(슈퍼 게이머) 이탈 : 서버가 새 호스트를 지정 → 정책(HostLossPolicy)과 팀 생존 인원으로 지속 여부 결정
//  7) 앱 재시작 후 복귀     : TryResumeActiveMatch (로비에서 호출)
//
// 이 스크립트는 "무엇을 할지"(점수 판정, 결과 화면, 로비 복귀)는 결정하지 않는다.
// 결정이 필요한 순간마다 이벤트를 발생시키고, 구독자(MatchFlow / ResultReporter / UI)가 처리한다.
//
// 뒤끝 문서 기준 주의사항
//  - 통신이 30초 이상 불량이면 서버가 끊는다. SDK도 자동 재접속을 시도하므로 먼저 autoReconnectWait 만큼 기다린다.
//  - 재접속은 IsGameRoomActivate → JoinGameServer(isReconnect: true) 순서이며, JoinGameRoom은 호출하면 안 된다.
//  - 끊긴 상태에서 새 매칭을 신청하면 그 게임은 패배 처리되고 재접속할 수 없다. (재접속 중에는 매칭 버튼을 막을 것)
//  - 다른 유저가 결과를 이미 보내 게임이 끝났다면 재접속할 수 없다. (IsGameRoomActivate가 404)
// ─────────────────────────────────────────────────────────────────────────────

public enum NetConnectionState
{
    Idle,           // 매치 없음
    Connected,      // 정상
    Disconnected,   // 내 연결이 끊김. SDK 자동 재접속 대기 중
    Reconnecting,   // 수동 재접속 시도 중
    Recovering,     // 재접속 성공. 호스트에게서 상태 복구 중
    Failed,         // 재접속 실패 (영구 이탈)
    Ended           // 매치 정상 종료
}

public enum ReconnectFailReason
{
    TimeLimit,            // 제한 시간 초과 → 이탈(패배) 처리 대상
    GameEnded,            // 이미 끝난 게임이거나 재접속 불가 → 로비로 복귀
    SnapshotUnavailable   // 앱 재시작 복귀인데 상태를 받지 못함 → 로비로 복귀
}

public enum MatchAbortReason
{
    OpponentsLeft,   // 한쪽 팀 전원이 이탈
    AllPlayersLeft,  // 양쪽 모두 이탈
    HostLeft         // 호스트 이탈 시 종료 정책(HostLossPolicy.EndMatch)
}

public enum HostLossPolicy
{
    ContinueWithNewHost,   // 서버가 지정한 새 호스트가 이어서 진행 (기본)
    EndMatch               // 호스트가 나가면 즉시 종료
}

public class NetConnectionManager : MonoBehaviour
{
    [Header("재접속 (초)")]
    [Tooltip("재접속 제한 시간. 내 쪽(재접속 시도)과 남의 쪽(이탈 확정 유예) 모두에 쓰인다.")]
    public float reconnectTimeLimit = 30f;
    [Tooltip("끊김 직후 SDK 자동 재접속을 기다리는 시간")]
    public float autoReconnectWait = 6f;
    public float retryInterval = 2f;
    public float joinTimeout = 10f;
    public float snapshotTimeout = 3f;
    public int snapshotRetries = 3;

    [Header("연결 감시")]
    [Tooltip("연결 확인(Ping) 주기. 0이면 끈다. SDK의 30초 감지보다 빨리 알아채기 위함.")]
    public float livenessInterval = 5f;
    public float probeTimeout = 3f;
    [Tooltip("연속 몇 번 응답이 없으면 끊김으로 판단할지 (일시적 프레임 끊김 오판 방지)")]
    public int probeFailThreshold = 2;

    [Header("호스트 이탈 / 인원")]
    public HostLossPolicy hostLossPolicy = HostLossPolicy.ContinueWithNewHost;
    [Tooltip("팀당 최소 생존 인원. 이보다 적어지면 매치를 중단한다.")]
    public int minPlayersPerTeam = 1;

    // ── 이벤트 (구독자가 화면/판정/결과를 처리) ───────────────────────────────
    public event Action<NetConnectionState> OnStateChanged;
    public event Action OnLocalDisconnected;                     // 내 연결 끊김 → 입력 정지 + "재접속 중..." 표시
    public event Action<bool> OnLocalReconnected;                // 재접속 성공. true = 호스트의 상태 스냅샷으로 복구함
    public event Action<ReconnectFailReason> OnLocalReconnectFailed;
    public event Action<NetPlayer> OnRemoteDisconnected;         // 상대 끊김(유예 시작)
    public event Action<NetPlayer> OnRemoteReconnected;          // 상대 재접속 성공
    public event Action<NetPlayer> OnRemoteForfeited;            // 상대 이탈 확정 (캐릭터 제거 등)
    public event Action<bool> OnHostChanged;                     // 인자: 내가 새 호스트인가
    public event Action<NetTeam, MatchAbortReason> OnMatchAborted; // 승리 팀(None = 무승부/없음)

    public static NetConnectionManager Instance
    {
        get { return InGameNet.Instance != null ? InGameNet.Instance.Connection : null; }
    }

    public NetConnectionState State { get { return state; } }
    /// <summary>게임플레이 스크립트가 입력/판정을 멈출지 판단할 때 사용.</summary>
    public bool IsLocalPlayable { get { return state == NetConnectionState.Connected; } }
    public float RemainingReconnectSeconds
    {
        get
        {
            bool waiting = state == NetConnectionState.Disconnected || state == NetConnectionState.Reconnecting || state == NetConnectionState.Recovering;
            return waiting ? Mathf.Max(0f, (float)(deadlineSec - NetClock.Now)) : 0f;
        }
    }

    private InGameNet net;
    private NetConnectionState state = NetConnectionState.Idle;
    private bool backendEventsRegistered;
    private bool matchEnded;
    private bool abortRaised;

    // 재접속
    private Coroutine reconnectRoutine;
    private double deadlineSec;
    private bool leaveEventReceived;
    private bool joinPending;
    private bool joinOk;
    private byte[] snapshotBytes;
    private double lastReconnectFinishedAt;
    private readonly Dictionary<uint, double> lastSnapshotSentAt = new Dictionary<uint, double>();

    // 연결 확인
    private bool probePending;
    private bool probeEchoed;
    private uint probeNonce;
    private double probeSentAt;
    private int probeFailCount;
    private double nextKeepAliveAt;
    private NetworkReachability lastReach;
    private double pausedAtSec;

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명 / 초기화

    private void Awake()
    {
        net = InGameNet.Instance;
        if (net == null) { NetLog.Error("NetConnectionManager는 InGameNet.Ensure()로 생성해야 합니다."); return; }

        net.Subscribe(NetPacketType.Ping, OnPing);
        net.Subscribe(NetPacketType.StateSnapshotRequest, OnSnapshotRequest);
        net.Subscribe(NetPacketType.StateSnapshot, OnSnapshot);
        RegisterBackendEvents();
        pausedAtSec = NetClock.Now;
    }

    private void OnDestroy()
    {
        if (net != null)
        {
            net.Unsubscribe(NetPacketType.Ping, OnPing);
            net.Unsubscribe(NetPacketType.StateSnapshotRequest, OnSnapshotRequest);
            net.Unsubscribe(NetPacketType.StateSnapshot, OnSnapshot);
        }
        UnregisterBackendEvents();
    }

    private void RegisterBackendEvents()
    {
        if (backendEventsRegistered || !Backend.IsInitialized) return;
        Backend.Match.OnSessionOffline -= HandleSessionOffline;
        Backend.Match.OnSessionOffline += HandleSessionOffline;
        Backend.Match.OnSessionOnline -= HandleSessionOnline;
        Backend.Match.OnSessionOnline += HandleSessionOnline;
        Backend.Match.OnLeaveInGameServer -= HandleLeaveInGameServer;
        Backend.Match.OnLeaveInGameServer += HandleLeaveInGameServer;
        Backend.Match.OnChangeSuperGamer -= HandleChangeSuperGamer;
        Backend.Match.OnChangeSuperGamer += HandleChangeSuperGamer;
        Backend.Match.OnSessionJoinInServer -= HandleJoinInServer;
        Backend.Match.OnSessionJoinInServer += HandleJoinInServer;
        backendEventsRegistered = true;
    }

    private void UnregisterBackendEvents()
    {
        if (!backendEventsRegistered || !Backend.IsInitialized) return;
        Backend.Match.OnSessionOffline -= HandleSessionOffline;
        Backend.Match.OnSessionOnline -= HandleSessionOnline;
        Backend.Match.OnLeaveInGameServer -= HandleLeaveInGameServer;
        Backend.Match.OnChangeSuperGamer -= HandleChangeSuperGamer;
        Backend.Match.OnSessionJoinInServer -= HandleJoinInServer;
        backendEventsRegistered = false;
    }

    /// <summary>InGameNet이 새 매치를 준비할 때 호출. 상태를 초기화하고 감시를 시작한다.</summary>
    public void OnMatchPrepared()
    {
        RegisterBackendEvents();
        StopReconnectRoutine();
        matchEnded = false;
        abortRaised = false;
        probePending = false;
        probeEchoed = false;
        probeFailCount = 0;
        nextKeepAliveAt = NetClock.Now + livenessInterval;
        lastReach = Application.internetReachability;
        lastSnapshotSentAt.Clear();
        SetState(NetConnectionState.Connected);
    }

    /// <summary>모드/맵 설정(ModeConfig)에서 값을 덮어쓸 때 사용.</summary>
    public void ApplySettings(float reconnectLimitSeconds, HostLossPolicy policy, int minPlayersPerTeamValue)
    {
        reconnectTimeLimit = reconnectLimitSeconds;
        hostLossPolicy = policy;
        minPlayersPerTeam = minPlayersPerTeamValue;
    }

    private void SetState(NetConnectionState next)
    {
        if (state == next) return;
        NetLog.Info("연결 상태: " + state + " → " + next);
        state = next;
        OnStateChanged?.Invoke(next);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 감시 (매 프레임)

    private void Update()
    {
        if (net == null || !net.HasActiveMatch || net.IsOfflineTest) return;
        if (state != NetConnectionState.Connected) return;   // 끊김/복구 중에는 코루틴이 처리

        WatchNetworkReachability();
        if (state != NetConnectionState.Connected) return;
        WatchLiveness();
        if (state != NetConnectionState.Connected) return;
        WatchRemotePlayers();
    }

    /// <summary>Wi-Fi ↔ LTE 전환, 인터넷 끊김 감지. 전환되면 기존 소켓은 죽은 것으로 보고 곧바로 확인한다.</summary>
    private void WatchNetworkReachability()
    {
        var reach = Application.internetReachability;
        if (reach == lastReach) return;

        var previous = lastReach;
        lastReach = reach;
        NetLog.Info("네트워크 상태 변화: " + previous + " → " + reach);

        if (reach == NetworkReachability.NotReachable) HandleLocalDisconnect("인터넷 연결 없음");
        else if (previous != NetworkReachability.NotReachable) ForceProbeSoon();   // Wi-Fi ↔ LTE
    }

    /// <summary>주기적으로 Ping을 보내 에코가 돌아오는지 확인한다. (서버는 보낸 사람에게도 되돌려준다)</summary>
    private void WatchLiveness()
    {
        if (livenessInterval <= 0f || !net.IsSceneReady) return;
        double now = NetClock.Now;

        if (probePending)
        {
            if (now - probeSentAt < probeTimeout) return;

            probePending = false;
            probeFailCount++;
            NetLog.Warn("연결 확인 응답 없음 (" + probeFailCount + "/" + probeFailThreshold + ")");
            if (probeFailCount >= probeFailThreshold) { HandleLocalDisconnect("연결 확인 실패"); return; }
            SendProbe();
            return;
        }

        if (now >= nextKeepAliveAt) SendProbe();
    }

    private void SendProbe()
    {
        probeNonce = (uint)UnityEngine.Random.Range(1, int.MaxValue);
        probeSentAt = NetClock.Now;
        probePending = true;
        probeEchoed = false;
        net.Send(NetPacketType.Ping, w => w.WriteUInt(probeNonce));
    }

    private void ForceProbeSoon()
    {
        probePending = false;
        probeFailCount = Mathf.Max(0, probeFailThreshold - 1);   // 이번 확인에서 한 번만 실패해도 끊김으로 본다
        nextKeepAliveAt = 0;
    }

    private void OnPing(NetPacket packet)
    {
        if (!packet.FromLocal) return;
        if (packet.OpenReader().ReadUInt() != probeNonce) return;

        probePending = false;
        probeEchoed = true;
        probeFailCount = 0;
        nextKeepAliveAt = NetClock.Now + livenessInterval;
    }

    /// <summary>모바일: 백그라운드에서 돌아오면 소켓이 죽었을 수 있으므로 바로 확인한다.</summary>
    private void OnApplicationPause(bool paused)
    {
        if (net == null || !net.HasActiveMatch || net.IsOfflineTest) return;

        if (paused) { pausedAtSec = NetClock.Now; return; }

        NetLog.Info("앱 복귀. 백그라운드 시간 " + (NetClock.Now - pausedAtSec).ToString("0.0") + "초");
        if (state == NetConnectionState.Connected) ForceProbeSoon();
        // 다른 상태(끊김/복구 중)는 코루틴이 벽시계 기준으로 제한 시간을 계산하므로 별도 처리 불필요
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 내 연결 끊김 → 재접속

    private void HandleLocalDisconnect(string reason)
    {
        if (net == null || !net.HasActiveMatch || matchEnded) return;
        if (state != NetConnectionState.Connected) return;   // 이미 끊김/복구/종료 상태

        NetLog.Warn("내 연결 끊김: " + reason);
        deadlineSec = NetClock.Now + reconnectTimeLimit;
        leaveEventReceived = false;
        probePending = false;
        probeEchoed = false;

        var me = net.LocalPlayer;
        if (me != null) { me.IsOnline = false; me.DisconnectedAtSec = NetClock.Now; }

        SetState(NetConnectionState.Disconnected);
        OnLocalDisconnected?.Invoke();
        reconnectRoutine = StartCoroutine(ReconnectRoutine(false));
    }

    private IEnumerator ReconnectRoutine(bool coldStart)
    {
        if (coldStart)
        {
            // 앱 재시작 복귀: 먼저 인게임 씬을 불러와 복구 대상 스크립트들이 준비되게 한다.
            AsyncOperation load = SceneManager.LoadSceneAsync(net.SceneName);
            if (load == null) { Fail(ReconnectFailReason.GameEnded); yield break; }
            while (!load.isDone) yield return null;
            leaveEventReceived = true;   // 연결 자체가 없으므로 정리할 것이 없다
        }
        else
        {
            // 1) SDK 자동 재접속에 시간을 준다. Ping 에코가 돌아오면 연결이 살아난 것이다.
            double autoUntil = NetClock.Now + autoReconnectWait;
            double nextProbe = 0;
            while (NetClock.Now < autoUntil && NetClock.Now < deadlineSec && !leaveEventReceived)
            {
                if (NetClock.Now >= nextProbe) { SendProbe(); nextProbe = NetClock.Now + 1.0; }
                if (probeEchoed)
                {
                    NetLog.Info("SDK 자동 재접속으로 연결 복구됨");
                    net.SetReconnecting(true);
                    yield return StartCoroutine(RecoverRoutine());
                    yield break;
                }
                yield return null;
            }

            // 2) 자동 재접속이 안 되면 기존 소켓을 정리한다.
            if (!leaveEventReceived)
            {
                TryLeaveGameServer();
                double leaveUntil = NetClock.Now + 2.0;
                while (!leaveEventReceived && NetClock.Now < leaveUntil) yield return null;
            }
        }

        // 3) 수동 재접속: IsGameRoomActivate → JoinGameServer(reconnect) → 이벤트 대기
        net.SetReconnecting(true);   // BACKEND_MATCH의 방 입장 핸들러가 JoinGameRoom을 호출하지 않도록 막는다
        SetState(NetConnectionState.Reconnecting);

        while (NetClock.Now < deadlineSec)
        {
            // 3-1) 재접속 가능한 게임이 있는가
            bool done = false, success = false;
            string statusCode = string.Empty, address = null, token = null;
            ushort port = 0;

            Backend.Match.IsGameRoomActivate(callback =>
            {
                try
                {
                    success = callback.IsSuccess();
                    if (success)
                    {
                        var json = callback.GetReturnValuetoJSON();
                        address = json["serverPublicHostName"].ToString();
                        port = Convert.ToUInt16(json["serverPort"].ToString());
                        token = json["roomToken"].ToString();
                    }
                    else
                    {
                        statusCode = callback.GetStatusCode();
                    }
                }
                catch (Exception exception)
                {
                    success = false;
                    NetLog.Warn("IsGameRoomActivate 응답 처리 실패: " + exception.Message);
                }
                finally { done = true; }
            });

            double apiUntil = NetClock.Now + 8.0;
            while (!done && NetClock.Now < apiUntil) yield return null;

            if (!done || !success)
            {
                if (done && statusCode == "404")
                {
                    // 정상 응답: 돌아갈 게임이 없다 (이미 종료되었거나 끊긴 동안 새 매칭을 신청함)
                    Fail(ReconnectFailReason.GameEnded);
                    yield break;
                }
                // 네트워크가 아직 복구되지 않은 일시 오류 → 제한 시간 안에서 재시도
                yield return StartCoroutine(WaitSeconds(retryInterval));
                continue;
            }

            NetLog.Info("재접속 가능한 게임 확인. 서버 roomToken=" + token + ", 저장된 값=" + net.RoomToken);
            net.SetRoomToken(token);

            // 3-2) 인게임 서버에 재접속 (JoinGameRoom은 호출하지 않는다)
            joinPending = true;
            joinOk = false;
            ErrorInfo errorInfo = null;
            bool accepted = false;
            try { accepted = Backend.Match.JoinGameServer(address, port, true, out errorInfo); }
            catch (Exception exception) { NetLog.Warn("JoinGameServer 예외: " + exception.Message); }

            if (!accepted)
            {
                joinPending = false;
                NetLog.Warn("JoinGameServer 요청 실패: " + (errorInfo != null ? errorInfo.Reason + " / " + errorInfo.Category : "-"));
                yield return StartCoroutine(WaitSeconds(retryInterval));
                continue;
            }

            // 3-3) 인증 결과 이벤트(OnSessionJoinInServer) 대기
            double joinUntil = NetClock.Now + joinTimeout;
            while (joinPending && NetClock.Now < joinUntil) yield return null;

            if (!joinOk)
            {
                joinPending = false;
                NetLog.Warn("재접속 인증 실패 또는 시간 초과 → 재시도");
                TryLeaveGameServer();
                yield return StartCoroutine(WaitSeconds(1f));
                continue;
            }

            yield return StartCoroutine(RecoverRoutine());
            yield break;
        }

        Fail(ReconnectFailReason.TimeLimit);
    }

    /// <summary>재접속 성공 직후: 호스트에게 현재 상태를 받아 각 스크립트에 복구시킨다.</summary>
    private IEnumerator RecoverRoutine()
    {
        SetState(NetConnectionState.Recovering);
        net.RefreshLocalSessionId();
        snapshotBytes = null;

        // 앱 재시작 복귀: 유저 목록(명단)이 도착할 때까지 잠깐 기다린다.
        if (net.IsResumedMatch)
        {
            double listUntil = NetClock.Now + 5.0;
            while (net.Players.Count == 0 && NetClock.Now < listUntil) yield return null;
        }

        // 복구 대상 스크립트(씬)가 준비될 때까지 기다린다.
        double sceneUntil = NetClock.Now + 15.0;
        while (!net.IsSceneReady && NetClock.Now < sceneUntil) yield return null;

        bool othersAlive = net.Players.Any(p => !p.IsLocal && p.IsOnline && !p.IsForfeited);
        for (int attempt = 0; attempt < snapshotRetries && snapshotBytes == null && othersAlive; attempt++)
        {
            net.Send(NetPacketType.StateSnapshotRequest, w => w.WriteUInt(net.LocalSessionId), true);
            double until = NetClock.Now + snapshotTimeout;
            while (snapshotBytes == null && NetClock.Now < until) yield return null;
        }

        bool applied = false;
        if (snapshotBytes != null) applied = net.ApplySnapshot(snapshotBytes);
        snapshotBytes = null;

        if (net.IsResumedMatch && !applied)
        {
            Fail(ReconnectFailReason.SnapshotUnavailable);
            yield break;
        }

        // 내가 끊겨 있는 동안 다른 유저들이 나를 이탈 확정했다면 돌아갈 수 없다.
        var me = net.LocalPlayer;
        if (me != null && me.IsForfeited)
        {
            Fail(ReconnectFailReason.TimeLimit);
            yield break;
        }

        FinishReconnect(applied);
    }

    private void FinishReconnect(bool snapshotApplied)
    {
        net.SetReconnecting(false);
        lastReconnectFinishedAt = NetClock.Now;

        var me = net.LocalPlayer;
        if (me != null) { me.IsOnline = true; me.DisconnectedAtSec = 0; }

        probePending = false;
        probeFailCount = 0;
        nextKeepAliveAt = NetClock.Now + livenessInterval;
        lastReach = Application.internetReachability;
        reconnectRoutine = null;

        net.NotifyPlayersChanged();
        SetState(NetConnectionState.Connected);
        NetLog.Info("재접속 완료. 상태 복구=" + snapshotApplied);
        OnLocalReconnected?.Invoke(snapshotApplied);
    }

    private void Fail(ReconnectFailReason reason)
    {
        if (state == NetConnectionState.Failed || state == NetConnectionState.Ended) return;

        NetLog.Warn("재접속 실패: " + reason);
        net.SetReconnecting(false);
        reconnectRoutine = null;

        var me = net.LocalPlayer;
        if (me != null && reason == ReconnectFailReason.TimeLimit) me.IsForfeited = true;

        SetState(NetConnectionState.Failed);
        TryLeaveGameServer();
        net.EndMatch();
        OnLocalReconnectFailed?.Invoke(reason);
    }

    private void StopReconnectRoutine()
    {
        if (reconnectRoutine != null) { StopCoroutine(reconnectRoutine); reconnectRoutine = null; }
    }

    private void TryLeaveGameServer()
    {
        try { if (Backend.IsInitialized) Backend.Match.LeaveGameServer(); }
        catch (Exception exception) { NetLog.Warn("LeaveGameServer 예외: " + exception.Message); }
    }

    private static IEnumerator WaitSeconds(float seconds)
    {
        double until = NetClock.Now + seconds;
        while (NetClock.Now < until) yield return null;
    }

    private void HandleJoinInServer(JoinChannelEventArgs args)
    {
        if (!InGameNet.IsReconnecting || !joinPending) return;
        joinOk = args != null && args.ErrInfo.Category == ErrorCode.Success;
        joinPending = false;
        NetLog.Info("재접속 인증 결과: " + (joinOk ? "성공" : "실패"));
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 스냅샷 요청/응답 (호스트 ↔ 재접속자)

    /// <summary>호스트만 응답한다. 같은 유저의 연속 요청은 1초에 한 번만 처리.</summary>
    private void OnSnapshotRequest(NetPacket packet)
    {
        if (packet.FromLocal || !net.IsHost) return;

        uint target = packet.OpenReader().ReadUInt();
        double now = NetClock.Now;
        double last;
        if (lastSnapshotSentAt.TryGetValue(target, out last) && now - last < 1.0) return;
        lastSnapshotSentAt[target] = now;

        byte[] snapshot = net.BuildSnapshot();
        net.Send(NetPacketType.StateSnapshot, w => { w.WriteUInt(target); w.WriteBlob(snapshot); }, true);
        NetLog.Info("스냅샷 전송 → 세션 " + target + " (" + snapshot.Length + "B)");
    }

    private void OnSnapshot(NetPacket packet)
    {
        if (state != NetConnectionState.Recovering) return;

        var reader = packet.OpenReader();
        uint target = reader.ReadUInt();
        if (target != net.LocalSessionId) return;

        snapshotBytes = reader.ReadBlob();
        NetLog.Info("스냅샷 수신 (" + snapshotBytes.Length + "B)");
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 서버 이벤트: 접속 끊김 / 재접속 / 종료 / 호스트 변경

    private void HandleSessionOffline(MatchInGameSessionEventArgs args)
    {
        if (net == null || !net.HasActiveMatch || matchEnded || args == null || args.GameRecord == null) return;

        uint sessionId = (uint)args.GameRecord.m_sessionId;
        if (sessionId == net.LocalSessionId)
        {
            if (NetClock.Now - lastReconnectFinishedAt < 3.0) return;   // 재접속 직전 연결의 늦은 알림 무시
            HandleLocalDisconnect("OnSessionOffline");
            return;
        }
        MarkRemoteOffline(sessionId);
    }

    private void HandleSessionOnline(MatchInGameSessionEventArgs args)
    {
        if (net == null || !net.HasActiveMatch || matchEnded || args == null || args.GameRecord == null) return;

        uint sessionId = (uint)args.GameRecord.m_sessionId;
        var player = net.GetPlayer(sessionId);
        if (player == null || player.IsLocal) return;

        if (player.IsForfeited)
        {
            NetLog.Warn("이미 이탈 확정된 유저가 재접속했습니다: " + player.Nickname + " (복귀 불가, 해당 유저 쪽에서 실패 처리됨)");
            return;
        }

        player.IsOnline = true;
        player.DisconnectedAtSec = 0;
        net.NotifyPlayersChanged();
        NetLog.Info("상대 재접속: " + player.Nickname);
        OnRemoteReconnected?.Invoke(player);
    }

    /// <summary>인게임 서버와의 접속이 닫혔다. 정상 종료가 아니면 끊김으로 처리한다.</summary>
    private void HandleLeaveInGameServer(MatchInGameSessionEventArgs args)
    {
        if (net == null || !net.HasActiveMatch) return;

        if (state == NetConnectionState.Disconnected || state == NetConnectionState.Reconnecting)
        {
            leaveEventReceived = true;   // 정리 완료 신호 → 수동 재접속 진행
            return;
        }
        if (matchEnded || state == NetConnectionState.Ended || state == NetConnectionState.Failed) return;
        if (NetClock.Now - lastReconnectFinishedAt < 3.0) return;   // 재접속 직전 연결의 늦은 종료 알림 무시

        HandleLocalDisconnect("OnLeaveInGameServer");
    }

    private void HandleChangeSuperGamer(MatchInGameChangeSuperGamerEventArgs args)
    {
        if (net == null || !net.HasActiveMatch || matchEnded) return;

        bool iAmHost = net.IsHost;
        if (args != null && args.NewSuperUserRecord != null && args.NewSuperUserRecord.m_sessionId != null)
            net.HostSessionId = (uint)args.NewSuperUserRecord.m_sessionId;
        else if (iAmHost)
            net.HostSessionId = net.LocalSessionId;

        // 이전 호스트는 연결이 끊긴 유저. OnSessionOffline도 오지만 순서와 무관하게 반영한다.
        if (args != null && args.OldSuperUserRecord != null && args.OldSuperUserRecord.m_sessionId != null)
        {
            uint oldHost = (uint)args.OldSuperUserRecord.m_sessionId;
            if (oldHost != net.LocalSessionId) MarkRemoteOffline(oldHost);
        }

        NetLog.Info("호스트 변경. 새 호스트=" + net.HostSessionId + ", 내가 호스트=" + iAmHost);
        OnHostChanged?.Invoke(iAmHost);

        // 게임 지속 여부 결정
        if (hostLossPolicy == HostLossPolicy.EndMatch) RaiseAbort(NetTeam.None, MatchAbortReason.HostLeft);
        else EvaluateMatchViability();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 상대 이탈 / 매치 지속 여부

    private void MarkRemoteOffline(uint sessionId)
    {
        var player = net.GetPlayer(sessionId);
        if (player == null || player.IsLocal || player.IsForfeited || !player.IsOnline) return;

        player.IsOnline = false;
        player.DisconnectedAtSec = NetClock.Now;
        net.NotifyPlayersChanged();
        NetLog.Info("상대 끊김: " + player.Nickname + " (유예 " + reconnectTimeLimit + "초)");
        OnRemoteDisconnected?.Invoke(player);
    }

    /// <summary>끊긴 상대가 제한 시간 안에 돌아오지 않으면 이탈 확정. 모든 클라이언트가 같은 기준으로 각자 판정한다.</summary>
    private void WatchRemotePlayers()
    {
        double now = NetClock.Now;
        bool anyForfeited = false;

        var list = net.Players;
        for (int i = 0; i < list.Count; i++)
        {
            var player = list[i];
            if (player.IsLocal || player.IsOnline || player.IsForfeited) continue;
            if (now - player.DisconnectedAtSec < reconnectTimeLimit) continue;

            player.IsForfeited = true;
            anyForfeited = true;
            net.NotifyPlayersChanged();
            NetLog.Warn("상대 이탈 확정: " + player.Nickname);
            OnRemoteForfeited?.Invoke(player);
        }

        if (anyForfeited) EvaluateMatchViability();
    }

    /// <summary>팀별로 이탈하지 않은 인원이 최소 인원 이상인지 확인하고, 아니면 매치 중단을 알린다.</summary>
    private void EvaluateMatchViability()
    {
        if (net.IsOfflineTest || abortRaised || matchEnded) return;

        int red = net.Players.Count(p => p.Team == NetTeam.Red && !p.IsForfeited);
        int blue = net.Players.Count(p => p.Team == NetTeam.Blue && !p.IsForfeited);
        bool hasBothTeams = net.Players.Any(p => p.Team == NetTeam.Red) && net.Players.Any(p => p.Team == NetTeam.Blue);
        if (!hasBothTeams) return;   // 연습방 등 단일 팀 모드는 해당 없음

        bool redOk = red >= minPlayersPerTeam;
        bool blueOk = blue >= minPlayersPerTeam;
        if (redOk && blueOk) return;

        if (!redOk && !blueOk) RaiseAbort(NetTeam.None, MatchAbortReason.AllPlayersLeft);
        else RaiseAbort(redOk ? NetTeam.Red : NetTeam.Blue, MatchAbortReason.OpponentsLeft);
    }

    private void RaiseAbort(NetTeam winner, MatchAbortReason reason)
    {
        if (abortRaised || matchEnded) return;
        abortRaised = true;
        NetLog.Warn("매치 중단: 승리 팀=" + winner + ", 사유=" + reason);
        OnMatchAborted?.Invoke(winner, reason);
    }

    /// <summary>
    /// 결과 처리를 시작하는 시점(MatchEnd 전송 직전)에 ResultReporter가 호출한다.
    /// 이후의 서버 접속 종료는 정상 종료로 보고 재접속하지 않는다.
    /// </summary>
    public void NotifyMatchEnded()
    {
        matchEnded = true;
        StopReconnectRoutine();
        SetState(NetConnectionState.Ended);
        if (net != null) net.EndMatch();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 앱 재시작 후 복귀 (로비에서 호출)

    /// <summary>
    /// 로그인 직후 로비에서 한 번 호출한다. 진행 중이던 매치가 있으면 인게임 씬으로 이동해 재접속을 진행한다.
    /// 콜백 인자: true = 복귀를 시작함(로비 UI를 멈출 것) / false = 복귀할 매치 없음.
    /// </summary>
    public static void TryResumeActiveMatch(Action<bool> onChecked)
    {
        InGameNet.Ensure().Connection.BeginResumeCheck(onChecked);
    }

    private void BeginResumeCheck(Action<bool> onChecked)
    {
        NetActiveMatchInfo info;
        if (!Backend.IsInitialized || net.HasActiveMatch || !InGameNet.TryLoadSavedMatch(out info))
        {
            if (onChecked != null) onChecked(false);
            return;
        }

        Backend.Match.IsGameRoomActivate(callback =>
        {
            bool success = false;
            string statusCode = string.Empty;
            try
            {
                success = callback.IsSuccess();
                if (!success) statusCode = callback.GetStatusCode();
            }
            catch (Exception exception) { NetLog.Warn("재접속 가능 여부 확인 실패: " + exception.Message); }

            if (!success)
            {
                if (statusCode == "404") InGameNet.ClearSavedMatch();   // 이미 끝난 게임: 저장 정보 폐기
                if (onChecked != null) onChecked(false);
                return;
            }

            net.RestoreForResume(info);
            matchEnded = false;
            abortRaised = false;
            deadlineSec = NetClock.Now + reconnectTimeLimit;
            leaveEventReceived = true;
            SetState(NetConnectionState.Disconnected);
            OnLocalDisconnected?.Invoke();
            reconnectRoutine = StartCoroutine(ReconnectRoutine(true));
            if (onChecked != null) onChecked(true);
        });
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region QA 도구 (인스펙터 우클릭 메뉴 / 코드에서 호출)

    [ContextMenu("Debug/내 연결 끊김 시뮬레이션")]
    public void DebugSimulateLocalDisconnect() { HandleLocalDisconnect("debug"); }

    [ContextMenu("Debug/첫 번째 상대 끊김 시뮬레이션")]
    public void DebugSimulateRemoteOffline()
    {
        var target = net != null ? net.Players.FirstOrDefault(p => !p.IsLocal && p.IsOnline) : null;
        if (target != null) MarkRemoteOffline(target.SessionId);
    }

    [ContextMenu("Debug/첫 번째 상대 재접속 시뮬레이션")]
    public void DebugSimulateRemoteOnline()
    {
        var target = net != null ? net.Players.FirstOrDefault(p => !p.IsLocal && !p.IsOnline && !p.IsForfeited) : null;
        if (target == null) return;
        target.IsOnline = true;
        target.DisconnectedAtSec = 0;
        net.NotifyPlayersChanged();
        OnRemoteReconnected?.Invoke(target);
    }

    #endregion
}
