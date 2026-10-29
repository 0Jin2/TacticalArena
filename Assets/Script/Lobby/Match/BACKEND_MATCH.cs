using BackEnd;
using BackEnd.Tcp;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if UNITY_ANDROID && !UNITY_EDITOR
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

public class BACKEND_MATCH : MonoBehaviour
{
    public enum TacticalMatchMode
    {
        CLASSIC,
        ARCADE,
        RANK
    }

    [Serializable]
    private class MatchCardInfo
    {
        public string inDate;
        public string title;
        public string matchTypeName;
        public string matchModeTypeName;
        public int matchHeadCount;
        public MatchType matchType;
        public MatchModeType matchModeType;
    }

    private static BACKEND_MATCH instance;
    public static BACKEND_MATCH Instance => instance;

    [Header("Match UI")]
    [SerializeField] private Button playButton;
    [SerializeField] private TMP_Text playButtonLabel;
    [Tooltip("The selected mode text must be exactly CLASSIC, ARCADE, or RANK (case-insensitive).")]
    [SerializeField] private TMP_Text selectedMatchModeText;
    [SerializeField] private GameObject matchStatusPanel;
    [SerializeField] private TMP_Text matchStatusTitleText;
    [SerializeField] private TMP_Text matchPlayerCountText;
    [SerializeField] private TMP_Text matchProgressText;

    [Header("Match Card inDate - copy from BACKND Console")]
    [SerializeField] private string classicMatchCardInDate;
    [SerializeField] private string arcadeMatchCardInDate;
    [SerializeField] private string rankMatchCardInDate;

    [Header("Random Map Scene Names - every matched client needs the same ordered list")]
    [SerializeField] private List<string> classicMapSceneNames = new List<string>();
    [SerializeField] private List<string> arcadeMapSceneNames = new List<string>();
    [SerializeField] private List<string> rankMapSceneNames = new List<string>();

    [Header("Diagnostics")]
    [SerializeField] private bool logVerboseMatchEvents = true;

    private bool isPrimaryInstance;
    private bool backendInitializationStarted;
    private bool backendInitialized;
    private bool loginInProgress;
    private bool isAuthenticated;
    private bool isMatchSearchActive;
    private bool matchServerSocketRequested;
    private bool matchServerAuthenticated;
    private bool waitingRoomCreationRequested;
    private bool waitingRoomCreated;
    private bool matchRequestSent;
    private bool cancellationRequested;
    private bool enteringGame;
    private bool gameStartHandled;
    private bool pollMatchEvents;
    private bool loginButtonListenerAdded;
    private bool playButtonListenerAdded;

    private int currentOperationId;
    private TacticalMatchMode selectedMode;
    private MatchCardInfo selectedMatchCard;
    private string matchedCardInDate;
    private MatchInGameRoomInfo matchedRoomInfo;
    private readonly HashSet<string> joinedGameSessionIds = new HashSet<string>();

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.Log("[BACKEND_MATCH] Duplicate component detected. Passing scene references to the persistent instance.");
            instance.AbsorbSceneReferences(this);
            Destroy(this);
            return;
        }

        instance = this;
        isPrimaryInstance = true;
        DontDestroyOnLoad(gameObject);
        RegisterMatchEvents();
        SetupPlayButton();
        SetMatchPanelVisible(false);
        SetPlayButtonLabel("플레이");
        UpdatePlayButtonInteractable();
        BeginBackendInitialization();
        Debug.Log("[BACKEND_MATCH] Awake completed. This component is now the persistent controller.");
    }

    private void Start()
    {
        if (!isPrimaryInstance) return;
        Debug.Log("[BACKEND_MATCH] Start completed.");
    }

    private void Update()
    {
        if (isPrimaryInstance && pollMatchEvents)
        {
            try
            {
                Backend.Match.Poll();
            }
            catch (Exception exception)
            {
                Debug.LogError("[BACKEND_MATCH] Backend.Match.Poll exception: " + exception);
            }
        }
    }

    private void OnDestroy()
    {
        if (!isPrimaryInstance) return;
        if (instance == this) instance = null;
        // Do not call Backend.Match.LeaveMatchMakingServer here. Scene transitions must not tear down
        // the persistent match connection before OnMatchInGameStart has been received.
        Debug.Log("[BACKEND_MATCH] Persistent controller destroyed.");
    }

    private void BeginBackendInitialization()
    {
        Debug.Log("[BACKEND_MATCH] 기존 뒤끝 로그인 상태 확인 시작");

        try
        {
            // Backend.InitializeAsync()를 다시 호출하지 않는다.
            // 기존 GPGS_BACKEND에서 로그인과 초기화를 완료한 상태를 확인한다.
            BackendReturnObject userInfo = Backend.BMember.GetUserInfo();

            if (userInfo != null && userInfo.IsSuccess())
            {
                backendInitialized = true;
                isAuthenticated = true;

                Debug.Log("[BACKEND_MATCH] 기존 뒤끝 로그인 상태 확인 성공");
            }
            else
            {
                backendInitialized = false;
                isAuthenticated = false;

                Debug.LogWarning(
                    "[BACKEND_MATCH] 로그인 상태 확인 실패: "
                    + (userInfo == null ? "userInfo가 null입니다." : userInfo.ToString())
                );
            }
        }
        catch (System.Exception exception)
        {
            backendInitialized = false;
            isAuthenticated = false;

            Debug.LogError(
                "[BACKEND_MATCH] 로그인 상태 확인 예외: " + exception
            );
        }

        UpdatePlayButtonInteractable();
    }

    #region Match setup and UI

    private void SetupPlayButton()
    {
        if (playButton == null || playButtonListenerAdded) return;
        playButton.onClick.RemoveListener(OnPlayButtonClicked);
        playButton.onClick.AddListener(OnPlayButtonClicked);
        playButtonListenerAdded = true;
        Debug.Log("[BACKEND_MATCH] Play / matchmaking-cancel button listener registered.");
    }

    private void UpdatePlayButtonInteractable()
    {
        if (playButton == null)
        {
            Debug.LogError("[BACKEND_MATCH] Play Button이 Inspector에 연결되지 않았습니다.");
            return;
        }

        // 로그인 확인은 기존 로그인 스크립트에서 처리한다.
        // 매칭 중에는 취소할 수 있어야 하므로 매칭 상태를 조건에 넣지 않는다.
        playButton.interactable = !enteringGame;

        Debug.Log(
            "[BACKEND_MATCH] Play Button interactable = "
            + playButton.interactable
            + ", enteringGame = " + enteringGame
        );
    }

    private void SetPlayButtonLabel(string value)
    {
        if (playButtonLabel != null) playButtonLabel.text = value;
        else if (playButton != null)
        {
            TMP_Text childText = playButton.GetComponentInChildren<TMP_Text>(true);
            if (childText != null) childText.text = value;
        }
        Debug.Log("[BACKEND_MATCH] Play button label updated: " + value);
    }

    private void SetMatchPanelVisible(bool visible)
    {
        if (matchStatusPanel != null) matchStatusPanel.SetActive(visible);
        Debug.Log("[BACKEND_MATCH] Match status panel active = " + visible);
    }

    private void SetMatchStatus(string title, string playerCount, string progress)
    {
        if (matchStatusTitleText != null) matchStatusTitleText.text = title;
        if (matchPlayerCountText != null) matchPlayerCountText.text = playerCount;
        if (matchProgressText != null) matchProgressText.text = progress;
        Debug.Log("[BACKEND_MATCH] Match UI updated | title=" + title + " | players=" + playerCount + " | progress=" + progress);
    }

    private void OnPlayButtonClicked()
    {
        Debug.Log("[BACKEND_MATCH] Main play button clicked.");
        if (isMatchSearchActive || matchServerSocketRequested || matchServerAuthenticated || waitingRoomCreated || matchRequestSent)
        {
            Debug.Log("[BACKEND_MATCH] Match flow is active. Treating button click as cancellation.");
            CancelCurrentMatchSearch();
            return;
        }

        if (!backendInitialized || !isAuthenticated)
        {
            Debug.LogWarning("[BACKEND_MATCH] Match search blocked: backend initialization or login is incomplete.");
            SetMatchStatus("로그인이 필요합니다", "-", "먼저 로그인을 완료해 주세요.");
            SetMatchPanelVisible(true);
            return;
        }

        if (!TryReadSelectedMode(out selectedMode))
        {
            SetMatchStatus("게임 모드를 확인해 주세요", "-", "모드 텍스트는 CLASSIC, ARCADE, RANK 중 하나여야 합니다.");
            SetMatchPanelVisible(true);
            return;
        }

        string configuredInDate = GetConfiguredInDate(selectedMode);
        if (string.IsNullOrWhiteSpace(configuredInDate))
        {
            Debug.LogError("[BACKEND_MATCH] The selected mode has no inDate configured in the Inspector: " + selectedMode);
            SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭", "-", "인스펙터에서 해당 모드의 inDate를 입력해 주세요.");
            SetMatchPanelVisible(true);
            return;
        }

        if (!ValidateSceneList(selectedMode))
        {
            SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭", "-", "해당 모드의 맵 씬 목록을 확인해 주세요.");
            SetMatchPanelVisible(true);
            return;
        }

        currentOperationId++;
        int thisOperation = currentOperationId;
        isMatchSearchActive = true;
        cancellationRequested = false;
        enteringGame = false;
        gameStartHandled = false;
        matchServerSocketRequested = false;
        matchServerAuthenticated = false;
        waitingRoomCreationRequested = false;
        waitingRoomCreated = false;
        matchRequestSent = false;
        selectedMatchCard = null;
        matchedRoomInfo = null;
        matchedCardInDate = null;
        joinedGameSessionIds.Clear();

        SetPlayButtonLabel("매칭취소");
        SetMatchPanelVisible(true);
        SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭", "인원 확인 중", "매치 카드 설정을 확인하는 중입니다.");
        Debug.Log("[BACKEND_MATCH] Starting match search. Mode=" + selectedMode + ", inDate=" + configuredInDate + ", operation=" + thisOperation);

        Backend.Match.GetMatchList(callback =>
        {
            Debug.Log("[BACKEND_MATCH] GetMatchList callback received: " + callback);
            if (thisOperation != currentOperationId || cancellationRequested || !isMatchSearchActive)
            {
                Debug.LogWarning("[BACKEND_MATCH] Ignoring stale GetMatchList callback because search was cancelled or replaced.");
                return;
            }

            if (!callback.IsSuccess())
            {
                FailMatchFlow("매치 카드 목록을 가져오지 못했습니다.", callback.ToString());
                return;
            }

            if (!TryFindMatchCard(callback, configuredInDate, out selectedMatchCard))
            {
                FailMatchFlow("해당 inDate의 매치 카드를 찾지 못했습니다.", "inDate=" + configuredInDate + ". 콘솔에서 값을 다시 확인해 주세요.");
                return;
            }

            Debug.Log("[BACKEND_MATCH] Matched card found. Title=" + selectedMatchCard.title +
                      ", inDate=" + selectedMatchCard.inDate +
                      ", type=" + selectedMatchCard.matchTypeName +
                      ", mode=" + selectedMatchCard.matchModeTypeName +
                      ", target players=" + selectedMatchCard.matchHeadCount);

            SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭", "대기 인원 1 / " + selectedMatchCard.matchHeadCount, "대기방 서버에 연결하는 중입니다.");
            RequestMatchMakingServerConnection();
        });
    }

    private bool TryReadSelectedMode(out TacticalMatchMode mode)
    {
        mode = TacticalMatchMode.CLASSIC;
        if (selectedMatchModeText == null)
        {
            Debug.LogError("[BACKEND_MATCH] Selected mode TMP reference is missing in the Inspector.");
            return false;
        }

        string modeText = selectedMatchModeText.text.Trim().ToUpperInvariant();
        Debug.Log("[BACKEND_MATCH] Reading selected mode from TMP text: '" + modeText + "'.");
        switch (modeText)
        {
            case "CLASSIC": mode = TacticalMatchMode.CLASSIC; return true;
            case "ARCADE": mode = TacticalMatchMode.ARCADE; return true;
            case "RANK": mode = TacticalMatchMode.RANK; return true;
            default:
                Debug.LogError("[BACKEND_MATCH] Unsupported selected mode text: " + modeText);
                return false;
        }
    }

    private string GetConfiguredInDate(TacticalMatchMode mode)
    {
        switch (mode)
        {
            case TacticalMatchMode.CLASSIC: return classicMatchCardInDate;
            case TacticalMatchMode.ARCADE: return arcadeMatchCardInDate;
            case TacticalMatchMode.RANK: return rankMatchCardInDate;
            default: return string.Empty;
        }
    }

    private string GetModeDisplayName(TacticalMatchMode mode)
    {
        switch (mode)
        {
            case TacticalMatchMode.CLASSIC: return "클래식";
            case TacticalMatchMode.ARCADE: return "아케이드";
            case TacticalMatchMode.RANK: return "랭크";
            default: return "알 수 없는 모드";
        }
    }

    private List<string> GetSceneList(TacticalMatchMode mode)
    {
        switch (mode)
        {
            case TacticalMatchMode.CLASSIC: return classicMapSceneNames;
            case TacticalMatchMode.ARCADE: return arcadeMapSceneNames;
            case TacticalMatchMode.RANK: return rankMapSceneNames;
            default: return null;
        }
    }

    private bool ValidateSceneList(TacticalMatchMode mode)
    {
        List<string> scenes = GetSceneList(mode);
        if (scenes == null || scenes.Count == 0)
        {
            Debug.LogError("[BACKEND_MATCH] No scene names configured for mode: " + mode);
            return false;
        }

        for (int i = 0; i < scenes.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(scenes[i]))
            {
                Debug.LogError("[BACKEND_MATCH] Empty scene name at index " + i + " for mode " + mode);
                return false;
            }
            if (!Application.CanStreamedLevelBeLoaded(scenes[i]))
            {
                Debug.LogError("[BACKEND_MATCH] Scene is not in Build Settings / Build Profiles: " + scenes[i]);
                return false;
            }
        }
        Debug.Log("[BACKEND_MATCH] Scene list validated for " + mode + ". Count=" + scenes.Count);
        return true;
    }

    private bool TryFindMatchCard(BackendReturnObject callback, string targetInDate, out MatchCardInfo card)
    {
        card = null;
        try
        {
            LitJson.JsonData rows = callback.FlattenRows();
            Debug.Log("[BACKEND_MATCH] GetMatchList returned " + (rows == null ? 0 : rows.Count) + " flattened card(s).");
            if (rows == null || !rows.IsArray) return false;

            for (int index = 0; index < rows.Count; index++)
            {
                LitJson.JsonData row = rows[index];
                string inDate = ReadJsonField(row, "inDate");
                if (!string.Equals(inDate, targetInDate.Trim(), StringComparison.Ordinal)) continue;

                string matchTypeText = ReadJsonField(row, "matchType");
                string matchModeText = ReadJsonField(row, "matchModeType");
                string headCountText = ReadJsonField(row, "matchHeadCount");
                int headCount;
                if (!int.TryParse(headCountText, out headCount) || headCount < 1)
                {
                    Debug.LogError("[BACKEND_MATCH] Invalid matchHeadCount in card " + inDate + ": " + headCountText);
                    return false;
                }

                MatchType parsedMatchType;
                MatchModeType parsedModeType;
                if (!Enum.TryParse(matchTypeText, true, out parsedMatchType))
                {
                    Debug.LogError("[BACKEND_MATCH] Unknown MatchType in match card: " + matchTypeText);
                    return false;
                }
                if (!Enum.TryParse(matchModeText, true, out parsedModeType))
                {
                    Debug.LogError("[BACKEND_MATCH] Unknown MatchModeType in match card: " + matchModeText);
                    return false;
                }

                card = new MatchCardInfo
                {
                    inDate = inDate,
                    title = ReadJsonField(row, "matchTitle"),
                    matchTypeName = matchTypeText,
                    matchModeTypeName = matchModeText,
                    matchHeadCount = headCount,
                    matchType = parsedMatchType,
                    matchModeType = parsedModeType
                };
                return true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[BACKEND_MATCH] Error parsing GetMatchList: " + exception);
        }
        return false;
    }

    private string ReadJsonField(LitJson.JsonData row, string key)
    {
        if (row == null || !row.IsObject || !row.ContainsKey(key)) return string.Empty;
        LitJson.JsonData value = row[key];
        if (value == null) return string.Empty;

        // FlattenRows normally returns simple values, but also tolerate the wrapped Dynamo-style format.
        if (value.IsObject)
        {
            if (value.ContainsKey("S")) return value["S"].ToString();
            if (value.ContainsKey("N")) return value["N"].ToString();
            if (value.ContainsKey("BOOL")) return value["BOOL"].ToString();
        }
        return value.ToString();
    }

    #endregion

    #region Matchmaking server events

    private void RegisterMatchEvents()
    {
        Backend.Match.OnJoinMatchMakingServer -= HandleJoinMatchMakingServer;
        Backend.Match.OnJoinMatchMakingServer += HandleJoinMatchMakingServer;
        Backend.Match.OnMatchMakingRoomCreate -= HandleMatchMakingRoomCreate;
        Backend.Match.OnMatchMakingRoomCreate += HandleMatchMakingRoomCreate;
        Backend.Match.OnMatchMakingResponse -= HandleMatchMakingResponse;
        Backend.Match.OnMatchMakingResponse += HandleMatchMakingResponse;
        Backend.Match.OnSessionJoinInServer -= HandleSessionJoinInGameServer;
        Backend.Match.OnSessionJoinInServer += HandleSessionJoinInGameServer;
        Backend.Match.OnSessionListInServer -= HandleSessionListInGameServer;
        Backend.Match.OnSessionListInServer += HandleSessionListInGameServer;
        Backend.Match.OnMatchInGameAccess -= HandleMatchInGameAccess;
        Backend.Match.OnMatchInGameAccess += HandleMatchInGameAccess;
        Backend.Match.OnMatchInGameStart -= HandleMatchInGameStart;
        Backend.Match.OnMatchInGameStart += HandleMatchInGameStart;
        Debug.Log("[BACKEND_MATCH] BACKND Match events registered.");
    }

    private void RequestMatchMakingServerConnection()
    {
        if (cancellationRequested || !isMatchSearchActive)
        {
            Debug.LogWarning("[BACKEND_MATCH] Skipping match-server connection because cancellation was requested.");
            FinishCancellationLocally();
            return;
        }

        Debug.Log("[BACKEND_MATCH] Calling Backend.Match.JoinMatchMakingServer().");
        ErrorInfo errorInfo;
        bool socketRequestAccepted = Backend.Match.JoinMatchMakingServer(out errorInfo);
        if (!socketRequestAccepted)
        {
            Debug.LogError("[BACKEND_MATCH] JoinMatchMakingServer request failed: " + errorInfo.Reason + " / " + errorInfo.Category);
            FailMatchFlow("매칭 서버 연결에 실패했습니다.", errorInfo.Reason);
            return;
        }

        matchServerSocketRequested = true;
        pollMatchEvents = true;
        Debug.Log("[BACKEND_MATCH] Match-server socket accepted. Waiting for OnJoinMatchMakingServer auth event. " + errorInfo.Reason);
    }

    private void HandleJoinMatchMakingServer(JoinChannelEventArgs args)
    {
        Debug.Log("[BACKEND_MATCH] OnJoinMatchMakingServer event received: " + args.ErrInfo.Reason + " / " + args.ErrInfo.Category);
        matchServerSocketRequested = false;
        if (args.ErrInfo.Category != ErrorCode.Success)
        {
            FailMatchFlow("매칭 서버 인증에 실패했습니다.", args.ErrInfo.Reason);
            return;
        }

        matchServerAuthenticated = true;
        if (cancellationRequested || !isMatchSearchActive)
        {
            Debug.Log("[BACKEND_MATCH] Match server connected after cancellation. Disconnecting cleanly.");
            LeaveMatchMakingServerAndReset();
            return;
        }

        if (waitingRoomCreationRequested || waitingRoomCreated)
        {
            Debug.LogWarning("[BACKEND_MATCH] Waiting-room creation is already underway; duplicate request ignored.");
            return;
        }

        waitingRoomCreationRequested = true;
        SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭", "대기 인원 1 / " + selectedMatchCard.matchHeadCount, "대기방을 생성하는 중입니다.");
        Debug.Log("[BACKEND_MATCH] Calling Backend.Match.CreateMatchRoom().");
        Backend.Match.CreateMatchRoom();
    }

    private void HandleMatchMakingRoomCreate(MatchMakingInteractionEventArgs args)
    {
        Debug.Log("[BACKEND_MATCH] OnMatchMakingRoomCreate event received: " + args.ErrInfo);
        waitingRoomCreationRequested = false;
        if (args.ErrInfo != ErrorCode.Success)
        {
            FailMatchFlow("대기방 생성에 실패했습니다.", args.ErrInfo.ToString());
            return;
        }

        waitingRoomCreated = true;
        if (cancellationRequested || !isMatchSearchActive)
        {
            Debug.Log("[BACKEND_MATCH] Waiting room was created after cancellation. Leaving immediately.");
            LeaveWaitingRoomThenServer();
            return;
        }

        if (selectedMatchCard == null)
        {
            FailMatchFlow("매치 카드 정보가 없습니다.", "대기방은 생성됐지만 선택된 카드 데이터가 null입니다.");
            return;
        }

        matchRequestSent = true;
        SetPlayButtonLabel("매칭취소");
        SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭", "대기 인원 1 / " + selectedMatchCard.matchHeadCount, "대기 중인 상대를 찾고 있습니다.");
        Debug.Log("[BACKEND_MATCH] Calling RequestMatchMaking. Type=" + selectedMatchCard.matchType +
                  ", ModeType=" + selectedMatchCard.matchModeType + ", inDate=" + selectedMatchCard.inDate);
        Backend.Match.RequestMatchMaking(selectedMatchCard.matchType, selectedMatchCard.matchModeType, selectedMatchCard.inDate);
    }

    private void HandleMatchMakingResponse(MatchMakingResponseEventArgs args)
    {
        Debug.Log("[BACKEND_MATCH] OnMatchMakingResponse event received. ErrInfo=" + args.ErrInfo +
                  ", Reason=" + args.Reason + ", MatchCardIndate=" + args.MatchCardIndate);

        if (args.ErrInfo == ErrorCode.Match_InProgress)
        {
            Debug.Log("[BACKEND_MATCH] Matchmaking request accepted. The server is searching for opponents.");
            if (!cancellationRequested && selectedMatchCard != null)
            {
                SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭", "대기 인원 1 / " + selectedMatchCard.matchHeadCount,
                    "상대 플레이어를 찾고 있습니다. 매칭을 중단하려면 버튼을 누르세요.");
            }
            return;
        }

        if (args.ErrInfo == ErrorCode.Match_MatchMakingCanceled)
        {
            Debug.Log("[BACKEND_MATCH] Matchmaking cancellation confirmed by server.");
            matchRequestSent = false;
            waitingRoomCreated = false;
            LeaveMatchMakingServerAndReset();
            return;
        }

        if (args.ErrInfo != ErrorCode.Success)
        {
            FailMatchFlow("매칭 처리 중 오류가 발생했습니다.", args.Reason + " / " + args.ErrInfo);
            return;
        }

        // Match success wins a race against a cancellation request: the server has already matched us.
        cancellationRequested = false;
        isMatchSearchActive = true;
        matchRequestSent = false;
        enteringGame = true;
        matchedCardInDate = string.IsNullOrEmpty(args.MatchCardIndate) ? selectedMatchCard.inDate : args.MatchCardIndate;
        matchedRoomInfo = args.RoomInfo;
        if (selectedMatchCard != null)
        {
            SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭 완료",
                selectedMatchCard.matchHeadCount + " / " + selectedMatchCard.matchHeadCount,
                "게임 서버에 접속하고 있습니다.");
        }
        SetPlayButtonLabel("방 입장 중...");
        if (playButton != null) playButton.interactable = false;
        Debug.Log("[BACKEND_MATCH] Match established. Shared inDate=" + matchedCardInDate +
                  ", room token=" + (matchedRoomInfo == null ? "null" : matchedRoomInfo.m_inGameRoomToken));

        if (matchedRoomInfo == null || matchedRoomInfo.m_inGameServerEndPoint == null)
        {
            FailMatchFlow("매칭은 성사됐지만 인게임 서버 정보가 없습니다.", "RoomInfo 또는 Endpoint가 null입니다.");
            return;
        }

        // Once the match is established, leave the matchmaking room before entering the in-game server.
        try
        {
            if (waitingRoomCreated)
            {
                Debug.Log("[BACKEND_MATCH] Leaving the matchmaking waiting room after match success.");
                Backend.Match.LeaveMatchRoom();
                waitingRoomCreated = false;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[BACKEND_MATCH] LeaveMatchRoom warning after match success: " + exception.Message);
        }

        ConnectToInGameServer();
    }

    private void ConnectToInGameServer()
    {
        if (matchedRoomInfo == null || matchedRoomInfo.m_inGameServerEndPoint == null)
        {
            FailMatchFlow("인게임 서버 정보가 없습니다.", "매칭 응답의 RoomInfo를 확인해 주세요.");
            return;
        }

        var endpoint = matchedRoomInfo.m_inGameServerEndPoint;
        Debug.Log("[BACKEND_MATCH] Calling JoinGameServer. Address=" + endpoint.m_address + ", Port=" + endpoint.m_port);
        ErrorInfo errorInfo;
        bool socketAccepted = Backend.Match.JoinGameServer(endpoint.m_address, endpoint.m_port, false, out errorInfo);
        if (!socketAccepted)
        {
            Debug.LogError("[BACKEND_MATCH] JoinGameServer request failed: " + errorInfo.Reason + " / " + errorInfo.Category);
            FailMatchFlow("게임 서버 연결 요청에 실패했습니다.", errorInfo.Reason);
            return;
        }

        pollMatchEvents = true;
        Debug.Log("[BACKEND_MATCH] In-game server socket accepted; waiting for OnSessionJoinInServer.");
    }

    private void HandleSessionJoinInGameServer(JoinChannelEventArgs args)
    {
        Debug.Log("[BACKEND_MATCH] OnSessionJoinInServer event received: " + args.ErrInfo.Reason + " / " + args.ErrInfo.Category);
        if (args.ErrInfo.Category != ErrorCode.Success)
        {
            FailMatchFlow("게임 서버 인증에 실패했습니다.", args.ErrInfo.Reason);
            return;
        }

        if (matchedRoomInfo == null || string.IsNullOrEmpty(matchedRoomInfo.m_inGameRoomToken))
        {
            FailMatchFlow("게임방 토큰이 없습니다.", "OnSessionJoinInServer 성공 후 RoomToken을 확인할 수 없습니다.");
            return;
        }

        Debug.Log("[BACKEND_MATCH] In-game server authenticated. Calling JoinGameRoom with server-issued room token.");
        Backend.Match.JoinGameRoom(matchedRoomInfo.m_inGameRoomToken);
    }

    private void HandleSessionListInGameServer(MatchInGameSessionListEventArgs args)
    {
        Debug.Log("[BACKEND_MATCH] OnSessionListInServer event received: " + args.ErrInfo);
        if (args.ErrInfo != ErrorCode.Success)
        {
            FailMatchFlow("게임방 접속 후 유저 목록을 받지 못했습니다.", args.ErrInfo.ToString());
            return;
        }

        joinedGameSessionIds.Clear();
        if (args.GameRecords != null)
        {
            foreach (MatchUserGameRecord record in args.GameRecords)
            {
                if (record == null || record.m_sessionId == null) continue;
                joinedGameSessionIds.Add(record.m_sessionId.ToString());
            }
        }
        UpdateInGamePlayerCount("게임방에 접속했습니다. 다른 참가자의 접속을 기다립니다.");
        Debug.Log("[BACKEND_MATCH] Initial in-game session list received. Connected players=" + joinedGameSessionIds.Count);
    }

    private void HandleMatchInGameAccess(MatchInGameSessionEventArgs args)
    {
        Debug.Log("[BACKEND_MATCH] OnMatchInGameAccess event received: " + args.ErrInfo);
        if (args.ErrInfo != ErrorCode.Success || args.GameRecord == null || args.GameRecord.m_sessionId == null)
        {
            Debug.LogWarning("[BACKEND_MATCH] Ignoring incomplete in-game access event.");
            return;
        }

        string sessionId = args.GameRecord.m_sessionId.ToString();
        bool added = joinedGameSessionIds.Add(sessionId);
        if (added)
        {
            Debug.Log("[BACKEND_MATCH] Player connected to the game room. Session=" + sessionId + ", count=" + joinedGameSessionIds.Count);
            UpdateInGamePlayerCount("참가 플레이어가 게임방에 접속했습니다.");
        }
        else
        {
            Debug.Log("[BACKEND_MATCH] Duplicate game-room access event ignored for session=" + sessionId);
        }
    }

    private void UpdateInGamePlayerCount(string progressMessage)
    {
        int targetCount = selectedMatchCard != null ? selectedMatchCard.matchHeadCount : 2;
        SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭 완료", Mathf.Min(joinedGameSessionIds.Count, targetCount) + " / " + targetCount, progressMessage);
    }

    private void HandleMatchInGameStart()
    {
        Debug.Log("[BACKEND_MATCH] OnMatchInGameStart event received from BACKND.");
        if (gameStartHandled)
        {
            Debug.LogWarning("[BACKEND_MATCH] Duplicate OnMatchInGameStart event ignored.");
            return;
        }
        gameStartHandled = true;

        List<string> sceneNames = GetSceneList(selectedMode);
        if (sceneNames == null || sceneNames.Count == 0)
        {
            FailMatchFlow("게임 시작 이벤트를 받았지만 맵 목록이 비어 있습니다.", "모드=" + selectedMode);
            return;
        }

        string roomToken = matchedRoomInfo != null ? matchedRoomInfo.m_inGameRoomToken : string.Empty;
        int sceneIndex = GetStableSceneIndex(roomToken + "|" + matchedCardInDate + "|" + selectedMode, sceneNames.Count);
        string chosenScene = sceneNames[sceneIndex];
        if (!Application.CanStreamedLevelBeLoaded(chosenScene))
        {
            FailMatchFlow("선택한 맵 씬을 불러올 수 없습니다.", chosenScene + " 씬을 Build Settings / Build Profiles에 추가해 주세요.");
            return;
        }

        isMatchSearchActive = false;
        enteringGame = true;
        SetPlayButtonLabel("게임 입장 중...");
        SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭 완료", selectedMatchCard.matchHeadCount + " / " + selectedMatchCard.matchHeadCount,
            "모든 참가자의 준비가 완료되었습니다. 맵으로 이동합니다.");
        Debug.Log("[BACKEND_MATCH] Loading shared deterministic map. RoomToken=" + roomToken +
                  ", inDate=" + matchedCardInDate + ", mode=" + selectedMode + ", index=" + sceneIndex + ", scene=" + chosenScene);
        SceneManager.LoadScene(chosenScene);
    }

    #endregion

    #region Cancellation and errors

    private void CancelCurrentMatchSearch()
    {
        if (!isMatchSearchActive && !matchServerSocketRequested && !matchServerAuthenticated && !waitingRoomCreated && !matchRequestSent)
        {
            Debug.LogWarning("[BACKEND_MATCH] Cancel requested, but there is no active match search.");
            return;
        }

        Debug.Log("[BACKEND_MATCH] Cancellation requested by the player.");
        cancellationRequested = true;
        currentOperationId++;
        SetMatchStatus(GetModeDisplayName(selectedMode) + " 매칭 취소", "-", "매칭 취소 요청을 처리하고 있습니다.");

        if (matchRequestSent)
        {
            Debug.Log("[BACKEND_MATCH] Calling Backend.Match.CancelMatchMaking().");
            Backend.Match.CancelMatchMaking();
            // Keep polling until OnMatchMakingResponse returns Match_MatchMakingCanceled.
            return;
        }

        if (waitingRoomCreated)
        {
            LeaveWaitingRoomThenServer();
            return;
        }

        if (matchServerAuthenticated)
        {
            LeaveMatchMakingServerAndReset();
            return;
        }

        if (!matchServerSocketRequested)
        {
            FinishCancellationLocally();
        }
        else
        {
            Debug.Log("[BACKEND_MATCH] Waiting for connection event so the pending server connection can be closed safely.");
        }
    }

    private void LeaveWaitingRoomThenServer()
    {
        try
        {
            if (waitingRoomCreated)
            {
                Debug.Log("[BACKEND_MATCH] Leaving matchmaking waiting room.");
                Backend.Match.LeaveMatchRoom();
                waitingRoomCreated = false;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[BACKEND_MATCH] LeaveMatchRoom exception: " + exception);
        }
        LeaveMatchMakingServerAndReset();
    }

    private void LeaveMatchMakingServerAndReset()
    {
        try
        {
            if (matchServerSocketRequested || matchServerAuthenticated)
            {
                Debug.Log("[BACKEND_MATCH] Calling Backend.Match.LeaveMatchMakingServer().");
                Backend.Match.LeaveMatchMakingServer();
            }
        }
        catch (Exception exception)
        {
            Debug.LogError("[BACKEND_MATCH] LeaveMatchMakingServer exception: " + exception);
        }

        FinishCancellationLocally();
    }

    private void FinishCancellationLocally()
    {
        Debug.Log("[BACKEND_MATCH] Match search state reset after cancellation.");
        isMatchSearchActive = false;
        matchServerSocketRequested = false;
        matchServerAuthenticated = false;
        waitingRoomCreationRequested = false;
        waitingRoomCreated = false;
        matchRequestSent = false;
        cancellationRequested = false;
        enteringGame = false;
        selectedMatchCard = null;
        matchedRoomInfo = null;
        matchedCardInDate = null;
        SetPlayButtonLabel("플레이");
        SetMatchPanelVisible(false);
        UpdatePlayButtonInteractable();
    }

    private void FailMatchFlow(string userMessage, string detail)
    {
        Debug.LogError("[BACKEND_MATCH] Match flow failed: " + userMessage + " | Detail: " + detail);
        isMatchSearchActive = false;
        cancellationRequested = true;
        matchRequestSent = false;
        try
        {
            if (waitingRoomCreated)
            {
                Backend.Match.LeaveMatchRoom();
                waitingRoomCreated = false;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[BACKEND_MATCH] Cleanup LeaveMatchRoom exception: " + exception.Message);
        }
        try
        {
            if (matchServerSocketRequested || matchServerAuthenticated)
                Backend.Match.LeaveMatchMakingServer();
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[BACKEND_MATCH] Cleanup LeaveMatchMakingServer exception: " + exception.Message);
        }

        matchServerSocketRequested = false;
        matchServerAuthenticated = false;
        waitingRoomCreationRequested = false;
        waitingRoomCreated = false;
        enteringGame = false;
        cancellationRequested = false;
        SetPlayButtonLabel("플레이");
        SetMatchStatus("매칭 오류", "-", userMessage + "\n" + detail);
        SetMatchPanelVisible(true);
        UpdatePlayButtonInteractable();
    }

    #endregion

    #region Shared scene selection

    private int GetStableSceneIndex(string sharedKey, int sceneCount)
    {
        if (sceneCount <= 1) return 0;

        // Stable FNV-1a hash: unlike string.GetHashCode(), this produces the same result on every client.
        unchecked
        {
            uint hash = 2166136261;
            string value = string.IsNullOrEmpty(sharedKey) ? "TacticalArenaFallback" : sharedKey;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619;
            }
            return (int)(hash % (uint)sceneCount);
        }
    }

    #endregion

    #region Scene reference transfer

    private void AbsorbSceneReferences(BACKEND_MATCH sceneComponent)
    {
        if (sceneComponent == null) return;
        Debug.Log("[BACKEND_MATCH] Absorbing Inspector references from scene component: " + sceneComponent.gameObject.name);

        if (sceneComponent.playButton != null) playButton = sceneComponent.playButton;
        if (sceneComponent.playButtonLabel != null) playButtonLabel = sceneComponent.playButtonLabel;
        if (sceneComponent.selectedMatchModeText != null) selectedMatchModeText = sceneComponent.selectedMatchModeText;
        if (sceneComponent.matchStatusPanel != null) matchStatusPanel = sceneComponent.matchStatusPanel;
        if (sceneComponent.matchStatusTitleText != null) matchStatusTitleText = sceneComponent.matchStatusTitleText;
        if (sceneComponent.matchPlayerCountText != null) matchPlayerCountText = sceneComponent.matchPlayerCountText;
        if (sceneComponent.matchProgressText != null) matchProgressText = sceneComponent.matchProgressText;

        if (!string.IsNullOrWhiteSpace(sceneComponent.classicMatchCardInDate)) classicMatchCardInDate = sceneComponent.classicMatchCardInDate;
        if (!string.IsNullOrWhiteSpace(sceneComponent.arcadeMatchCardInDate)) arcadeMatchCardInDate = sceneComponent.arcadeMatchCardInDate;
        if (!string.IsNullOrWhiteSpace(sceneComponent.rankMatchCardInDate)) rankMatchCardInDate = sceneComponent.rankMatchCardInDate;
        if (sceneComponent.classicMapSceneNames != null && sceneComponent.classicMapSceneNames.Count > 0) classicMapSceneNames = new List<string>(sceneComponent.classicMapSceneNames);
        if (sceneComponent.arcadeMapSceneNames != null && sceneComponent.arcadeMapSceneNames.Count > 0) arcadeMapSceneNames = new List<string>(sceneComponent.arcadeMapSceneNames);
        if (sceneComponent.rankMapSceneNames != null && sceneComponent.rankMapSceneNames.Count > 0) rankMapSceneNames = new List<string>(sceneComponent.rankMapSceneNames);

        if (sceneComponent.playButton != null) playButtonListenerAdded = false;
        SetupPlayButton();
        UpdatePlayButtonInteractable();
        Debug.Log("[BACKEND_MATCH] Scene UI references transferred successfully.");
    }

    #endregion
}
