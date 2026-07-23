using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;
using System.Linq;
using UnityEngine.SceneManagement;

public class CharacterSelectionManager : MonoBehaviour
{
    public static CharacterSelectionManager Instance { get; private set; }

    [Header("UI")]
    public GameObject selectionPanel;
    public TMP_Text countdownText;
    public TMP_Text myTeamText;
    public Button readyButton;
    public Transform redTeamPanel, blueTeamPanel;
    public GameObject redTeamItemPrefab;
    public GameObject blueTeamItemPrefab;
    public GameObject myTeamItemPrefab;
    public CharacterButton[] characterButtons;

    private NetworkRunner runner;
    private FusionTimerData timer;
    private bool isFinalCountdownStarted = false;
    private bool isPreDelayStarted = false;
    private bool isFinalCountdownRunning = false;
    private bool observedFinalStarted = false;
    private bool localReadyOverride = false;
    private Coroutine debugTimerCoroutine;
    private Coroutine heartbeatCoroutine;

    private readonly Dictionary<PlayerRef, GameObject> spawnedItems = new(); // 각 유저별 아이템 스폰 여부
    private readonly Dictionary<PlayerRef, bool> playerDisconnected = new(); // 각 유저 접속 여부
    private Dictionary<PlayerRef, bool> lastDisconnectState = new Dictionary<PlayerRef, bool>();

    private void OnEnable()
    {
        var myNetworkData = CharacterSelectionNetworkData.Local;

        if (myNetworkData != null && myNetworkData.Object != null)
        {
            myNetworkData.OnCharacterSelected += HandleCharacterSelected;
        }

        if (TeamAssigner.Instance != null)
        {
            TeamAssigner.Instance.OnNicknameUpdated += HandleNicknameUpdated;

            // ✅ 팀 배정 완료 콜백에도 연결해 두면, 늦게 팀이 정해져도 즉시 패널 갱신 가능
            TeamAssigner.Instance.OnPlayerTeamAssigned -= OnPlayerTeamAssigned_Internal;
            TeamAssigner.Instance.OnPlayerTeamAssigned += OnPlayerTeamAssigned_Internal;
        }

        RegisterRunnerCallbacks();
    }

    private void OnPlayerTeamAssigned_Internal(PlayerRef player)
    {
        if (runner == null) runner = FindObjectOfType<NetworkRunner>();
        if (runner == null) return;

        if (player == runner.LocalPlayer)
        {
            // 내 팀이 방금 확정되었으니 가시성 반영
            ApplyMyTeamVisibility();
        }
    }

    private void OnDisable()
    {
        if (TeamAssigner.Instance != null)
        {
            TeamAssigner.Instance.OnNicknameUpdated -= HandleNicknameUpdated;
        }
        if (debugTimerCoroutine != null)
        {
            StopCoroutine(debugTimerCoroutine);
            debugTimerCoroutine = null;
        }

        UnregisterRunnerCallbacks();
    }

    private void RegisterRunnerCallbacks()
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null)
        {
            var callback = runner.GetComponent<MatchingRunnerCallback>();
            if (callback != null)
                callback.OnPlayerConnectionChanged += OnPlayerConnectionChanged;
        }
    }

    private void UnregisterRunnerCallbacks()
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null)
        {
            var callback = runner.GetComponent<MatchingRunnerCallback>();
            if (callback != null)
                callback.OnPlayerConnectionChanged -= OnPlayerConnectionChanged;
        }
    }

    // 연결/해제 콜백
    public void OnPlayerConnectionChanged(PlayerRef player, bool isConnected)
    {
        playerDisconnected[player] = !isConnected;

        // UI 동기화
        if (spawnedItems.TryGetValue(player, out var obj))
        {
            var teamMemberItem = obj.GetComponent<TeamMemberItem>();
            if (teamMemberItem != null)
                teamMemberItem.SetDisconnected(!isConnected);
        }
    }

    private void Awake()
    {
        Instance = this;
    }

    private IEnumerator Start()
    {
        runner = FindObjectOfType<NetworkRunner>();
        if (runner == null)
        {
            Debug.LogError("❌ NetworkRunner를 찾을 수 없습니다.");
            yield break;
        }

        // 1) 팀/닉네임 준비는 “병렬”로 진행 → 타이머/UI 초기화가 막히지 않음
        StartCoroutine(WaitForTeamAssigner());
        // 2) 선택 네트워크 데이터 준비(이 메서드 내부도 블로킹 제거됨)
        yield return StartCoroutine(WaitForCharacterSelectionNetworkData());

        // 3) 타이머 바인딩
        while ((timer = GetAvailableTimer()) == null)
        {
            Debug.Log("[CSM] 아직 타이머 복제 안 됨, 대기 중…");
            yield return null;
        }
        Debug.Log("[CSM] timer 할당 완료");

        // 3) 서버 상태 기반 UI 초기화
        SyncUIFromServerState();

        // 4) 이벤트 재등록(준비/선택)
        CharacterSelectionNetworkData.Instance.OnReadyToggled += HandleReadyStateChanged;
        TeamAssigner.Instance.OnNicknameUpdated -= HandleNicknameUpdated;
        TeamAssigner.Instance.OnNicknameUpdated += HandleNicknameUpdated;

        if (debugTimerCoroutine == null)
            debugTimerCoroutine = StartCoroutine(DebugLogRemainingTimesCoroutine());

        foreach (var kvp in CharacterSelectionNetworkData.Instance.ReadyStates)
            HandleReadyStateChanged(kvp.Key, kvp.Value);

        // 5) 하트비트 송신 시작
        heartbeatCoroutine = StartCoroutine(HeartbeatSender());

        // ✅ 6) <<여기 추가>> 팀 정보가 준비됐으니 “내 팀만 보이기” 적용
        ApplyMyTeamVisibility();

        // ✅ 튜토리얼 미완료 상태로 연습방(Tutorial) 입장 시: 카운트다운/팀텍스트 비활성
        //    (필드 추가 없이, 기존 로직 영향 최소화)
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Tutorial" &&
            UserInfoManager.Instance != null &&
            !UserInfoManager.Instance.IsTutorialCompleted)
        {
            if (countdownText != null) countdownText.gameObject.SetActive(false);
            if (myTeamText != null) myTeamText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (timer == null || runner == null || selectionPanel == null || countdownText == null || readyButton == null || characterButtons == null || CharacterSelectionNetworkData.Instance == null)
            return;

        // ✅ 튜토리얼 미완료 + 연습방(Tutorial) 전용 처리
        // - countdownText / myTeamText 비활성 유지
        // - Ready 클릭 시 즉시 selectionPanel 비활성
        // - 타이머(카운트다운) 기반 UI 잠금/패널 토글 로직을 적용하지 않음
        bool isTutorialPractice = (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Tutorial" &&
                                  UserInfoManager.Instance != null &&
                                  !UserInfoManager.Instance.IsTutorialCompleted);

        if (timer.Runner == null || !timer.Runner.IsRunning)
        {
            timer = GetAvailableTimer();
            if (timer == null)
            {
                countdownText.text = "타이머 없음";
                return;
            }
        }

        // 내 Ready 상태 조회
        bool myReady = false;
        CharacterSelectionNetworkData.Instance.ReadyStates.TryGet(runner.LocalPlayer, out myReady);
        if (localReadyOverride) myReady = true;

        // 현재 내가 서버에 반영된 선택
        string mySelectedId = null;
        CharacterSelectionNetworkData.Instance.SelectedCharacters.TryGet(runner.LocalPlayer, out mySelectedId);

        if (isTutorialPractice)
        {
            // 텍스트 비활성 유지(외부에서 켜져도 다시 꺼짐)
            if (countdownText != null && countdownText.gameObject.activeSelf) countdownText.gameObject.SetActive(false);
            if (myTeamText != null && myTeamText.gameObject.activeSelf) myTeamText.gameObject.SetActive(false);

            // 연습방(튜토리얼 미완료): Ready 여부만으로 패널/버튼 제어
            readyButton.interactable = !myReady;

            foreach (var btn in characterButtons)
            {
                if (btn == null || btn.button == null || btn.characterInfo == null) continue;

                bool isMyPickedNet = (!string.IsNullOrEmpty(mySelectedId) && btn.characterInfo.characterId == mySelectedId);
                btn.button.interactable = !isMyPickedNet && !myReady;
            }

            selectionPanel.SetActive(!myReady);

            // 연결상태(모바일 고려) 점검
            CheckHeartbeatStatus();
            return;
        }

        float remainingSelection = timer.GetRemainingSelectionTime();
        float remainingFinal = timer.GetRemainingFinalCountdown();
        bool isFinal = timer.IsFinalCountdown;

        // ✅ 공통 잠금 조건(파이널 or 1초 이하 or 파이널 전환 처리 시작)
        bool finalLock = isFinal || isFinalCountdownStarted || (remainingSelection <= 1f);

        // 마지막 1초 남으면 Ready 비활성(이후 어떤 콜백도 다시 켜지지 않게 finalLock으로 통일)
        readyButton.interactable = !finalLock;

        // 선택 종료 시점(호스트만 자동 선택/준비)
        if (!isFinalCountdownStarted && remainingSelection <= 0f)
        {
            isFinalCountdownStarted = true;   // 이후 모든 UI는 finalLock으로 잠금
            AutoSelectAndReady();
        }

        foreach (var btn in characterButtons)
        {
            if (btn == null || btn.button == null || btn.characterInfo == null) continue;

            bool isMyPickedNet = (!string.IsNullOrEmpty(mySelectedId) && btn.characterInfo.characterId == mySelectedId);
            bool isMyReady = myReady || localReadyOverride;

            // ✅ 캐릭터 잠금 상태 먼저 체크
            bool isLocked = UserCharacterDataManager.Instance.IsCharacterLocked(btn.characterInfo.characterId);
            if (isLocked)
            {
                btn.button.interactable = false;
                continue;
            }

            // 버튼 잠금 조건: 내가 이 버튼을 선택했거나, 파이널 락이거나, 내가 준비 상태일 때
            btn.button.interactable = !isMyPickedNet && !finalLock && !isMyReady;
        }

        // 패널 표시
        bool showPanel = !isFinal || (remainingFinal > 0f);
        selectionPanel.SetActive(showPanel);

        UpdateTimerText(remainingSelection, remainingFinal);

        // 연결상태(모바일 고려) 점검
        CheckHeartbeatStatus();
    }

    /// <summary>
    /// 내 팀 패널만 보이도록 토글한다.
    /// TeamAssigner, runner, 로컬플레이어가 모두 유효할 때만 동작.
    /// </summary>
    private void ApplyMyTeamVisibility()
    {
        if (runner == null || TeamAssigner.Instance == null) return;
        var me = runner.LocalPlayer;
        if (!me.IsValid) return;
        if (!TeamAssigner.Instance.HasTeam(me)) return;

        var myTeam = TeamAssigner.Instance.GetTeam(me);
        bool showRed = (myTeam == TeamAssigner.Team.Red);
        bool showBlue = (myTeam == TeamAssigner.Team.Blue);

        if (redTeamPanel != null) redTeamPanel.gameObject.SetActive(showRed);
        if (blueTeamPanel != null) blueTeamPanel.gameObject.SetActive(showBlue);

        if (myTeamText != null)
        {
            string teamName = myTeam == TeamAssigner.Team.Red ? "레드팀" : "블루팀";
            myTeamText.text = teamName;
            myTeamText.color = (myTeam == TeamAssigner.Team.Red)
                ? new Color(1f, 0.3f, 0.3f)
                : new Color(0.3f, 0.5f, 1f);
        }

        Debug.Log($"[CSM] 내 팀 패널만 표시: myTeam={myTeam}, RedPanel={showRed}, BluePanel={showBlue}");
    }

    private FusionTimerData GetAvailableTimer()
    {
        var all = FindObjectsOfType<FusionTimerData>();

        foreach (var t in all)
        {
            if (t.Runner != null && t.Runner.IsRunning)
            {
                Debug.Log($"[CSM] 타이머 발견 (권한: {t.Object.HasStateAuthority})");
                return t;
            }
        }

        Debug.LogWarning("[CSM] 유효한 타이머 객체를 찾을 수 없음!");
        return null;
    }

    private IEnumerator DebugLogRemainingTimesCoroutine()
    {
        while (true)
        {
            if (timer != null && timer.Runner != null && timer.Runner.IsRunning)
            {
                float sel = timer.GetRemainingSelectionTime();
                float fin = timer.GetRemainingFinalCountdown();
            }
            else
            {
                Debug.LogWarning("❗ [CLIENT DEBUG] 타이머 또는 러너 없음");
            }

            yield return new WaitForSeconds(1f);
        }
    }

    public void UpdateTimerText(float selectionTime, float finalCountdown)
    {
        if (!selectionPanel.activeSelf)
            return;

        if (finalCountdown > 0f)
        {
            countdownText.text = $"{Mathf.CeilToInt(finalCountdown)}";
        }
        else if (selectionTime > 0f)
        {
            countdownText.text = $"{Mathf.CeilToInt(selectionTime)}";
        }
        else
        {
            countdownText.text = "";
        }
    }

    private void UpdateUIState()
    {
        if (timer == null)
            timer = FindObjectOfType<FusionTimerData>();
        if (timer == null || runner == null)
            return;

        float selLeft = timer.GetRemainingSelectionTime();
        bool finalLock = timer.IsFinalCountdown || isFinalCountdownStarted || (selLeft <= 1f);

        bool myReady = false;
        CharacterSelectionNetworkData.Instance.ReadyStates.TryGet(runner.LocalPlayer, out myReady);
        if (localReadyOverride) myReady = true;

        string mySelectedId = null;
        CharacterSelectionNetworkData.Instance.SelectedCharacters.TryGet(runner.LocalPlayer, out mySelectedId);

        readyButton.interactable = !finalLock;

        foreach (var btn in characterButtons)
        {
            if (btn == null || btn.button == null || btn.characterInfo == null) continue;

            // 로컬에서 이미 잠근 버튼은 스킵
            if (!btn.button.interactable) continue;

            bool isMyPickedNet = (!string.IsNullOrEmpty(mySelectedId) && btn.characterInfo.characterId == mySelectedId);
            if (isMyPickedNet)
            {
                btn.button.interactable = false;
                continue;
            }

            btn.button.interactable = !finalLock && !myReady;
        }

        Debug.Log($"[UI 상태 갱신] finalLock={finalLock}, myReady={myReady}");
    }

    /// <summary>
    /// 호스트 마이그레이션 등으로 FusionTimerData 인스턴스를 다시 찾아 바인딩합니다.
    /// </summary>
    public void RebindTimerEvents()
    {
        timer = FindObjectOfType<FusionTimerData>();
    }

    private void SyncUIFromServerState()
    {
        if (timer == null)
            timer = FindObjectOfType<FusionTimerData>();
        if (timer == null || runner == null)
            return;

        float selLeft = timer.GetRemainingSelectionTime();
        bool finalLock = timer.IsFinalCountdown || isFinalCountdownStarted || (selLeft <= 1f);

        selectionPanel.SetActive(!timer.IsFinalCountdown);

        bool myReady = false;
        CharacterSelectionNetworkData.Instance.ReadyStates.TryGet(runner.LocalPlayer, out myReady);
        if (localReadyOverride) myReady = true;

        string mySelectedId = null;
        CharacterSelectionNetworkData.Instance.SelectedCharacters.TryGet(runner.LocalPlayer, out mySelectedId);

        readyButton.interactable = !finalLock;

        foreach (var btn in characterButtons)
        {
            if (btn == null || btn.button == null || btn.characterInfo == null) continue;

            // ✅ 캐릭터 잠금 상태 먼저 체크
            bool isLocked = UserCharacterDataManager.Instance.IsCharacterLocked(btn.characterInfo.characterId);
            if (isLocked)
            {
                btn.button.interactable = false;
                continue;
            }

            // 이미 로컬에서 잠근 버튼은 건드리지 않음
            if (!btn.button.interactable) continue;

            bool isMyPickedNet = (!string.IsNullOrEmpty(mySelectedId) && btn.characterInfo.characterId == mySelectedId);
            if (isMyPickedNet)
            {
                btn.button.interactable = false;
                continue;
            }

            btn.button.interactable = !finalLock && !myReady;
        }

        Debug.Log($"[SyncUI] finalLock={finalLock}, myReady={myReady}");
    }

    private IEnumerator WaitForTeamAssigner()
    {
        while (TeamAssigner.Instance == null)
        {
            Debug.Log("[CSM] TeamAssigner 대기 중...");
            yield return null;
        }

        Debug.Log("[CSM] UI 생성 시작 (닉네임 도착 즉시 생성)");

        // 현재 보이는 닉네임은 즉시 생성, 없으면 기다렸다가 개별 생성
        foreach (var p in runner.ActivePlayers)
        {
            if (TeamAssigner.Instance.Nicknames.TryGet(p, out var nickname))
            {
                AddTeamMemberItem(p, nickname);
            }
            else
            {
                StartCoroutine(Co_WaitNicknameThenAddItem(p));
            }
        }
    }

    private bool AllPlayersHaveNicknames()
    {
        foreach (var kvp in TeamAssigner.Instance.Teams)
        {
            var player = kvp.Key;
            if (!TeamAssigner.Instance.Nicknames.ContainsKey(player))
                return false;
        }
        return true;
    }

    private IEnumerator WaitForCharacterSelectionNetworkData()
    {
        float timeout = 5f;
        float timerLocal = 0f;

        while (CharacterSelectionNetworkData.Instance == null)
        {
            timerLocal += Time.deltaTime;
            if (timerLocal > timeout)
            {
                Debug.LogError("❌ CharacterSelectionNetworkData.Instance 초기화 타임아웃");
                yield break;
            }
            yield return null;
        }

        // Local 핸들러 복제 대기
        yield return new WaitUntil(() => CharacterSelectionNetworkData.Local != null);

        // 캐릭터 버튼 일단 활성화
        foreach (var btn in characterButtons)
            btn.button.interactable = true;

        // 이벤트 구독
        CharacterSelectionNetworkData.Instance.OnCharacterSelected -= HandleCharacterSelected;
        CharacterSelectionNetworkData.Instance.OnCharacterSelected += HandleCharacterSelected;

        CharacterSelectionNetworkData.Local.OnCharacterSelected -= HandleCharacterSelected;
        CharacterSelectionNetworkData.Local.OnCharacterSelected += HandleCharacterSelected;

        CharacterSelectionNetworkData.Instance.OnReadyToggled -= HandleReadyStateChanged;
        CharacterSelectionNetworkData.Instance.OnReadyToggled += HandleReadyStateChanged;

        CharacterSelectionNetworkData.Local.OnReadyToggled -= HandleReadyStateChanged;
        CharacterSelectionNetworkData.Local.OnReadyToggled += HandleReadyStateChanged;

        // 이미 선택된 캐릭터 UI 반영
        foreach (var kvp in CharacterSelectionNetworkData.Instance.SelectedCharacters)
            HandleCharacterSelected(kvp.Key, kvp.Value);

        // 🔁 “모든 닉네임 도착”을 기다리지 말고, 개별 플레이어별로 닉네임이 오면 곧바로 생성
        foreach (var p in runner.ActivePlayers)
            StartCoroutine(Co_WaitNicknameThenAddItem(p));
    }

    private IEnumerator Co_WaitNicknameThenAddItem(PlayerRef player)
    {
        float hardTimeout = 8f;
        float t = 0f;

        while (TeamAssigner.Instance == null ||
               !TeamAssigner.Instance.Nicknames.TryGet(player, out var _))
        {
            t += Time.deltaTime;
            if (t > hardTimeout)
            {
                Debug.LogWarning($"⏱ 닉네임 대기 타임아웃: {player}");
                yield break;
            }
            yield return null;
        }

        if (TeamAssigner.Instance.Nicknames.TryGet(player, out var nickname))
            AddTeamMemberItem(player, nickname);
    }

    private void AddTeamMemberItem(PlayerRef player, string nickname)
    {
        if (spawnedItems.ContainsKey(player)) return;

        if (!TeamAssigner.Instance.HasTeam(player))
        {
            Debug.LogWarning($"⛔ 팀 정보 없음 - {player}, 임시로 UI 생성");
        }

        var team = TeamAssigner.Instance.GetTeam(player);
        GameObject prefab;
        Transform parent;

        // ⚠️ 수정 포인트: 모든 클라이언트에서 동일한 prefab으로 생성되게 보장
        bool isMine = player == runner.LocalPlayer;

        if (isMine && myTeamItemPrefab != null)
        {
            prefab = myTeamItemPrefab;
            parent = (team == TeamAssigner.Team.Red) ? redTeamPanel : blueTeamPanel;
        }
        else
        {
            prefab = (team == TeamAssigner.Team.Red) ? redTeamItemPrefab : blueTeamItemPrefab;
            parent = (team == TeamAssigner.Team.Red) ? redTeamPanel : blueTeamPanel;
        }

        GameObject item = Instantiate(prefab, parent);
        TeamMemberItem itemScript = item.GetComponent<TeamMemberItem>();
        itemScript.SetNickname(nickname);
        itemScript.InitializeDefault();

        spawnedItems[player] = item;

        // 🔄 준비 상태 즉시 반영
        if (CharacterSelectionNetworkData.Instance.ReadyStates.TryGet(player, out var isReady))
        {
            itemScript.SetReady(isReady);
        }
    }

    private void HandleNicknameUpdated(PlayerRef player, string nickname)
    {
        Debug.Log($"✅ 닉네임 업데이트 수신: {player} → {nickname}");
        AddTeamMemberItem(player, nickname); // 무조건 시도
    }

    public void HandleCharacterSelected(PlayerRef player, string characterId)
    {
        if (!spawnedItems.ContainsKey(player))
        {
            Debug.LogWarning($"⚠️ HandleCharacterSelected: 해당 PlayerRef {player}에 대한 UI 항목이 없음 → 강제로 추가 시도");

            if (TeamAssigner.Instance != null && TeamAssigner.Instance.Nicknames.TryGet(player, out var name))
            {
                AddTeamMemberItem(player, name);
            }
            else
            {
                return;
            }
        }

        var item = spawnedItems[player].GetComponent<TeamMemberItem>();
        var character = GetCharacterInfoById(characterId);

        if (item != null && character != null)
        {
            // TeamMemberItem.SetCharacterVisual에서 동일 프리팹이면 노옵 → 깜빡임 방지
            item.SetCharacterVisual(character.uiPrefab);
        }
    }

    public CharacterInfo GetCharacterInfoById(string id)
    {
        foreach (var button in characterButtons)
        {
            if (button.characterInfo != null && button.characterInfo.characterId == id)
            {
                return button.characterInfo;
            }
        }

        Debug.LogWarning($"⚠️ GetCharacterInfoById 실패: characterId '{id}' 없음");
        return null;
    }

    public void OnClick_Ready()
    {
        var local = CharacterSelectionNetworkData.Local;
        if (local == null || local.Object == null) return;

        bool isTutorialPractice = (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Tutorial" &&
                                  UserInfoManager.Instance != null &&
                                  !UserInfoManager.Instance.IsTutorialCompleted);

        var playerKey = runner.LocalPlayer;

        // 🔒 캐릭터가 선택되지 않았을 경우 Ready 처리 차단
        if (!CharacterSelectionNetworkData.Instance.SelectedCharacters.TryGet(playerKey, out var selectedId) || string.IsNullOrEmpty(selectedId))
        {
            Debug.LogWarning("⛔ 캐릭터 미선택 상태에서는 Ready 불가");
            return;
        }

        bool currentReady = CharacterSelectionNetworkData.Instance.ReadyStates.TryGet(playerKey, out var val) && val;
        bool newReady = !currentReady;

        Debug.Log($"🎯 Ready 버튼 클릭 - PlayerKey: {playerKey}, NewReady: {newReady}");

        local.RequestToggleReady();

        if (newReady)
        {
            localReadyOverride = true;

            foreach (var btn in characterButtons)
                btn.button.interactable = false;

            // ✅ 연습방(튜토리얼 미완료): Ready 누르면 즉시 캐릭터 선택 패널 닫기
            if (isTutorialPractice && selectionPanel != null)
                selectionPanel.SetActive(false);
        }

        // ✅ 연습방(튜토리얼 미완료)에서는 타이머 기반 락 대신 Ready 여부만 반영
        if (isTutorialPractice)
        {
            readyButton.interactable = false;
        }
        else
        {
            readyButton.interactable = !timer.IsFinalCountdown;
        }
    }

    private void AutoSelectAndReady()
    {
        if (!CharacterSelectionNetworkData.Instance.Object.HasStateAuthority) return;

        foreach (var player in runner.ActivePlayers.Concat(new[] { runner.LocalPlayer }).Distinct())
        {
            if (!CharacterSelectionNetworkData.Instance.SelectedCharacters.TryGet(player, out var selectedId)
                || string.IsNullOrEmpty(selectedId))
            {
                var randomCharacter = characterButtons[Random.Range(0, characterButtons.Length)].characterInfo.characterId;
                CharacterSelectionNetworkData.Instance.SelectedCharacters.Set(player, randomCharacter);
                CharacterSelectionManager.Instance.HandleCharacterSelected(player, randomCharacter);
                Debug.Log($"🔄 자동 캐릭터 선택: {player} → {randomCharacter}");
            }

            if (!CharacterSelectionNetworkData.Instance.ReadyStates.ContainsKey(player) ||
                !CharacterSelectionNetworkData.Instance.ReadyStates[player])
            {
                CharacterSelectionNetworkData.Instance.ForceToggleReady(player, true);
                Debug.Log($"🔄 자동 준비 처리: {player}");
            }
        }
    }

    public void HandleReadyStateChanged(PlayerRef player, bool isReady)
    {
        Debug.Log($"[ReadyStateChanged] Player={player}, isReady={isReady}");

        float selLeft = (timer != null) ? timer.GetRemainingSelectionTime() : 0f;
        bool finalLock = (timer != null && timer.IsFinalCountdown) || isFinalCountdownStarted || (selLeft <= 1f);

        if (runner != null && runner.LocalPlayer == player)
        {
            // ❗ 준비 취소 → localReadyOverride 자동 해제
            if (!isReady) localReadyOverride = false;

            string mySelectedId = null;
            CharacterSelectionNetworkData.Instance.SelectedCharacters.TryGet(runner.LocalPlayer, out mySelectedId);

            readyButton.interactable = !finalLock;

            foreach (var btn in characterButtons)
            {
                bool isMyPicked = (btn != null && btn.characterInfo != null && btn.characterInfo.characterId == mySelectedId);
                btn.button.interactable = !finalLock && !isReady && !isMyPicked;
            }
        }

        if (spawnedItems.TryGetValue(player, out var itemObj))
        {
            var item = itemObj.GetComponent<TeamMemberItem>();
            if (item != null)
                item.SetReady(isReady);
        }
    }

    private void StopCoroutineIfRunning(ref Coroutine coroutine)
    {
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
            coroutine = null;
        }
    }

    private IEnumerator HeartbeatSender()
    {
        while (true)
        {
            var netData = CharacterSelectionNetworkData.Instance;
            if (netData != null && netData.Object != null && netData.Object.HasInputAuthority)
            {
                float now = netData.Runner.SimulationTime;
                netData.Rpc_SendHeartbeat(netData.Runner.LocalPlayer, now);
            }
            yield return new WaitForSeconds(1f); // 1초 간격 송신 (원하면 더 짧게/길게)
        }
    }

    private void CheckHeartbeatStatus()
    {
        var netData = CharacterSelectionNetworkData.Instance;
        if (netData == null) return;
        if (netData.Runner == null || !netData.Runner.IsRunning) return;

        float now = netData.Runner.SimulationTime;

        foreach (var player in spawnedItems.Keys)
        {
            bool heartbeatDisconnected = false;

            if (netData.Heartbeats.ContainsKey(player))
            {
                float lastBeat = netData.Heartbeats[player];
                heartbeatDisconnected = (now - lastBeat) > 2.5f; // ⏱ 살짝 여유
            }
            else
            {
                // 최초 하트비트 전엔 끊김으로 보지 않음
                heartbeatDisconnected = false;
            }

            // 콜백 기반 연결 플래그(실제 조인/퇴장 이벤트)와 OR
            bool eventDisconnected = playerDisconnected.TryGetValue(player, out bool val) && val;

            bool isDisconnected = heartbeatDisconnected || eventDisconnected;

            if (!lastDisconnectState.ContainsKey(player) || lastDisconnectState[player] != isDisconnected)
            {
                lastDisconnectState[player] = isDisconnected;
                UpdateDisconnectIndicator(player, isDisconnected);
            }
        }
    }

    private void UpdateDisconnectIndicator(PlayerRef player, bool isDisconnected)
    {
        if (spawnedItems.TryGetValue(player, out var obj))
        {
            var teamItem = obj.GetComponent<TeamMemberItem>();
            if (teamItem != null)
                teamItem.SetDisconnected(isDisconnected);
        }
    }
}