using BackEnd;
using BackEnd.Tcp;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// InGameNet : 뒤끝 인게임 서버(릴레이) 위의 "네트워크 공용 계층"
//
// 역할
//  1) 매칭 완료 시 참가자 명단(세션ID/닉네임) 수집 + 레드/블루 팀 배정  (PrepareFromMatch)
//  2) 릴레이 송수신 래퍼 (Send / Subscribe), 패킷 형식 = [Magic][Version][Type][Payload]
//  3) 씬 전환 후에도 유지(DontDestroyOnLoad), 씬 준비 전 수신 패킷은 큐에 보관
//  4) 재접속용 상태 스냅샷 수집/적용 (INetStateProvider)
//  5) 전송 한도(쓰로틀링) 관리: 일반 10KB/s, 슈퍼 게이머 30KB/s → 초과분은 SDK가 조용히 버리므로 직접 관리
//
// 사용 순서
//  [로비] BACKEND_MATCH.HandleMatchInGameStart → InGameNet.Ensure().PrepareFromMatch(...) → 씬 로드
//  [인게임 씬] 각 스크립트가 Subscribe/RegisterStateProvider 한 뒤 → MatchFlow가 SetSceneReady() 호출
//  [종료] ResultReporter → NetConnectionManager.NotifyMatchEnded()
//
// 연결 끊김/재접속 처리는 NetConnectionManager(같은 오브젝트에 자동 부착)가 담당한다.
// ─────────────────────────────────────────────────────────────────────────────

public enum NetTeam : byte { None = 0, Red = 1, Blue = 2 }

public sealed class NetPlayer
{
    public uint SessionId;
    public BackEnd.Tcp.SessionId RawSession;   // 결과 보고(MatchEnd)용 원본 세션 ID. 오프라인 테스트에서는 기본값
    public string Nickname;
    public NetTeam Team;
    public int TeamIndex;               // 팀 안에서의 순번 (스폰 포인트 선택에 사용)
    public string CharacterId;          // 캐릭터 선택 후 채워짐
    public bool IsLocal;
    public bool IsOnline = true;        // 현재 서버와 연결되어 있는가
    public bool IsForfeited;            // 재접속 제한 시간 초과 등으로 영구 이탈 처리되었는가
    public double DisconnectedAtSec;    // NetClock 기준 끊긴 시각 (IsOnline == false일 때만 유효)
}

/// <summary>앱이 종료되었다가 다시 켜져도 진행 중인 매치로 돌아갈 수 있도록 로컬에 저장하는 정보.</summary>
[Serializable]
public class NetActiveMatchInfo
{
    public string roomToken;
    public int mode;
    public string sceneName;
    public string cardInDate;
    public long savedAtUnix;
}

public class InGameNet : MonoBehaviour
{
    public const byte Magic = 0xA7;            // 이 게임의 패킷임을 구분 (다른 형식 메시지는 무시)
    public const byte ProtocolVersion = 1;     // 패킷 형식을 바꾸면 올린다. 다르면 무시.

    private const string ActiveMatchPrefKey = "TA_ActiveMatch";
    private const double ActiveMatchExpireSec = 30 * 60;
    private const int MaxQueuedPackets = 512;
    private const int HostBytesPerSecond = 27000;    // 서버 한도 30KB/s 의 90%
    private const int GuestBytesPerSecond = 9000;    // 서버 한도 10KB/s 의 90%
    private const int SnapshotWarnBytes = 8000;

    public static InGameNet Instance { get; private set; }

    /// <summary>재접속 진행 중인가. BACKEND_MATCH의 방 입장 핸들러가 재접속 때 끼어들지 않도록 확인한다.</summary>
    public static bool IsReconnecting { get { return Instance != null && Instance.isReconnecting; } }

    [Header("Debug (QA용)")]
    [Tooltip("켜면 서버에서 오는 모든 릴레이 패킷을 버린다. 네트워크 불량(에코 끊김) 상황을 흉내낸다.")]
    public bool debugDropIncoming;
    [Tooltip("켜면 송수신/큐/예산 로그를 자세히 출력한다.")]
    public bool verboseLog;

    // ── 매치 컨텍스트 ────────────────────────────────────────────────────────
    public NetConnectionManager Connection { get; private set; }
    public BACKEND_MATCH.TacticalMatchMode Mode { get; private set; }
    public string SceneName { get; private set; }
    public string RoomToken { get; private set; }
    public string MatchCardInDate { get; private set; }
    public uint LocalSessionId { get; private set; }
    public uint HostSessionId { get; set; }                  // 알 수 없으면 0
    public bool HasActiveMatch { get; private set; }         // 매치 시작 ~ 결과 처리 전까지 true
    public bool IsOfflineTest { get; private set; }          // 로비 없이 씬만 실행한 1인 테스트
    public bool IsResumedMatch { get; private set; }         // 앱 재시작 후 복귀한 매치 (씬은 스냅샷으로 복구)
    public bool IsSceneReady { get { return sceneReady; } }

    public bool IsHost
    {
        get
        {
            if (IsOfflineTest) return true;
            try { return Backend.IsInitialized && Backend.Match.IsSuperGamer(); }
            catch (Exception) { return false; }
        }
    }

    private readonly List<NetPlayer> players = new List<NetPlayer>();
    public IReadOnlyList<NetPlayer> Players { get { return players; } }
    public NetPlayer LocalPlayer { get { return GetPlayer(LocalSessionId); } }
    public event Action PlayersChanged;

    // ── 내부 상태 ────────────────────────────────────────────────────────────
    private bool isReconnecting;
    private bool sceneReady;

    // 씬 이동 전 "모두 로딩 끝났는가" 확인용
    private readonly HashSet<uint> sceneLoadedSessions = new HashSet<uint>();
    private float announceUntil;
    private float nextAnnounceAt;
    private bool eventsRegistered;

    private readonly Dictionary<NetPacketType, List<Action<NetPacket>>> handlers = new Dictionary<NetPacketType, List<Action<NetPacket>>>();
    private readonly Queue<NetPacket> pendingQueue = new Queue<NetPacket>();
    private readonly Dictionary<ushort, INetStateProvider> providers = new Dictionary<ushort, INetStateProvider>();

    private int budgetWindowSecond = -1;
    private int budgetUsedBytes;
    private readonly Queue<byte[]> deferredCritical = new Queue<byte[]>();

    // ═════════════════════════════════════════════════════════════════════════
    #region 생성 / 수명

    /// <summary>없으면 만들고(DontDestroyOnLoad) 있으면 그대로 돌려준다.</summary>
    public static InGameNet Ensure()
    {
        if (Instance != null) return Instance;

        var go = new GameObject("[InGameNet]");
        DontDestroyOnLoad(go);
        return go.AddComponent<InGameNet>();      // Awake에서 Instance 설정 + NetConnectionManager 부착
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        NetLog.Verbose = verboseLog;

        // 연결/재접속 관리자는 항상 같은 오브젝트에 붙어 함께 유지된다.
        Connection = GetComponent<NetConnectionManager>();
        if (Connection == null) Connection = gameObject.AddComponent<NetConnectionManager>();

        RegisterBackendEvents();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        UnregisterBackendEvents();
        Instance = null;
    }

    private void RegisterBackendEvents()
    {
        if (eventsRegistered || !Backend.IsInitialized) return;
        // -= 후 += : 중복 등록 방지 (BackendTDMManager 등 다른 스크립트와 함께 구독 가능)
        Backend.Match.OnMatchRelay -= HandleMatchRelay;
        Backend.Match.OnMatchRelay += HandleMatchRelay;
        Backend.Match.OnSessionListInServer -= HandleSessionList;
        Backend.Match.OnSessionListInServer += HandleSessionList;
        eventsRegistered = true;
    }

    private void UnregisterBackendEvents()
    {
        if (!eventsRegistered || !Backend.IsInitialized) return;
        Backend.Match.OnMatchRelay -= HandleMatchRelay;
        Backend.Match.OnSessionListInServer -= HandleSessionList;
        eventsRegistered = false;
    }

    private void Update()
    {
        NetLog.Verbose = verboseLog;
        PollBackend();
        FlushDeferredCritical();
        UpdateSceneLoadedAnnounce();
    }

    /// <summary>
    /// 릴레이 이벤트는 Poll()을 매 프레임 호출해야 도착한다.
    /// 로비의 BACKEND_MATCH가 이미 Poll 중이면 맡기고, 앱 재시작 복귀처럼 Poll 주체가 없을 때만 직접 호출한다.
    /// </summary>
    private void PollBackend()
    {
        if (!HasActiveMatch || IsOfflineTest || !Backend.IsInitialized) return;
        var match = BACKEND_MATCH.Instance;
        if (match != null && match.IsPollingMatchEvents) return;
        try { Backend.Match.Poll(); }
        catch (Exception exception) { NetLog.Error("Poll 예외: " + exception.Message); }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 매치 시작: 명단 수집 + 팀 배정

    /// <summary>
    /// BACKEND_MATCH가 OnMatchInGameStart를 받은 직후, 씬을 불러오기 전에 호출한다.
    /// 참가자 명단/닉네임을 받아 팀을 배정하고 컨텍스트를 만든다.
    /// </summary>
    public void PrepareFromMatch(IEnumerable<MatchUserGameRecord> records, BACKEND_MATCH.TacticalMatchMode mode,
                                 string sceneName, string roomToken, string cardInDate)
    {
        ResetForNewMatch();
        RegisterBackendEvents();

        Mode = mode;
        SceneName = sceneName;
        RoomToken = roomToken;
        MatchCardInDate = cardInDate;
        LocalSessionId = ReadMySessionId();
        HasActiveMatch = true;
        if (IsHost) HostSessionId = LocalSessionId;

        BuildPlayers(records);
        SaveActiveMatch();
        Connection.OnMatchPrepared();

        NetLog.Info("매치 준비 완료. 모드=" + mode + ", 씬=" + sceneName + ", 인원=" + players.Count + ", 내 세션=" + LocalSessionId + ", 호스트=" + IsHost);
    }

    /// <summary>로비 없이 씬만 실행했을 때의 1인 테스트. 송신은 자기 자신에게 즉시 되돌려준다(에코 흉내).</summary>
    public void PrepareOffline(BACKEND_MATCH.TacticalMatchMode mode, string sceneName, string nickname)
    {
        ResetForNewMatch();
        IsOfflineTest = true;
        HasActiveMatch = true;
        Mode = mode;
        SceneName = sceneName;
        LocalSessionId = 1;
        HostSessionId = 1;

        players.Add(new NetPlayer { SessionId = 1, Nickname = string.IsNullOrEmpty(nickname) ? "Player" : nickname, Team = NetTeam.Red, IsLocal = true });
        AssignTeamIndices();
        PlayersChanged?.Invoke();
        Connection.OnMatchPrepared();
        NetLog.Warn("오프라인 테스트 모드로 시작합니다. (네트워크 전송 없음)");
    }

    /// <summary>앱 재시작 후 진행 중이던 매치로 복귀할 때 NetConnectionManager가 호출. 명단은 방 입장 후 받는 유저 목록으로 채운다.</summary>
    public void RestoreForResume(NetActiveMatchInfo info)
    {
        ResetForNewMatch();
        RegisterBackendEvents();

        Mode = (BACKEND_MATCH.TacticalMatchMode)info.mode;
        SceneName = info.sceneName;
        RoomToken = info.roomToken;
        MatchCardInDate = info.cardInDate;
        HasActiveMatch = true;
        IsResumedMatch = true;
        NetLog.Info("앱 재시작 복귀 컨텍스트 복원. 모드=" + Mode + ", 씬=" + SceneName);
    }

    private void ResetForNewMatch()
    {
        players.Clear();
        pendingQueue.Clear();
        deferredCritical.Clear();
        sceneReady = false;
        sceneLoadedSessions.Clear();
        announceUntil = 0f;
        isReconnecting = false;
        IsOfflineTest = false;
        IsResumedMatch = false;
        HasActiveMatch = false;
        HostSessionId = 0;
        LocalSessionId = 0;
        budgetWindowSecond = -1;
        budgetUsedBytes = 0;
    }

    /// <summary>
    /// 모든 클라이언트가 같은 결과를 얻도록 세션 ID 오름차순으로 정렬한 뒤 배정한다. (네트워크 전송 불필요)
    /// 서버 매치 카드가 팀 번호를 나눠 줬으면 그대로 따르고, 아니면 번갈아 배정한다.
    /// </summary>
    private void BuildPlayers(IEnumerable<MatchUserGameRecord> source)
    {
        players.Clear();
        var records = (source ?? Enumerable.Empty<MatchUserGameRecord>())
            .Where(r => r != null && r.m_sessionId != null).ToList();
        records.Sort((a, b) => ((uint)a.m_sessionId).CompareTo((uint)b.m_sessionId));

        bool serverTeams = records.Select(r => r.m_teamNumber).Distinct().Count() > 1;
        var teamNumbers = records.Select(r => r.m_teamNumber).Distinct().OrderBy(n => n).ToList();

        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            bool isRed = serverTeams ? (teamNumbers.IndexOf(record.m_teamNumber) % 2 == 0) : (i % 2 == 0);
            uint sessionId = (uint)record.m_sessionId;

            players.Add(new NetPlayer
            {
                SessionId = sessionId,
                RawSession = record.m_sessionId,
                Nickname = string.IsNullOrEmpty(record.m_nickname) ? "Player" + (i + 1) : record.m_nickname,
                Team = isRed ? NetTeam.Red : NetTeam.Blue,
                IsLocal = sessionId == LocalSessionId
            });
        }

        AssignTeamIndices();
        PlayersChanged?.Invoke();
    }

    private void AssignTeamIndices()
    {
        foreach (NetTeam team in new[] { NetTeam.Red, NetTeam.Blue })
        {
            int index = 0;
            foreach (var player in players.Where(p => p.Team == team)) player.TeamIndex = index++;
        }
    }

    private uint ReadMySessionId()
    {
        try { return (uint)Backend.Match.GetMySessionId(); }
        catch (Exception exception) { NetLog.Warn("내 세션 ID를 읽지 못했습니다: " + exception.Message); return 0; }
    }

    /// <summary>방(재)입장 후 유저 목록 수신. 앱 재시작 복귀 때는 이 목록으로 명단을 만든다.</summary>
    private void HandleSessionList(MatchInGameSessionListEventArgs args)
    {
        if (!HasActiveMatch || args == null || args.ErrInfo != ErrorCode.Success || args.GameRecords == null) return;

        if (LocalSessionId == 0) LocalSessionId = ReadMySessionId();
        if (players.Count == 0)
        {
            BuildPlayers(args.GameRecords);
            NetLog.Info("유저 목록으로 명단 구성: " + players.Count + "명");
        }
    }

    public NetPlayer GetPlayer(uint sessionId)
    {
        for (int i = 0; i < players.Count; i++)
            if (players[i].SessionId == sessionId) return players[i];
        return null;
    }

    public IEnumerable<NetPlayer> PlayersOf(NetTeam team) { return players.Where(p => p.Team == team); }

    /// <summary>접속 상태 등 명단 변화를 UI/모드에 알린다.</summary>
    public void NotifyPlayersChanged() { PlayersChanged?.Invoke(); }

    public void SetRoomToken(string token) { if (!string.IsNullOrEmpty(token)) RoomToken = token; }
    public void SetReconnecting(bool value) { isReconnecting = value; }

    /// <summary>NetConnectionManager가 호스트 변경을 반영한다.</summary>
    public void RefreshLocalSessionId() { if (LocalSessionId == 0) LocalSessionId = ReadMySessionId(); }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 송신 / 수신

    /// <summary>
    /// 방 전체(나 포함)에 패킷을 보낸다.
    /// critical = true 이면 전송 한도 초과 시 버리지 않고 다음 1초 구간에 다시 시도한다. (점수/상태 변경 등 유실되면 안 되는 것)
    /// 위치처럼 계속 갱신되는 값은 critical = false 로 두고 초과분은 버린다.
    /// </summary>
    public bool Send(NetPacketType type, Action<NetWriter> write = null, bool critical = false)
    {
        byte[] bytes = Encode(type, write);

        if (IsOfflineTest)
        {
            // 서버 에코를 흉내: 바로 자기 자신에게 전달
            DeliverRaw(bytes, LocalSessionId);
            return true;
        }

        if (!TryConsumeBudget(bytes.Length))
        {
            if (critical) deferredCritical.Enqueue(bytes);
            else NetLog.Warn("전송 한도 초과로 패킷을 버렸습니다: " + type + " (" + bytes.Length + "B)");
            return false;
        }
        return SendRaw(bytes);
    }

    private bool SendRaw(byte[] bytes)
    {
        try
        {
            Backend.Match.SendDataToInGameRoom(bytes);
            return true;
        }
        catch (Exception exception)
        {
            NetLog.Warn("릴레이 전송 실패(연결 끊김 가능): " + exception.Message);
            return false;
        }
    }

    private static byte[] Encode(NetPacketType type, Action<NetWriter> write)
    {
        var writer = new NetWriter(32);
        writer.WriteByte(Magic).WriteByte(ProtocolVersion).WriteByte((byte)type);
        if (write != null) write(writer);
        return writer.ToArray();
    }

    /// <summary>SDK 한도는 "절대 시각 1초 구간" 기준이다. 같은 방식으로 직접 계산해 초과를 미리 막는다.</summary>
    private bool TryConsumeBudget(int bytes)
    {
        int second = Mathf.FloorToInt(Time.realtimeSinceStartup);
        if (second != budgetWindowSecond)
        {
            budgetWindowSecond = second;
            budgetUsedBytes = 0;
        }

        int limit = IsHost ? HostBytesPerSecond : GuestBytesPerSecond;
        if (budgetUsedBytes + bytes > limit) return false;
        budgetUsedBytes += bytes;
        return true;
    }

    private void FlushDeferredCritical()
    {
        while (deferredCritical.Count > 0)
        {
            byte[] bytes = deferredCritical.Peek();
            if (!TryConsumeBudget(bytes.Length)) break;
            deferredCritical.Dequeue();
            SendRaw(bytes);
        }
    }

    private void HandleMatchRelay(MatchRelayEventArgs args)
    {
        if (!HasActiveMatch || args == null || args.BinaryUserData == null) return;
        if (debugDropIncoming) return;

        DeliverRaw(args.BinaryUserData, (uint)args.From.SessionId);
    }

    private void DeliverRaw(byte[] data, uint sender)
    {
        if (data.Length < 3 || data[0] != Magic) return;   // 다른 형식의 메시지(예: 기존 문자열 프로토콜)는 무시
        if (data[1] != ProtocolVersion)
        {
            NetLog.Warn("프로토콜 버전이 다른 패킷을 무시했습니다. 수신=" + data[1] + ", 내 버전=" + ProtocolVersion);
            return;
        }

        var type = (NetPacketType)data[2];
        var payload = new byte[data.Length - 3];
        Buffer.BlockCopy(data, 3, payload, 0, payload.Length);
        var packet = new NetPacket(type, sender, sender == LocalSessionId, payload);
        if (type == NetPacketType.SceneLoaded) sceneLoadedSessions.Add(sender);

        // 코어 패킷(연결 확인/스냅샷)은 씬 준비와 무관하게 즉시 처리, 나머지는 씬이 준비될 때까지 보관
        if (sceneReady || IsImmediate(type)) Dispatch(packet);
        else Enqueue(packet);
    }

    private static bool IsImmediate(NetPacketType type)
    {
        return type == NetPacketType.Ping
            || type == NetPacketType.SceneLoaded
            || type == NetPacketType.StateSnapshotRequest
            || type == NetPacketType.StateSnapshot;
    }

    private void Enqueue(NetPacket packet)
    {
        if (pendingQueue.Count >= MaxQueuedPackets)
        {
            pendingQueue.Dequeue();
            NetLog.Warn("씬 준비 전 패킷 큐가 가득 차 가장 오래된 패킷을 버렸습니다.");
        }
        pendingQueue.Enqueue(packet);
        NetLog.Info("씬 준비 전 패킷 보관: " + packet.Type + " (큐 " + pendingQueue.Count + ")");
    }

    private void Dispatch(NetPacket packet)
    {
        List<Action<NetPacket>> list;
        if (!handlers.TryGetValue(packet.Type, out list) || list.Count == 0) return;

        // 복사본으로 순회: 핸들러 안에서 구독/해제해도 안전
        foreach (var handler in list.ToArray())
        {
            try { handler(packet); }
            catch (Exception exception) { NetLog.Error("패킷 처리 예외 [" + packet.Type + "]: " + exception); }
        }
    }

    public void Subscribe(NetPacketType type, Action<NetPacket> handler)
    {
        List<Action<NetPacket>> list;
        if (!handlers.TryGetValue(type, out list))
        {
            list = new List<Action<NetPacket>>();
            handlers[type] = list;
        }
        if (!list.Contains(handler)) list.Add(handler);
    }

    public void Unsubscribe(NetPacketType type, Action<NetPacket> handler)
    {
        List<Action<NetPacket>> list;
        if (handlers.TryGetValue(type, out list)) list.Remove(handler);
    }

    /// <summary>
    /// 인게임 씬의 스크립트들이 Subscribe/RegisterStateProvider를 마친 뒤 (보통 MatchFlow가) 한 번 호출한다.
    /// 그동안 보관했던 패킷이 받은 순서대로 전달된다.
    /// </summary>
    public void SetSceneReady()
    {
        if (sceneReady) return;
        sceneReady = true;
        NetLog.Info("씬 준비 완료. 보관된 패킷 " + pendingQueue.Count + "개 전달");
        while (pendingQueue.Count > 0) Dispatch(pendingQueue.Dequeue());
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 씬 이동 전 로딩 동기화 (로비에서 사용)

    // 흐름: 각자 인게임 씬을 90% 까지 미리 불러 둔다 → ReportSceneLoaded() → 모두의 SceneLoaded 가 모이면 같이 씬을 연다.
    // 상대 기기가 아직 매치 준비 전이라 패킷을 버릴 수 있으므로, 완료 신고는 1초마다 반복해서 보낸다. (같은 신고를 여러 번 받아도 문제 없음)

    /// <summary>내 기기의 씬 로딩이 끝났음을 알린다. keepAnnouncingSeconds 동안 1초마다 다시 알린다.</summary>
    public void ReportSceneLoaded(float keepAnnouncingSeconds)
    {
        sceneLoadedSessions.Add(LocalSessionId);
        announceUntil = Time.unscaledTime + Mathf.Max(1f, keepAnnouncingSeconds);
        SendSceneLoaded();
    }

    private void SendSceneLoaded()
    {
        nextAnnounceAt = Time.unscaledTime + 1f;
        Send(NetPacketType.SceneLoaded, null, true);
    }

    private void UpdateSceneLoadedAnnounce()
    {
        if (announceUntil <= 0f) return;

        float now = Time.unscaledTime;
        if (now >= announceUntil) { announceUntil = 0f; return; }

        // 모두 끝났어도 아직 못 받은 사람이 있을 수 있어 5초는 더 알려 준다.
        if (AllSceneLoaded && announceUntil > now + 5f) announceUntil = now + 5f;
        if (now >= nextAnnounceAt) SendSceneLoaded();
    }

    private bool MustWaitFor(NetPlayer p)
    {
        if (p == null) return false;
        if (p.SessionId == LocalSessionId) return true;
        return !p.IsForfeited && p.IsOnline;       // 이미 나간 사람은 기다리지 않는다
    }

    /// <summary>씬 로딩을 끝낸 사람 수 (기다릴 대상 중)</summary>
    public int SceneLoadedCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < players.Count; i++)
                if (MustWaitFor(players[i]) && sceneLoadedSessions.Contains(players[i].SessionId)) n++;
            return n;
        }
    }

    /// <summary>기다릴 총 인원</summary>
    public int SceneLoadTargetCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < players.Count; i++) if (MustWaitFor(players[i])) n++;
            return n;
        }
    }

    /// <summary>기다릴 모든 사람이 씬 로딩을 끝냈는가</summary>
    public bool AllSceneLoaded
    {
        get
        {
            for (int i = 0; i < players.Count; i++)
                if (MustWaitFor(players[i]) && !sceneLoadedSessions.Contains(players[i].SessionId)) return false;
            return true;
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재접속 상태 스냅샷

    public void RegisterStateProvider(INetStateProvider provider)
    {
        if (provider == null) return;
        if (provider.StateId == NetStateIds.Roster) { NetLog.Error("StateId 1(Roster)은 InGameNet 전용입니다."); return; }
        if (providers.ContainsKey(provider.StateId) && providers[provider.StateId] != provider)
            NetLog.Warn("StateId 중복 등록: " + provider.StateId + " (기존 것을 교체합니다)");
        providers[provider.StateId] = provider;
    }

    public void UnregisterStateProvider(INetStateProvider provider)
    {
        if (provider == null) return;
        INetStateProvider current;
        if (providers.TryGetValue(provider.StateId, out current) && current == provider) providers.Remove(provider.StateId);
    }

    /// <summary>호스트가 호출: 현재 상태를 하나의 바이트 배열로 묶는다. [버전][블록수]{[id][blob]}...</summary>
    public byte[] BuildSnapshot()
    {
        var writer = new NetWriter(256);
        writer.WriteByte(1);                                        // 스냅샷 형식 버전
        writer.WriteUShort((ushort)(providers.Count + 1));          // +1 = Roster

        WriteBlock(writer, NetStateIds.Roster, WriteRoster);
        foreach (var provider in providers.Values.ToList())
        {
            var p = provider;
            try { WriteBlock(writer, p.StateId, p.WriteState); }
            catch (Exception exception) { NetLog.Error("스냅샷 기록 실패 [StateId " + p.StateId + "]: " + exception); }
        }

        byte[] bytes = writer.ToArray();
        if (bytes.Length > SnapshotWarnBytes) NetLog.Warn("스냅샷이 큽니다: " + bytes.Length + "B (슈퍼 게이머 한도 30KB/s)");
        return bytes;
    }

    private static void WriteBlock(NetWriter writer, ushort id, Action<NetWriter> write)
    {
        var block = new NetWriter(64);
        write(block);
        writer.WriteUShort(id);
        writer.WriteBlob(block.ToArray());
    }

    /// <summary>재접속한 유저가 호출: 받은 스냅샷을 Roster와 각 Provider에 나눠 적용한다. 반드시 씬 준비 후에 호출.</summary>
    public bool ApplySnapshot(byte[] snapshot)
    {
        try
        {
            var reader = new NetReader(snapshot);
            byte version = reader.ReadByte();
            if (version != 1) { NetLog.Warn("알 수 없는 스냅샷 버전: " + version); return false; }

            int count = reader.ReadUShort();
            for (int i = 0; i < count; i++)
            {
                ushort id = reader.ReadUShort();
                byte[] blob = reader.ReadBlob();

                if (id == NetStateIds.Roster)
                {
                    ReadRoster(new NetReader(blob));
                    continue;
                }

                INetStateProvider provider;
                if (!providers.TryGetValue(id, out provider))
                {
                    NetLog.Warn("스냅샷에 있으나 등록되지 않은 StateId " + id + " → 건너뜀");
                    continue;
                }
                try { provider.ReadState(new NetReader(blob)); }
                catch (Exception exception) { NetLog.Error("상태 복구 실패 [StateId " + id + "]: " + exception); }
            }
            return true;
        }
        catch (Exception exception)
        {
            NetLog.Error("스냅샷 적용 실패: " + exception);
            return false;
        }
    }

    // Roster 블록: [버전][호스트][인원수]{[세션ID][닉네임][팀][팀내순번][캐릭터ID][온라인][이탈확정][남은유예초]}
    // 끊긴 유저는 "끊긴 시각"이 아니라 "남은 유예 시간"으로 보낸다. (기기마다 시계가 다르므로)
    private void WriteRoster(NetWriter writer)
    {
        float limit = Connection != null ? Connection.reconnectTimeLimit : 0f;

        writer.WriteByte(1);
        writer.WriteUInt(HostSessionId);
        writer.WriteUShort((ushort)players.Count);
        foreach (var p in players)
        {
            float graceLeft = p.IsOnline || p.IsForfeited ? 0f : Mathf.Max(0f, limit - (float)(NetClock.Now - p.DisconnectedAtSec));

            writer.WriteUInt(p.SessionId);
            writer.WriteString(p.Nickname);
            writer.WriteByte((byte)p.Team);
            writer.WriteByte((byte)p.TeamIndex);
            writer.WriteString(p.CharacterId);
            writer.WriteBool(p.IsOnline);
            writer.WriteBool(p.IsForfeited);
            writer.WriteFloat(graceLeft);
        }
    }

    private void ReadRoster(NetReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 1) return;

        HostSessionId = reader.ReadUInt();
        int count = reader.ReadUShort();
        for (int i = 0; i < count; i++)
        {
            uint sessionId = reader.ReadUInt();
            string nickname = reader.ReadString();
            var team = (NetTeam)reader.ReadByte();
            int teamIndex = reader.ReadByte();
            string characterId = reader.ReadString();
            bool online = reader.ReadBool();
            bool forfeited = reader.ReadBool();
            float graceLeft = reader.ReadFloat();

            var player = GetPlayer(sessionId);
            if (player == null)
            {
                player = new NetPlayer { SessionId = sessionId, IsLocal = sessionId == LocalSessionId };
                players.Add(player);
            }
            player.Nickname = nickname;
            player.Team = team;
            player.TeamIndex = teamIndex;
            player.CharacterId = string.IsNullOrEmpty(characterId) ? null : characterId;
            player.IsForfeited = forfeited;
            // 내 접속 상태는 내가 가장 잘 안다(방금 재접속했으므로 온라인). 남의 상태만 호스트 기준으로 반영.
            if (!player.IsLocal)
            {
                player.IsOnline = online;
                // 남은 유예 시간만큼만 더 기다리도록 끊긴 시각을 내 시계 기준으로 환산
                if (!online && !forfeited && Connection != null)
                    player.DisconnectedAtSec = NetClock.Now - (Connection.reconnectTimeLimit - graceLeft);
            }
        }
        PlayersChanged?.Invoke();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 앱 재시작 복귀용 저장 정보

    private void SaveActiveMatch()
    {
        try
        {
            var info = new NetActiveMatchInfo
            {
                roomToken = RoomToken,
                mode = (int)Mode,
                sceneName = SceneName,
                cardInDate = MatchCardInDate,
                savedAtUnix = (long)NetClock.Now
            };
            PlayerPrefs.SetString(ActiveMatchPrefKey, JsonUtility.ToJson(info));
            PlayerPrefs.Save();
        }
        catch (Exception exception) { NetLog.Warn("진행 중 매치 정보 저장 실패: " + exception.Message); }
    }

    public static bool TryLoadSavedMatch(out NetActiveMatchInfo info)
    {
        info = null;
        try
        {
            if (!PlayerPrefs.HasKey(ActiveMatchPrefKey)) return false;
            info = JsonUtility.FromJson<NetActiveMatchInfo>(PlayerPrefs.GetString(ActiveMatchPrefKey));
            bool valid = info != null && !string.IsNullOrEmpty(info.sceneName)
                         && NetClock.Now - info.savedAtUnix < ActiveMatchExpireSec;
            if (!valid) { ClearSavedMatch(); info = null; }
            return valid;
        }
        catch (Exception) { info = null; return false; }
    }

    public static void ClearSavedMatch()
    {
        try { PlayerPrefs.DeleteKey(ActiveMatchPrefKey); PlayerPrefs.Save(); }
        catch (Exception) { }
    }

    /// <summary>결과 처리 완료/영구 이탈 시 호출: 컨텍스트를 닫고 저장 정보를 지운다.</summary>
    public void EndMatch()
    {
        HasActiveMatch = false;
        isReconnecting = false;
        deferredCritical.Clear();
        ClearSavedMatch();
        NetLog.Info("매치 종료 처리");
    }

    #endregion
}
