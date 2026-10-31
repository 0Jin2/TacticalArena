using BackEnd;
using BackEnd.Tcp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.UI;

// 뒤끝 매치 기반 팀데스매치 진행 매니저 (ClassicTest 씬)
// 1. 팀데스매치 인트로 연출 → 뒤끝 Character 테이블 조회 → 보유 여부로 캐릭터 잠금 → 캐릭터 선택 30초
// 2. 선택 종료 시 캐릭터/준비완료 버튼 잠금 → 게임 시작까지 5초 카운트다운
// 3. 캐릭터 생성 → 시네머신 본인 캐릭터 추적 → 인게임 UI 활성화 → 게임 시작
// 팀 배정은 참가 인원 수와 무관하게 동작하도록 작성되어 1vs1 이외의 다수vs다수 매치에도 그대로 쓸 수 있다.
public class BackendTDMManager : MonoBehaviour
{
    public enum Team { Red = 0, Blue = 1 }

    public enum Phase { Intro, WaitingPlayers, Selection, Countdown, Playing }

    public class PlayerSlot
    {
        public string sessionId;
        public string nickname;
        public Team team;
        public int teamIndex;
        public string characterId;
        public bool isReady;
        public bool isOnline = true;
        public GameObject spawnedCharacter;
    }

    public static BackendTDMManager Instance { get; private set; }

    private const string CHARACTER_TABLE = "Character";
    private static readonly string[] CharacterKeys = { "ChulWoo", "Jairo", "JinHyuk", "Leon", "Mirei", "Rubell", "SeoHee" };

    // 릴레이 메시지 타입
    private const string MSG_SCENE_READY = "SCENE_READY";
    private const string MSG_SELECTION_START = "SELECT_START";
    private const string MSG_SELECT = "SELECT";
    private const string MSG_READY = "READY";
    private const string MSG_COUNTDOWN_START = "COUNTDOWN";

    [Header("Timing")]
    [SerializeField] private float selectionDuration = 30f;
    [SerializeField] private float countdownDuration = 5f;
    [Tooltip("모든 참가자의 씬 로드를 기다리는 최대 시간")]
    [SerializeField] private float waitPlayersTimeout = 10f;
    [Tooltip("선택 시간이 끝난 뒤 자동 선택 메시지를 기다리는 시간")]
    [SerializeField] private float selectionGraceTime = 1.5f;

    [Header("Intro (팀데스매치 모드 안내)")]
    [SerializeField] private GameObject introPanel;
    [SerializeField] private CanvasGroup introCanvasGroup;
    [SerializeField] private RectTransform introTitle;
    [Tooltip("지정 시 인트로를 이 애니메이터로 재생한다. 비워두면 코드 연출을 사용한다.")]
    [SerializeField] private Animator introAnimator;
    [SerializeField] private string introAnimatorStateName = "TDMIntro";
    [SerializeField] private float introDuration = 2.5f;

    [Header("Character Selection UI")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private TMP_Text selectionTimerText;
    [SerializeField] private TMP_Text myTeamText;
    [SerializeField] private TMP_Text redTeamListText;
    [SerializeField] private TMP_Text blueTeamListText;
    [SerializeField] private TMP_Text selectedCharacterText;
    [SerializeField] private Button readyButton;
    [SerializeField] private BackendTDMCharacterButton[] characterButtons;

    [Header("Countdown UI")]
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private TMP_Text countdownText;

    [Header("Gameplay")]
    [SerializeField] private GameObject gameplayUIRoot;
    [SerializeField] private CinemachineCamera virtualCamera;
    [SerializeField] private Transform[] redTeamSpawnPoints;
    [SerializeField] private Transform[] blueTeamSpawnPoints;
    [SerializeField] private CharacterInfo[] characterInfos;
    [SerializeField] private string defaultCharacterId = "ChulWoo";
    [Tooltip("캐릭터 리디자인(rigging-test) 임시 캐릭터. 지정하면 선택한 캐릭터와 관계없이 모두 이 캐릭터로 생성한다.")]
    [SerializeField] private GameObject tempCharacterPrefab;
    [SerializeField] private float nameTagHeight = 0.62f;

    [Header("Editor Test")]
    [Tooltip("에디터에서 이 씬을 바로 실행하면 로비 없이 자동으로 매칭한 뒤 씬을 다시 불러온다.")]
    [SerializeField] private bool autoMatchWhenOpenedDirectly = true;
    [SerializeField] private string testMatchCardInDate = "2026-10-29T11:23:09.812Z";
    [Tooltip("이 시간 동안 상대가 없으면 매칭을 취소하고 혼자 진행한다 (1p 시나리오용)")]
    [SerializeField] private float soloStartTimeout = 8f;

    public event Action OnGameStarted;

    public Phase CurrentPhase { get; private set; } = Phase.Intro;
    public IReadOnlyList<PlayerSlot> Players => players;
    public PlayerSlot LocalPlayer => FindSlot(localSessionId);

    private readonly List<PlayerSlot> players = new List<PlayerSlot>();
    private readonly Dictionary<string, bool> characterUnlocks = new Dictionary<string, bool>();
    private readonly HashSet<string> sceneReadySessions = new HashSet<string>();

    private string localSessionId;
    private bool isHost;
    private bool offlineMode;
    private bool localSceneReady;
    private float phaseEndTime;
    private bool selectionEndHandled;

    private void Awake()
    {
        Instance = this;
        SetActive(introPanel, false);
        SetActive(selectionPanel, false);
        SetActive(countdownPanel, false);
        SetActive(gameplayUIRoot, false);

        if (readyButton != null) readyButton.onClick.AddListener(OnReadyButtonClicked);
        if (characterButtons != null)
        {
            foreach (var button in characterButtons)
                if (button != null) button.Bind(this);
        }
    }

    private void OnEnable()
    {
        // 뒤끝이 초기화되지 않은 상태(씬 단독 실행)에서는 Backend.Match 접근 시 예외가 발생한다.
        if (!Backend.IsInitialized) return;
        Backend.Match.OnMatchRelay += HandleMatchRelay;
        Backend.Match.OnSessionOffline += HandleSessionOffline;
        Backend.Match.OnSessionOnline += HandleSessionOnline;
        Backend.Match.OnLeaveInGameServer += HandleLeaveInGameServer;
        Backend.Match.OnChangeSuperGamer += HandleChangeSuperGamer;
    }

    private void OnDisable()
    {
        if (!Backend.IsInitialized) return;
        Backend.Match.OnMatchRelay -= HandleMatchRelay;
        Backend.Match.OnSessionOffline -= HandleSessionOffline;
        Backend.Match.OnSessionOnline -= HandleSessionOnline;
        Backend.Match.OnLeaveInGameServer -= HandleLeaveInGameServer;
        Backend.Match.OnChangeSuperGamer -= HandleChangeSuperGamer;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private IEnumerator Start()
    {
        if (ShouldAutoMatch())
        {
            yield return AutoMatchForEditorTest();
            // 매칭에 성공하면 BACKEND_MATCH가 이 씬을 다시 불러오므로 여기서 끝난다.
            if (BACKEND_MATCH.Instance != null && BACKEND_MATCH.Instance.IsEnteringGame) yield break;
        }

        BuildPlayerSlots();

        // 인트로 연출과 캐릭터 테이블 조회를 동시에 진행한다.
        bool characterDataLoaded = false;
        LoadCharacterTable(() => characterDataLoaded = true);
        yield return PlayIntro();
        while (!characterDataLoaded) yield return null;

        ApplyCharacterLocks();
        RefreshTeamUI();

        CurrentPhase = Phase.WaitingPlayers;
        localSceneReady = true;
        Send(MSG_SCENE_READY);
        MarkSceneReady(localSessionId);

        if (isHost) StartCoroutine(HostWaitForPlayers());
    }

    private void Update()
    {
        if (CurrentPhase == Phase.Selection)
        {
            float remaining = Mathf.Max(0f, phaseEndTime - Time.time);
            if (selectionTimerText != null) selectionTimerText.text = Mathf.CeilToInt(remaining).ToString();
            if (remaining <= 0f && !selectionEndHandled) EndSelectionLocally();
        }
        else if (CurrentPhase == Phase.Countdown)
        {
            float remaining = Mathf.Max(0f, phaseEndTime - Time.time);
            if (countdownText != null) countdownText.text = remaining > 0f ? Mathf.CeilToInt(remaining).ToString() : "START!";
            if (remaining <= 0f) StartGame();
        }
    }

    #region Players & teams

    private void BuildPlayerSlots()
    {
        players.Clear();
        var match = BACKEND_MATCH.Instance;
        var records = match != null ? match.GameRecords.Where(r => r != null).ToList() : new List<MatchUserGameRecord>();

        if (records.Count == 0)
        {
            // 매칭 없이 씬을 직접 실행한 경우(에디터 테스트) 혼자 진행한다.
            offlineMode = true;
            isHost = true;
            localSessionId = "LOCAL";
            players.Add(new PlayerSlot { sessionId = localSessionId, nickname = GetLocalNickname(), team = Team.Red });
            AssignTeamIndices();
            Debug.LogWarning("[BackendTDM] 매칭 정보가 없어 오프라인 테스트 모드로 진행합니다.");
            return;
        }

        localSessionId = Backend.Match.GetMySessionId().ToString();
        isHost = Backend.Match.IsSuperGamer();

        // 모든 클라이언트가 같은 결과를 얻도록 세션 ID 순으로 정렬한다.
        records.Sort((a, b) => ((uint)a.m_sessionId).CompareTo((uint)b.m_sessionId));

        // 매치 카드가 팀전이라 서버가 팀 번호를 나눠준 경우 그대로 쓰고, 아니면 번갈아 배정한다.
        bool serverTeams = records.Select(r => r.m_teamNumber).Distinct().Count() > 1;
        var teamNumbers = records.Select(r => r.m_teamNumber).Distinct().OrderBy(n => n).ToList();

        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            Team team = serverTeams
                ? (teamNumbers.IndexOf(record.m_teamNumber) % 2 == 0 ? Team.Red : Team.Blue)
                : (i % 2 == 0 ? Team.Red : Team.Blue);

            players.Add(new PlayerSlot
            {
                sessionId = record.m_sessionId.ToString(),
                nickname = string.IsNullOrEmpty(record.m_nickname) ? "Player" + (i + 1) : record.m_nickname,
                team = team
            });
        }
        AssignTeamIndices();

        Debug.Log("[BackendTDM] 참가자 " + players.Count + "명, 내 세션=" + localSessionId + ", 호스트=" + isHost);
    }

    private bool ShouldAutoMatch()
    {
        if (!Application.isEditor || !autoMatchWhenOpenedDirectly || !Backend.IsInitialized) return false;
        var match = BACKEND_MATCH.Instance;
        if (match == null) return true;
        // 로비에서 매칭하고 들어온 경우(이미 게임방 입장 중)에는 다시 매칭하지 않는다.
        return !match.IsEnteringGame && !match.GameRecords.Any();
    }

    private IEnumerator AutoMatchForEditorTest()
    {
        var match = BACKEND_MATCH.Instance;
        if (match == null) match = new GameObject("BACKEND_MATCH (Editor Test)").AddComponent<BACKEND_MATCH>();

        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!match.StartMatchSearchForTest(BACKEND_MATCH.TacticalMatchMode.CLASSIC, testMatchCardInDate, sceneName))
        {
            Debug.LogWarning("[BackendTDM] 자동 매칭을 시작하지 못해 오프라인 테스트로 진행합니다.");
            yield break;
        }

        var status = CreateMatchingStatusText();
        float startedAt = Time.time;
        // 매칭이 잡히면 씬이 다시 로드되어 이 코루틴도 함께 사라진다. 실패/취소되면 오프라인으로 진행한다.
        while (match.IsMatchSearchActive || match.IsEnteringGame)
        {
            if (match.IsEnteringGame)
            {
                if (status != null) status.text = "매칭 완료! 게임방 입장 중...";
                yield return null;
                continue;
            }
            float remaining = soloStartTimeout - (Time.time - startedAt);
            if (remaining <= 0f)
            {
                // 1p로 실행하면 상대가 없으므로 기다리지 않고 혼자 진행한다.
                match.CancelMatchSearch();
                break;
            }
            if (status != null) status.text = "에디터 테스트 매칭 중...\n<size=60%>" + Mathf.CeilToInt(remaining) + "초 안에 상대가 없으면 혼자 시작합니다</size>";
            yield return null;
        }
        if (status != null) Destroy(status.gameObject);
        Debug.LogWarning("[BackendTDM] 자동 매칭이 종료되어 오프라인 테스트로 진행합니다.");
    }

    private TMP_Text CreateMatchingStatusText()
    {
        if (countdownPanel == null) return null;
        var go = new GameObject("MatchingStatusText", typeof(RectTransform));
        go.transform.SetParent(countdownPanel.transform.parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<TextMeshProUGUI>();
        if (countdownText != null) text.font = countdownText.font;
        text.fontSize = 56;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        return text;
    }

    private void AssignTeamIndices()
    {
        foreach (Team team in Enum.GetValues(typeof(Team)))
        {
            int index = 0;
            foreach (var slot in players.Where(p => p.team == team)) slot.teamIndex = index++;
        }
    }

    private string GetLocalNickname()
    {
        string nickname = Backend.IsInitialized ? Backend.UserNickName : null;
        return string.IsNullOrEmpty(nickname) ? "Player" : nickname;
    }

    private PlayerSlot FindSlot(string sessionId)
    {
        return players.FirstOrDefault(p => p.sessionId == sessionId);
    }

    private IEnumerable<PlayerSlot> ActivePlayers => players.Where(p => p.isOnline);

    #endregion

    #region 1. Intro & character table

    private IEnumerator PlayIntro()
    {
        CurrentPhase = Phase.Intro;
        SetActive(introPanel, true);

        if (introAnimator != null)
        {
            introAnimator.Play(introAnimatorStateName, 0, 0f);
            yield return null;
            float length = introAnimator.GetCurrentAnimatorStateInfo(0).length;
            yield return new WaitForSeconds(length > 0f ? length : introDuration);
        }
        else
        {
            // 타이틀이 튀어나오고 잠시 머문 뒤 사라지는 기본 연출
            float fadeTime = 0.35f;
            float holdTime = Mathf.Max(0f, introDuration - fadeTime * 2f);

            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                float k = t / fadeTime;
                if (introCanvasGroup != null) introCanvasGroup.alpha = k;
                if (introTitle != null) introTitle.localScale = Vector3.one * Mathf.LerpUnclamped(2.5f, 1f, EaseOutBack(k));
                yield return null;
            }
            if (introCanvasGroup != null) introCanvasGroup.alpha = 1f;
            if (introTitle != null) introTitle.localScale = Vector3.one;

            yield return new WaitForSeconds(holdTime);

            for (float t = 0f; t < fadeTime; t += Time.deltaTime)
            {
                if (introCanvasGroup != null) introCanvasGroup.alpha = 1f - t / fadeTime;
                yield return null;
            }
        }

        SetActive(introPanel, false);
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    private void LoadCharacterTable(Action onComplete)
    {
        if (!Backend.IsInitialized)
        {
            Debug.LogWarning("[BackendTDM] 뒤끝이 초기화되지 않아 캐릭터 테이블 조회를 건너뜁니다.");
            onComplete?.Invoke();
            return;
        }

        Backend.GameData.GetMyData(CHARACTER_TABLE, new Where(), 1, result =>
        {
            characterUnlocks.Clear();
            if (result.IsSuccess())
            {
                var rows = result.FlattenRows();
                if (rows != null && rows.Count > 0)
                {
                    var row = rows[0];
                    foreach (var key in CharacterKeys)
                    {
                        bool unlocked;
                        if (row.ContainsKey(key) && bool.TryParse(row[key].ToString(), out unlocked))
                            characterUnlocks[key] = unlocked;
                    }
                }
            }
            else
            {
                Debug.LogError("[BackendTDM] 캐릭터 테이블 조회 실패: " + result);
            }

            Debug.Log("[BackendTDM] 캐릭터 보유 정보: " + string.Join(", ", characterUnlocks.Select(kv => kv.Key + "=" + kv.Value)));
            onComplete?.Invoke();
        });
    }

    public bool IsCharacterUnlocked(string characterId)
    {
        bool unlocked;
        if (characterUnlocks.TryGetValue(characterId, out unlocked)) return unlocked;
        // 테이블 조회에 실패했으면 로비에서 받아둔 정보를 사용한다.
        if (UserCharacterDataManager.Instance != null) return !UserCharacterDataManager.Instance.IsCharacterLocked(characterId);
        return characterId == "ChulWoo" || characterId == "JinHyuk" || characterId == "SeoHee";
    }

    private void ApplyCharacterLocks()
    {
        if (characterButtons == null) return;
        foreach (var button in characterButtons)
        {
            if (button == null || button.CharacterInfo == null) continue;
            bool unlocked = IsCharacterUnlocked(button.CharacterInfo.characterId);
            button.SetLocked(!unlocked);
            button.SetInteractable(false); // 선택 단계 시작 전까지는 누를 수 없다.
        }
        if (readyButton != null) readyButton.interactable = false;
    }

    #endregion

    #region Selection phase

    private IEnumerator HostWaitForPlayers()
    {
        float deadline = Time.time + waitPlayersTimeout;
        while (Time.time < deadline && ActivePlayers.Any(p => !sceneReadySessions.Contains(p.sessionId)))
            yield return null;

        if (CurrentPhase != Phase.WaitingPlayers) yield break;
        Send(MSG_SELECTION_START, selectionDuration.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BeginSelection(selectionDuration);
    }

    private void MarkSceneReady(string sessionId)
    {
        if (!string.IsNullOrEmpty(sessionId)) sceneReadySessions.Add(sessionId);
    }

    private void BeginSelection(float duration)
    {
        if (CurrentPhase != Phase.WaitingPlayers && CurrentPhase != Phase.Intro) return;

        CurrentPhase = Phase.Selection;
        phaseEndTime = Time.time + duration;
        selectionEndHandled = false;

        SetActive(selectionPanel, true);
        RefreshSelectionInteractable();
        RefreshTeamUI();
        Debug.Log("[BackendTDM] 캐릭터 선택 시작 (" + duration + "초)");
    }

    public void OnCharacterButtonClicked(string characterId)
    {
        var me = LocalPlayer;
        if (CurrentPhase != Phase.Selection || selectionEndHandled || me == null || me.isReady) return;
        if (!IsCharacterUnlocked(characterId)) return;

        ApplySelect(localSessionId, characterId);
        Send(MSG_SELECT, characterId);
    }

    private void OnReadyButtonClicked()
    {
        var me = LocalPlayer;
        if (CurrentPhase != Phase.Selection || selectionEndHandled || me == null || me.isReady) return;
        if (string.IsNullOrEmpty(me.characterId)) return;

        ApplyReady(localSessionId);
        Send(MSG_READY, me.characterId);
    }

    private void ApplySelect(string sessionId, string characterId)
    {
        var slot = FindSlot(sessionId);
        if (slot == null || slot.isReady || CurrentPhase == Phase.Countdown || CurrentPhase == Phase.Playing) return;
        slot.characterId = characterId;
        RefreshTeamUI();
        RefreshSelectionInteractable();
    }

    private void ApplyReady(string sessionId)
    {
        var slot = FindSlot(sessionId);
        if (slot == null || slot.isReady) return;
        slot.isReady = true;
        RefreshTeamUI();
        RefreshSelectionInteractable();

        // 전원이 준비를 마치면 남은 시간을 기다리지 않고 카운트다운으로 넘어간다.
        if (isHost && CurrentPhase == Phase.Selection && ActivePlayers.All(p => p.isReady))
            HostStartCountdown();
    }

    private void EndSelectionLocally()
    {
        selectionEndHandled = true;
        LockSelectionUI();

        // 시간 안에 고르지 못했으면 보유한 첫 캐릭터로 자동 선택 후 준비 완료 처리
        var me = LocalPlayer;
        if (me != null && !me.isReady)
        {
            if (string.IsNullOrEmpty(me.characterId)) me.characterId = GetFirstUnlockedCharacter();
            Send(MSG_READY, me.characterId);
            ApplyReady(localSessionId);
        }

        if (isHost) StartCoroutine(HostFinishSelectionAfterGrace());
    }

    private IEnumerator HostFinishSelectionAfterGrace()
    {
        float deadline = Time.time + selectionGraceTime;
        while (Time.time < deadline && ActivePlayers.Any(p => !p.isReady)) yield return null;
        HostStartCountdown();
    }

    private string GetFirstUnlockedCharacter()
    {
        if (IsCharacterUnlocked(defaultCharacterId)) return defaultCharacterId;
        var key = CharacterKeys.FirstOrDefault(IsCharacterUnlocked);
        return key ?? defaultCharacterId;
    }

    private void RefreshSelectionInteractable()
    {
        var me = LocalPlayer;
        bool canSelect = CurrentPhase == Phase.Selection && !selectionEndHandled && me != null && !me.isReady;

        if (characterButtons != null)
        {
            foreach (var button in characterButtons)
            {
                if (button == null || button.CharacterInfo == null) continue;
                string id = button.CharacterInfo.characterId;
                button.SetInteractable(canSelect && IsCharacterUnlocked(id));
                button.SetSelected(me != null && me.characterId == id);
            }
        }

        if (readyButton != null) readyButton.interactable = canSelect && !string.IsNullOrEmpty(me.characterId);

        if (selectedCharacterText != null)
        {
            var info = me != null ? FindCharacterInfo(me.characterId) : null;
            selectedCharacterText.text = info != null ? info.characterName : "캐릭터를 선택하세요";
        }
    }

    private void LockSelectionUI()
    {
        if (characterButtons != null)
            foreach (var button in characterButtons)
                if (button != null) button.SetInteractable(false);
        if (readyButton != null) readyButton.interactable = false;
    }

    private void RefreshTeamUI()
    {
        var me = LocalPlayer;
        if (myTeamText != null && me != null)
        {
            myTeamText.text = me.team == Team.Red ? "RED TEAM" : "BLUE TEAM";
            // 씬을 직접 실행하면 매칭 정보가 없어 혼자 진행된다. 멀티 테스트로 착각하지 않게 표시한다.
            if (offlineMode) myTeamText.text += "  <size=60%><color=#FFD24A>오프라인 테스트 (혼자 진행)</color></size>";
            myTeamText.color = me.team == Team.Red ? new Color(1f, 0.35f, 0.35f) : new Color(0.35f, 0.6f, 1f);
        }
        if (redTeamListText != null) redTeamListText.text = BuildTeamList(Team.Red);
        if (blueTeamListText != null) blueTeamListText.text = BuildTeamList(Team.Blue);
    }

    private string BuildTeamList(Team team)
    {
        var sb = new StringBuilder();
        foreach (var slot in players.Where(p => p.team == team))
        {
            var info = FindCharacterInfo(slot.characterId);
            sb.Append(slot.nickname);
            if (slot.sessionId == localSessionId) sb.Append(" (나)");
            sb.Append("  ");
            sb.Append(info != null ? info.characterName : "선택 중...");
            if (slot.isReady) sb.Append("  <color=#7CFC00>준비완료</color>");
            if (!slot.isOnline) sb.Append("  <color=#888888>연결 끊김</color>");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    #endregion

    #region 2. Countdown

    private void HostStartCountdown()
    {
        if (CurrentPhase != Phase.Selection) return;

        // 호스트가 최종 선택 결과를 확정해 모두에게 알린다. (sessionId:characterId,...)
        var parts = new List<string>();
        foreach (var slot in players)
        {
            if (string.IsNullOrEmpty(slot.characterId)) slot.characterId = defaultCharacterId;
            parts.Add(slot.sessionId + ":" + slot.characterId);
        }
        Send(MSG_COUNTDOWN_START, countdownDuration.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(",", parts));
        BeginCountdown(countdownDuration);
    }

    private void BeginCountdown(float duration)
    {
        if (CurrentPhase != Phase.Selection) return;

        CurrentPhase = Phase.Countdown;
        selectionEndHandled = true;
        phaseEndTime = Time.time + duration;

        LockSelectionUI();
        RefreshTeamUI();
        if (selectionTimerText != null) selectionTimerText.text = "0";
        if (selectedCharacterText != null) selectedCharacterText.gameObject.SetActive(false);
        SetActive(countdownPanel, true);
        Debug.Log("[BackendTDM] 게임 시작 카운트다운 (" + duration + "초)");
    }

    #endregion

    #region 3. Spawn & start

    private void StartGame()
    {
        if (CurrentPhase == Phase.Playing) return;
        CurrentPhase = Phase.Playing;

        SetActive(countdownPanel, false);
        SetActive(selectionPanel, false);

        foreach (var slot in ActivePlayers) SpawnCharacter(slot);

        var me = LocalPlayer;
        if (virtualCamera != null && me != null && me.spawnedCharacter != null)
        {
            virtualCamera.Follow = me.spawnedCharacter.transform;
            virtualCamera.LookAt = me.spawnedCharacter.transform;
        }

        SetActive(gameplayUIRoot, true);
        Debug.Log("[BackendTDM] 게임 시작!");
        OnGameStarted?.Invoke();
    }

    private void SpawnCharacter(PlayerSlot slot)
    {
        if (tempCharacterPrefab != null)
        {
            SpawnTempCharacter(slot);
            return;
        }

        var info = FindCharacterInfo(slot.characterId) ?? FindCharacterInfo(defaultCharacterId);
        if (info == null || info.prefab == null)
        {
            Debug.LogError("[BackendTDM] 캐릭터 프리팹을 찾을 수 없습니다: " + slot.characterId);
            return;
        }

        Transform[] points = slot.team == Team.Red ? redTeamSpawnPoints : blueTeamSpawnPoints;
        Vector3 position = Vector3.zero;
        if (points != null && points.Length > 0) position = points[slot.teamIndex % points.Length].position;

        slot.spawnedCharacter = BackendTDMCharacterFactory.CreateLocalCharacter(info.prefab.gameObject, position);
        slot.spawnedCharacter.name = info.characterId + "_" + slot.nickname;

        // 블루팀은 왼쪽을 바라보게 한다.
        if (slot.team == Team.Blue)
        {
            var scale = slot.spawnedCharacter.transform.localScale;
            scale.x = -Mathf.Abs(scale.x);
            slot.spawnedCharacter.transform.localScale = scale;
        }

        BackendTDMCharacterFactory.SetNameTag(slot.spawnedCharacter, slot.nickname, slot.team == Team.Red ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 0.65f, 1f));
    }

    private void SpawnTempCharacter(PlayerSlot slot)
    {
        slot.spawnedCharacter = Instantiate(tempCharacterPrefab, GetSpawnPosition(slot), Quaternion.identity);
        slot.spawnedCharacter.name = "TempRig_" + slot.nickname;

        // 이동은 아직 동기화하지 않으므로 내 캐릭터만 입력을 받는다.
        var mover = slot.spawnedCharacter.GetComponent<RigTestMover>();
        if (mover != null) mover.enabled = slot.sessionId == localSessionId;

        // 원본 그림은 왼쪽을 본다. 레드팀은 오른쪽(상대 진영)을 보게 뒤집는다.
        var visual = slot.spawnedCharacter.transform.Find("Visual");
        if (visual != null && slot.team == Team.Red)
        {
            var scale = visual.localScale;
            scale.x = -Mathf.Abs(scale.x);
            visual.localScale = scale;
        }

        CreateNameTag(slot.spawnedCharacter.transform, slot.nickname, slot.team == Team.Red ? new Color(1f, 0.4f, 0.4f) : new Color(0.4f, 0.65f, 1f));
    }

    private Vector3 GetSpawnPosition(PlayerSlot slot)
    {
        Transform[] points = slot.team == Team.Red ? redTeamSpawnPoints : blueTeamSpawnPoints;
        if (points == null || points.Length == 0) return Vector3.zero;
        return points[slot.teamIndex % points.Length].position;
    }

    private void CreateNameTag(Transform character, string nickname, Color color)
    {
        var tag = new GameObject("NameTag");
        tag.transform.SetParent(character, false);
        tag.transform.localPosition = new Vector3(0f, nameTagHeight, 0f);

        var text = tag.AddComponent<TextMeshPro>();
        if (countdownText != null) text.font = countdownText.font;
        text.text = nickname;
        text.color = color;
        text.fontSize = 0.9f;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.rectTransform.sizeDelta = new Vector2(3f, 0.3f);
        text.sortingOrder = 100;
    }

    private CharacterInfo FindCharacterInfo(string characterId)
    {
        if (string.IsNullOrEmpty(characterId) || characterInfos == null) return null;
        return characterInfos.FirstOrDefault(c => c != null && c.characterId == characterId);
    }

    #endregion

    #region Relay

    private void Send(string type, params string[] args)
    {
        if (offlineMode) return;
        string message = args.Length == 0 ? type : type + "|" + string.Join("|", args);
        try
        {
            Backend.Match.SendDataToInGameRoom(Encoding.UTF8.GetBytes(message));
        }
        catch (Exception exception)
        {
            Debug.LogError("[BackendTDM] 릴레이 전송 실패: " + exception);
        }
    }

    private void HandleMatchRelay(MatchRelayEventArgs args)
    {
        if (args == null || args.BinaryUserData == null) return;

        string sender = args.From.SessionId.ToString();
        string[] parts = Encoding.UTF8.GetString(args.BinaryUserData).Split('|');
        // 내가 보낸 메시지는 이미 로컬에 반영했으므로 무시한다.
        if (sender == localSessionId) return;

        switch (parts[0])
        {
            case MSG_SCENE_READY:
                MarkSceneReady(sender);
                break;

            case MSG_SELECTION_START:
                float selectionTime;
                if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out selectionTime))
                    StartCoroutine(BeginSelectionWhenLoaded(selectionTime));
                break;

            case MSG_SELECT:
                if (parts.Length > 1) ApplySelect(sender, parts[1]);
                break;

            case MSG_READY:
                if (parts.Length > 1)
                {
                    var slot = FindSlot(sender);
                    if (slot != null && !slot.isReady) slot.characterId = parts[1];
                }
                ApplyReady(sender);
                break;

            case MSG_COUNTDOWN_START:
                if (parts.Length > 2) ApplyFinalSelections(parts[2]);
                float countdownTime;
                if (parts.Length > 1 && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out countdownTime))
                    StartCoroutine(BeginCountdownWhenSelecting(countdownTime));
                break;
        }
    }

    private IEnumerator BeginSelectionWhenLoaded(float duration)
    {
        // 인트로/테이블 조회가 끝나기 전에 시작 신호를 받으면 끝날 때까지 기다린 뒤 남은 시간만큼 진행한다.
        float receivedAt = Time.time;
        while (!localSceneReady) yield return null;
        BeginSelection(Mathf.Max(1f, duration - (Time.time - receivedAt)));
    }

    private IEnumerator BeginCountdownWhenSelecting(float duration)
    {
        float receivedAt = Time.time;
        while (CurrentPhase == Phase.Intro || CurrentPhase == Phase.WaitingPlayers) yield return null;
        if (CurrentPhase == Phase.Selection) BeginCountdown(Mathf.Max(0f, duration - (Time.time - receivedAt)));
    }

    private void ApplyFinalSelections(string encoded)
    {
        foreach (var pair in encoded.Split(','))
        {
            var kv = pair.Split(':');
            if (kv.Length != 2) continue;
            var slot = FindSlot(kv[0]);
            if (slot == null) continue;
            slot.characterId = kv[1];
            slot.isReady = true;
        }
        RefreshTeamUI();
    }

    private void HandleSessionOffline(MatchInGameSessionEventArgs args)
    {
        if (args == null || args.GameRecord == null) return;
        var slot = FindSlot(args.GameRecord.m_sessionId.ToString());
        if (slot == null) return;
        slot.isOnline = false;
        RefreshTeamUI();

        if (isHost && CurrentPhase == Phase.Selection && ActivePlayers.All(p => p.isReady))
            HostStartCountdown();
    }

    private void HandleSessionOnline(MatchInGameSessionEventArgs args)
    {
        if (args == null || args.GameRecord == null) return;
        var slot = FindSlot(args.GameRecord.m_sessionId.ToString());
        if (slot == null) return;
        slot.isOnline = true;
        RefreshTeamUI();
    }

    private void HandleLeaveInGameServer(MatchInGameSessionEventArgs args)
    {
        Debug.LogWarning("[BackendTDM] 인게임 서버 연결 종료: " + (args == null ? "-" : args.ErrInfo.ToString()));
    }

    private void HandleChangeSuperGamer(MatchInGameChangeSuperGamerEventArgs args)
    {
        bool wasHost = isHost;
        isHost = Backend.Match.IsSuperGamer();
        Debug.Log("[BackendTDM] 호스트 변경. 내가 호스트=" + isHost);

        // 호스트가 나가 대기 중에 멈추지 않도록 새 호스트가 진행을 이어받는다.
        if (!wasHost && isHost)
        {
            if (CurrentPhase == Phase.WaitingPlayers) StartCoroutine(HostWaitForPlayers());
            else if (CurrentPhase == Phase.Selection && selectionEndHandled) StartCoroutine(HostFinishSelectionAfterGrace());
        }
    }

    #endregion

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active) target.SetActive(active);
    }
}
