using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// CharSelectUI : 캐릭터 선택 화면  (기존 CharacterSelectionManager 대체)
//
//  기존에는 이 스크립트가 서버 타이머(FusionTimerData), 선택/준비 네트워크 데이터, 하트비트까지 다뤘지만
//  지금은 "화면 표시"만 한다. 진행(타이머, 확정, 자동 선택, 단계 전환)은 MatchFlow 가 한다.
//
//    CharSelect 단계 : 선택 시간 카운트다운 표시 / 캐릭터 고르기 / 준비 버튼
//    Countdown  단계 : "게임 시작 대기" 5초 카운트다운 → 패널이 닫히면 캐릭터 생성 + 카메라 줌인 → 모두 끝나면 본 게임 시작
//                      (선택 시간 30초 = selectionPanel + countdownText / 시작 대기 5초 = startWaitPanel + startWaitCountdownText)
//    Loading / Intro 단계 : (선택) 로딩 화면, 인트로 화면 켜기
//
//  기존과 같은 점
//   - 내 팀 패널만 보이고, 내 팀 이름/색이 표시됨
//   - 준비를 누르면 캐릭터 변경 불가. 준비를 다시 누르면 취소 (마지막 1초 이후엔 불가)
//   - 선택 시간 안에 못 고르면 자동 선택 (※ 기존은 잠긴 캐릭터도 뽑힐 수 있었음 → 지금은 잠긴 캐릭터 제외)
//   - 튜토리얼 연습방(미완료): 카운트다운/팀 문구 숨김, 준비를 누르면 패널이 바로 닫힘
//   - 잠긴 캐릭터는 선택 불가 (튜토리얼 제외)
//
//  버튼 규칙
//   - 캐릭터 버튼은 항상 터치를 받는다. 고를 수 없는 캐릭터는 어둡게 보이고, 누르면 이유를 알림으로 띄운다.
//   - 캐릭터 미선택 + 준비 누름 : "캐릭터를 선택해 주세요" 알림, 준비 완료 안 됨
//   - 캐릭터 선택 + 준비 누름   : 내가 고른 캐릭터까지 모두 어둡게, 버튼 글자는 "준비 취소"
//   - 준비 취소                 : 다시 밝아짐 (캐릭터 변경 가능)
//   - 선택 시간 종료 / 시작 전 카운트다운 : 내가 고른 캐릭터 말고 모두 어둡게
//   - 보유하지 않은 캐릭터       : 잠금 아이콘 + 어둡게 → 누르면 "캐릭터를 보유하고 있지 않습니다."
//   - 랭크전(Point 매치)        : 같은 팀원이 고른 캐릭터는 어둡게 → 누르면 "팀원이 캐릭터를 이미 사용중입니다."
//                                 (일반전 MMR 은 중복 선택 가능. 규칙은 MatchFlow 인스펙터에서 바꿀 수 있음)
//
//  배치: 기존 CharacterSelectionManager 가 붙어 있던 오브젝트
// ─────────────────────────────────────────────────────────────────────────────

public class CharSelectUI : MonoBehaviour
{
    public static CharSelectUI Instance { get; private set; }

    [Header("캐릭터 선택 화면 UI")]
    public GameObject selectionPanel;  // 캐릭터 선택 화면 전체 패널
    public TMP_Text countdownText;     // 선택 남은 시간 글자
    public TMP_Text myTeamText;        // 내 팀 이름 글자 ("1팀" / "2팀", 우리 팀 색 = 파랑)
    public Button readyButton;         // 준비 버튼 (캐릭터를 고르기 전에는 눌리지 않음)
    [Tooltip("준비 버튼 안의 글자. 비워 두면 준비 버튼 자식에서 자동으로 찾습니다. (TMP 글자와 기본 Text 모두 지원)")]
    public TMP_Text readyButtonText;   // 준비 버튼 안의 글자 (비우면 자동으로 찾음)

    [Header("팀원 목록")]
    public Transform redTeamPanel, blueTeamPanel;  // 레드 / 블루 팀원 칸이 생길 부모
    public GameObject redTeamItemPrefab;           // 레드팀 팀원 칸 프리팹 (TeamMemberItem)
    public GameObject blueTeamItemPrefab;          // 블루팀 팀원 칸 프리팹 (TeamMemberItem)
    public GameObject myTeamItemPrefab;            // 내 칸 프리팹 (TeamMemberItem)

    [Header("캐릭터 버튼")]
    public CharacterButton[] characterButtons;  // 캐릭터 선택 버튼들 (각 버튼에 CharacterButton)

    [Header("게임 시작 대기 (캐릭터 선택이 끝난 뒤 마지막 카운트다운)")]
    [Tooltip("Countdown 단계에서만 켜지는 오브젝트. 이때 selectionPanel 은 꺼집니다. 비워 두면 예전처럼 selectionPanel 을 유지합니다.")]
    public GameObject startWaitPanel;         // 선택이 끝난 뒤 시작 카운트다운 때 켜질 패널
    [Tooltip("시작 대기 카운트다운 숫자. 선택 시간 숫자(countdownText)와 다른 글자를 연결하세요.")]
    public TMP_Text startWaitCountdownText;   // 시작 카운트다운 숫자 글자
    [Tooltip("카운트다운이 0 이 된 뒤 패널을 닫기까지 기다리는 시간(초). 패널이 닫히는 연출이 있으면 그 길이로 맞추세요. 패널이 닫힌 직후 캐릭터가 생성되고 줌인합니다.")]
    public float startWaitCloseDelay = 0.5f;  // 카운트다운 0 이후 패널을 닫기까지 대기 시간(초)

    [Header("단계별 화면 (선택)")]
    [Tooltip("Loading 단계에서만 켜지는 화면")]
    public GameObject loadingPanel;  // 로딩 단계에만 켜지는 화면
    [Tooltip("씬에 들어오는 순간부터 Intro 단계가 끝날 때까지 켜져 있는 화면. (캐릭터 선택으로 넘어가면 꺼짐)")]
    public GameObject introPanel;    // 인트로 단계에만 켜지는 화면

    [Header("준비 버튼 글자")]
    public string readyLabel = "준비";             // 준비 전 버튼 글자
    public string readyCancelLabel = "준비 취소";  // 준비한 뒤 버튼 글자

    [Header("알림 (선택할 수 없는 캐릭터를 눌렀을 때 / 캐릭터를 고르지 않고 준비를 눌렀을 때)")]
    [Tooltip("(선택) 알림으로 켰다 끌 오브젝트. 아래 모든 알림이 이 오브젝트를 같이 씁니다. 비워 두면 화면 가운데에 자동으로 글자를 띄웁니다.")]
    public GameObject noCharacterNotice;                           // (선택) 알림 오브젝트 (모든 알림 공용)
    [Tooltip("(선택) 알림 오브젝트 안의 글자. 비워 두면 알림 오브젝트 자식에서 자동으로 찾습니다.")]
    public TMP_Text noCharacterNoticeText;                         // (선택) 알림 오브젝트 안의 글자
    public string noCharacterMessage = "캐릭터를 선택해 주세요.";  // 캐릭터를 안 고르고 준비를 눌렀을 때 문구
    public string notOwnedMessage = "캐릭터를 보유하고 있지 않습니다.";                                // 보유하지 않은 캐릭터를 눌렀을 때 문구
    public string teammateUsingMessage = "팀원이 캐릭터를 이미 사용중입니다.";                         // 랭크전에서 팀원이 쓰는 캐릭터를 눌렀을 때 문구
    public string readyLockedMessage = "준비 완료 상태입니다. 준비를 취소하면 캐릭터를 바꿀 수 있습니다.";  // 준비 완료 상태에서 다른 캐릭터를 눌렀을 때 문구
    public string timeOverMessage = "캐릭터 선택 시간이 끝났습니다.";                                  // 선택 시간이 끝난 뒤 눌렀을 때 문구
    public float noticeSeconds = 1.5f;                             // 알림 표시 시간(초)

    [Header("내 팀 표시 (이름/색은 Resources/TeamColorSettings 에서 바꿈)")]
    [Tooltip("켜면 보이는 내 팀 패널(RedTeamBox/BlueTeamBox)의 배경을 '우리 팀 패널 색'(어두운 파랑)으로 칠합니다.")]
    public bool tintMyTeamPanel = true;  // 켜면 내 팀 패널 배경을 우리 팀 색(파랑)으로 칠함

    [Header("기타")]
    [Tooltip("캐릭터 목록. 비워 두면 PlayerSpawner 의 목록을 사용")]
    public CharacterCatalog catalogOverride;                            // (선택) 캐릭터 목록. 비우면 PlayerSpawner 의 목록 사용
    [SerializeField] private string tutorialSceneKeyword = "Tutorial";  // 씬 이름에 이 글자가 있으면 튜토리얼로 판단
    [Tooltip("잠금 상태를 다시 확인하는 간격(초). 보유 캐릭터 데이터가 늦게 도착해도 반영되게 함")]
    [SerializeField] private float lockRefreshInterval = 1f;            // 캐릭터 잠금 상태를 다시 확인하는 간격(초)

    private MatchFlow flow;
    private InGameNet net;
    private bool subscribed;
    private bool teamTextSet;
    private float nextLockRefreshAt;
    private Text readyLegacyText;           // readyButtonText 가 없을 때 쓰는 기본 UI Text
    private int shownReadyState = -1;       // 마지막으로 표시한 준비 글자 상태 (-1 = 아직 없음)
    private float noticeUntil;              // 이 시각까지 알림 표시
    private string noticeMessage;           // 지금 띄우는 알림 문구
    private float nextItemCheckAt;
    private float closeStartedAt = -1f;     // 카운트다운이 끝나 패널을 닫기 시작한 시각
    private GUIStyle noticeStyle;
    private readonly HashSet<string> warned = new HashSet<string>();

    private readonly Dictionary<uint, TeamMemberItem> items = new Dictionary<uint, TeamMemberItem>();
    private readonly Dictionary<uint, string> shownCharacter = new Dictionary<uint, string>();

    // ── 상태 질의 ───────────────────────────────────────────────────────────
    public bool IsTutorial
    {
        get
        {
            if (SceneManager.GetActiveScene().name.Contains(tutorialSceneKeyword)) return true;
            return flow != null && flow.Config != null && flow.Config.modeType == GameModeType.TUTORIAL;
        }
    }

    /// <summary>튜토리얼 연습방(튜토리얼 미완료)인가: 카운트다운 없이 준비만 누르면 시작</summary>
    public bool IsTutorialPractice
    {
        get { return IsTutorial && UserInfoManager.Instance != null && !UserInfoManager.Instance.IsTutorialCompleted; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    private void Awake()
    {
        Instance = this;
        if (selectionPanel != null) selectionPanel.SetActive(false);
        if (loadingPanel != null) loadingPanel.SetActive(false);
        if (introPanel != null) introPanel.SetActive(true);          // 씬에 들어오자마자 인트로 화면
        if (startWaitPanel != null) startWaitPanel.SetActive(false);
    }

    private void Start()
    {
        net = InGameNet.Ensure();
        flow = MatchFlow.Instance;
        if (flow == null) { NetLog.Error("CharSelectUI: MatchFlow 가 씬에 없습니다."); enabled = false; return; }

        if (readyButton != null)
        {
            readyButton.onClick.RemoveListener(OnClick_Ready);
            readyButton.onClick.AddListener(OnClick_Ready);

            // 준비 버튼 자식에서 글자 찾기
            if (readyButtonText == null) readyButtonText = readyButton.GetComponentInChildren<TMP_Text>(true);
            if (readyButtonText == null) readyLegacyText = readyButton.GetComponentInChildren<Text>(true);
        }

        if (characterButtons != null)
            foreach (var b in characterButtons) if (b != null) b.Setup(this);

        if (noCharacterNotice != null)
        {
            if (noCharacterNoticeText == null) noCharacterNoticeText = noCharacterNotice.GetComponentInChildren<TMP_Text>(true);
            noCharacterNotice.SetActive(false);
        }

        flow.OnPhaseChanged += HandlePhaseChanged;
        flow.OnCharacterPicked += HandleCharacterPicked;
        flow.OnLocalPickRejected += HandleLocalPickRejected;
        net.PlayersChanged += SyncItems;
        flow.autoPickProvider = PickRandomAvailable;
        subscribed = true;

        SyncItems();
    }

    private void OnDestroy()
    {
        if (subscribed)
        {
            if (flow != null)
            {
                flow.OnPhaseChanged -= HandlePhaseChanged;
                flow.OnCharacterPicked -= HandleCharacterPicked;
                flow.OnLocalPickRejected -= HandleLocalPickRejected;
                if (flow.autoPickProvider == (System.Func<string>)PickRandomAvailable) flow.autoPickProvider = null;
            }
            if (net != null) net.PlayersChanged -= SyncItems;
        }
        if (Instance == this) Instance = null;
    }

    private void HandlePhaseChanged(MatchPhase phase, bool restored)
    {
        RefreshTeamPanels();
        if (phase == MatchPhase.CharSelect) SyncItems();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 매 프레임

    private void Update()
    {
        if (flow == null || net == null) return;

        var phase = flow.Phase;
        if (loadingPanel != null) loadingPanel.SetActive(phase == MatchPhase.Loading);
        // 인트로 화면: 씬 입장(Loading) ~ Intro 단계 동안 계속 켜 둔다.
        if (introPanel != null) introPanel.SetActive(phase == MatchPhase.Loading || phase == MatchPhase.Intro);

        // 씬에 들어오자마자(내 팀이 정해지는 즉시) 내 팀 패널만 켠다.
        RefreshTeamPanels();
        UpdateNotice();

        bool selecting = phase == MatchPhase.CharSelect;
        bool finalCountdown = phase == MatchPhase.Countdown;
        if (!selecting && !finalCountdown)
        {
            if (selectionPanel != null && selectionPanel.activeSelf) selectionPanel.SetActive(false);
            SetStartWait(false);
            return;
        }

        var me = net.LocalPlayer;
        bool myReady = me != null && flow.IsConfirmed(me.SessionId);
        string mySelected = me != null ? me.CharacterId : null;

        UpdateReadyLabel(myReady);

        // 명단과 칸 개수가 다르면 맞춘다 (매 프레임 말고 0.5초마다 확인)
        if (Time.unscaledTime >= nextItemCheckAt)
        {
            nextItemCheckAt = Time.unscaledTime + 0.5f;
            if (items.Count != CountDisplayablePlayers()) SyncItems();
        }

        if (Time.unscaledTime >= nextLockRefreshAt)
        {
            nextLockRefreshAt = Time.unscaledTime + lockRefreshInterval;
            if (characterButtons != null)
                foreach (var b in characterButtons) if (b != null) b.RefreshLock(IsTutorial);
        }

        bool practice = IsTutorialPractice;
        if (practice)
        {
            // 연습방: 카운트다운/팀 문구 숨김, 준비를 누르면 패널이 바로 닫힘
            if (countdownText != null && countdownText.gameObject.activeSelf) countdownText.gameObject.SetActive(false);
            if (myTeamText != null && myTeamText.gameObject.activeSelf) myTeamText.gameObject.SetActive(false);
            if (readyButton != null) readyButton.interactable = !myReady;
            UpdateCharacterButtons(mySelected, myReady, false);
            SetPanel(!myReady && selecting);
            SetStartWait(false);
            if (finalCountdown && flow.CountdownFinished) flow.BeginSpawnStage();     // 연습방은 패널 연출 없이 바로 캐릭터 생성
            UpdateItems();
            return;
        }

        bool timed = flow.HasTimeLimit;
        float remain = flow.RemainingSeconds;
        // 마지막 1초 / 시작 전 카운트다운에는 변경 잠금
        bool finalLock = finalCountdown || (timed && remain <= 1f);

        // 준비 버튼은 캐릭터를 고르지 않아도 눌려야 한다 (눌렀을 때 "캐릭터를 선택해 주세요" 알림).
        // 마지막 카운트다운에는 준비/취소 모두 불가.
        if (readyButton != null) readyButton.interactable = !finalLock;
        UpdateCharacterButtons(mySelected, myReady, finalLock);

        // 시작 대기 오브젝트를 연결했으면 단계마다 다른 오브젝트/글자를 쓴다. (30초 = 선택 패널, 5초 = 시작 대기)
        bool separateStartWait = startWaitPanel != null || startWaitCountdownText != null;
        if (finalCountdown)
        {
            if (!flow.CountdownFinished)
            {
                // 5초 카운트다운 진행 중
                closeStartedAt = -1f;
                if (separateStartWait)
                {
                    SetPanel(false);
                    SetStartWait(true);
                    if (startWaitCountdownText != null) startWaitCountdownText.text = Mathf.CeilToInt(remain).ToString();
                }
                else
                {
                    SetPanel(true);
                    SetStartWait(false);
                    if (countdownText != null) countdownText.text = Mathf.CeilToInt(remain).ToString();
                }
            }
            else
            {
                // 카운트다운 끝: 0 을 잠깐 보여 주고(startWaitCloseDelay) 패널을 완전히 닫은 뒤 캐릭터 생성 + 줌인
                if (closeStartedAt < 0f) closeStartedAt = Time.unscaledTime;
                if (separateStartWait && startWaitCountdownText != null) startWaitCountdownText.text = "0";
                else if (!separateStartWait && countdownText != null) countdownText.text = "0";

                if (Time.unscaledTime - closeStartedAt >= startWaitCloseDelay)
                {
                    SetPanel(false);
                    SetStartWait(false);
                    flow.BeginSpawnStage();
                }
            }
        }
        else
        {
            closeStartedAt = -1f;
            SetPanel(true);
            SetStartWait(false);
            if (countdownText != null)
                countdownText.text = timed ? Mathf.CeilToInt(remain).ToString() : string.Empty;
        }

        UpdateItems();
    }

    /// <summary>준비 버튼 글자: 준비 전에는 "준비", 준비 후에는 "준비 취소"</summary>
    private void UpdateReadyLabel(bool myReady)
    {
        int state = myReady ? 1 : 0;
        if (state == shownReadyState) return;
        shownReadyState = state;

        string label = myReady ? readyCancelLabel : readyLabel;
        if (readyButtonText != null) readyButtonText.text = label;
        else if (readyLegacyText != null) readyLegacyText.text = label;
    }

    private void SetPanel(bool active)
    {
        if (selectionPanel != null && selectionPanel.activeSelf != active) selectionPanel.SetActive(active);
    }

    /// <summary>게임 시작 대기(5초) 오브젝트 켜기/끄기</summary>
    private void SetStartWait(bool active)
    {
        if (startWaitPanel != null && startWaitPanel.activeSelf != active) startWaitPanel.SetActive(active);
    }

    /// <summary>
    /// 캐릭터 버튼 상태 규칙 (버튼은 항상 터치를 받고, 고를 수 없으면 어둡게 + 누르면 알림)
    ///   잠긴(미보유) 캐릭터      : 어둡게 + 잠금 아이콘 (버튼이 직접 판단)
    ///   준비 완료               : 내가 고른 캐릭터까지 모두 어둡게 (선택 표시는 밝게 유지)
    ///   선택 시간 종료 / 마지막 카운트다운 (준비 전) : 내가 고른 캐릭터 말고 모두 어둡게
    ///   랭크전에서 팀원이 고른 캐릭터 : 어둡게
    ///   내가 고른 캐릭터(준비 전)  : 밝게 + 선택 표시
    /// </summary>
    private void UpdateCharacterButtons(string mySelectedId, bool myReady, bool finalLock)
    {
        if (characterButtons == null) return;
        foreach (var btn in characterButtons)
        {
            if (btn == null || btn.characterDef == null) continue;

            bool isMyPick = !string.IsNullOrEmpty(mySelectedId) && btn.CharacterId == mySelectedId;
            btn.SetSelectedMark(isMyPick);

            var reason = CharacterBlockReason.None;
            if (myReady) reason = finalLock ? CharacterBlockReason.TimeOver : CharacterBlockReason.ReadyLocked;   // 준비 완료: 전부 어둡게
            else if (isMyPick) reason = CharacterBlockReason.None;
            else if (finalLock) reason = CharacterBlockReason.TimeOver;
            else if (flow != null && flow.IsTakenByTeammate(btn.CharacterId)) reason = CharacterBlockReason.TeammateUsing;
            btn.SetBlock(reason);
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 입력

    /// <summary>[CharacterButton] 캐릭터 선택 요청</summary>
    public void Select(string characterId)
    {
        if (flow == null || string.IsNullOrEmpty(characterId)) return;
        var me = net != null ? net.LocalPlayer : null;
        if (me != null && me.CharacterId == characterId) return;     // 이미 고른 캐릭터
        flow.SelectCharacter(characterId);
    }

    /// <summary>[CharacterButton] 고를 수 없는 캐릭터를 눌렀을 때 이유를 알림으로 띄운다.</summary>
    public void ShowBlockedNotice(CharacterBlockReason reason)
    {
        switch (reason)
        {
            case CharacterBlockReason.NotOwned: ShowNotice(notOwnedMessage); break;
            case CharacterBlockReason.TeammateUsing: ShowNotice(teammateUsingMessage); break;
            case CharacterBlockReason.ReadyLocked: ShowNotice(readyLockedMessage); break;
            case CharacterBlockReason.TimeOver: ShowNotice(timeOverMessage); break;
        }
    }

    /// <summary>[MatchFlow] 내가 고른 캐릭터를 팀원이 한발 먼저 골라서 거절됐다 (랭크전, 동시에 눌렀을 때)</summary>
    private void HandleLocalPickRejected(string characterId)
    {
        ShowNotice(teammateUsingMessage);
    }

    /// <summary>[준비 버튼] 준비/준비 취소. 캐릭터를 고르지 않았으면 알림만 띄우고 준비는 되지 않는다.</summary>
    public void OnClick_Ready()
    {
        if (flow == null || net == null) return;
        var me = net.LocalPlayer;
        if (me == null) return;

        bool alreadyReady = flow.IsConfirmed(me.SessionId);
        if (!alreadyReady && string.IsNullOrEmpty(me.CharacterId))
        {
            ShowNotice(noCharacterMessage);
            return;
        }
        flow.SetReady(!alreadyReady);
    }

    // ── 알림 ────────────────────────────────────────────────────────────────
    private void ShowNotice(string message)
    {
        if (string.IsNullOrEmpty(message)) return;
        noticeMessage = message;
        noticeUntil = Time.unscaledTime + Mathf.Max(0.3f, noticeSeconds);
        if (noCharacterNotice != null)
        {
            if (noCharacterNoticeText != null) noCharacterNoticeText.text = message;
            noCharacterNotice.SetActive(true);
        }
    }

    private void UpdateNotice()
    {
        if (noCharacterNotice != null && noCharacterNotice.activeSelf && Time.unscaledTime >= noticeUntil)
            noCharacterNotice.SetActive(false);
    }

    // 알림 오브젝트를 연결하지 않았을 때 쓰는 기본 알림 (화면 가운데 글자)
    private void OnGUI()
    {
        if (noCharacterNotice != null || Time.unscaledTime >= noticeUntil) return;

        if (noticeStyle == null)
        {
            noticeStyle = new GUIStyle(GUI.skin.box);
            noticeStyle.alignment = TextAnchor.MiddleCenter;
            noticeStyle.fontStyle = FontStyle.Bold;
            noticeStyle.wordWrap = true;
        }
        noticeStyle.fontSize = Mathf.Max(18, Screen.height / 28);

        float w = Screen.width * 0.6f, h = Screen.height * 0.12f;
        GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height * 0.4f, w, h), noticeMessage ?? noCharacterMessage, noticeStyle);
    }

    /// <summary>시간 안에 못 고른 내 캐릭터를 정한다. 잠기지 않은 캐릭터 중 무작위 (랭크전이면 팀원이 쓰는 캐릭터도 제외)</summary>
    private string PickRandomAvailable()
    {
        var candidates = new List<string>();
        if (characterButtons != null)
        {
            foreach (var b in characterButtons)
            {
                if (b == null || b.characterDef == null) continue;
                b.RefreshLock(IsTutorial);
                if (!b.IsLocked && !IsTakenByTeammate(b.CharacterId)) candidates.Add(b.CharacterId);
            }
        }
        if (candidates.Count > 0) return candidates[Random.Range(0, candidates.Count)];

        var catalog = GetCatalog();
        if (catalog != null)
        {
            string id = catalog.RandomId(cid => IsTakenByTeammate(cid)
                || (!IsTutorial && UserCharacterDataManager.Instance != null && UserCharacterDataManager.Instance.IsCharacterLocked(cid)));
            if (!string.IsNullOrEmpty(id)) return id;
        }
        return flow != null && flow.Config != null ? flow.Config.defaultCharacterId : string.Empty;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 팀원 칸

    /// <summary>랭크전에서 내 팀원이 이미 쓰는 캐릭터인가 (일반전이면 항상 false)</summary>
    private bool IsTakenByTeammate(string characterId)
    {
        return flow != null && flow.IsTakenByTeammate(characterId);
    }

    private CharacterCatalog GetCatalog()
    {
        return catalogOverride != null ? catalogOverride
            : (PlayerSpawner.Instance != null ? PlayerSpawner.Instance.catalog : null);
    }

    private int CountDisplayablePlayers()
    {
        int n = 0;
        foreach (var p in net.Players) if (!p.IsForfeited && p.Team != NetTeam.None) n++;
        return n;
    }

    /// <summary>명단과 칸을 맞춘다 (추가/제거). 칸 하나가 실패해도 나머지 유저 칸은 계속 만든다.</summary>
    private void SyncItems()
    {
        if (net == null) return;

        // 사라진/이탈한 유저 칸, 그리고 이미 파괴된(null) 칸 제거
        var remove = new List<uint>();
        foreach (var kv in items)
        {
            var p = net.GetPlayer(kv.Key);
            if (kv.Value == null || p == null || p.IsForfeited) remove.Add(kv.Key);
        }
        foreach (var id in remove)
        {
            if (items[id] != null) Destroy(items[id].gameObject);
            items.Remove(id);
            shownCharacter.Remove(id);
        }

        foreach (var p in net.Players)
        {
            if (p.IsForfeited || p.Team == NetTeam.None) continue;
            if (items.ContainsKey(p.SessionId)) continue;
            try { AddItem(p); }
            catch (System.Exception e) { NetLog.Error("CharSelectUI: '" + p.Nickname + "' 팀원 칸 생성 실패: " + e.Message); }
        }

        // 명단과 칸 수가 다르면 원인을 찾기 쉽게 한 번 기록
        int expected = CountDisplayablePlayers();
        if (items.Count != expected)
            WarnOnce("count" + items.Count + "/" + expected, "CharSelectUI: 명단 " + expected + "명인데 팀원 칸은 " + items.Count + "개입니다. (프리팹/패널 연결 확인)");

        ApplyMyTeamVisibility();
        UpdateItems();
    }

    private void AddItem(NetPlayer player)
    {
        bool red = player.Team == NetTeam.Red;
        Transform parent = red ? redTeamPanel : blueTeamPanel;
        GameObject prefab = (player.IsLocal && myTeamItemPrefab != null) ? myTeamItemPrefab
            : (red ? redTeamItemPrefab : blueTeamItemPrefab);
        if (parent == null || prefab == null)
        {
            WarnOnce("noprefab" + player.Team + player.IsLocal,
                "CharSelectUI: 팀원 칸 프리팹 또는 팀 패널이 비어 있습니다. (" + (red ? "Red" : "Blue") + (player.IsLocal ? " / 내 칸" : "") + ") 인스펙터를 확인하세요.");
            return;
        }

        var go = Instantiate(prefab, parent);
        var item = go.GetComponent<TeamMemberItem>();
        if (item == null) item = go.GetComponentInChildren<TeamMemberItem>(true);
        if (item == null)
        {
            WarnOnce("noitem" + prefab.name, "CharSelectUI: '" + prefab.name + "' 프리팹에 TeamMemberItem 스크립트가 없습니다. (옛 TeamMemberItem 이 아닌 새 스크립트를 붙여야 합니다)");
            Destroy(go);
            return;
        }

        // 프리팹에 저장된 위치 z 값(예: -57176)이 남아 있으면 카메라 캔버스에서 안 보일 수 있다 → 항상 0 으로
        var rect = go.transform as RectTransform;
        if (rect != null)
        {
            var pos = rect.anchoredPosition3D;
            pos.z = 0f;
            rect.anchoredPosition3D = pos;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
        }
        go.name = prefab.name + "_" + player.Nickname;
        if (!go.activeSelf) go.SetActive(true);

        item.SessionId = player.SessionId;
        item.SetNickname(player.Nickname);
        item.InitializeDefault();
        items[player.SessionId] = item;
    }

    private void HandleCharacterPicked(NetPlayer player)
    {
        if (player == null) return;
        RefreshItem(player);
    }

    /// <summary>모든 칸의 준비/끊김/캐릭터 미리보기 갱신</summary>
    private void UpdateItems()
    {
        foreach (var kv in items)
        {
            var p = net.GetPlayer(kv.Key);
            if (p != null) RefreshItem(p);
        }
    }

    private void RefreshItem(NetPlayer player)
    {
        TeamMemberItem item;
        if (!items.TryGetValue(player.SessionId, out item) || item == null) return;

        // 닉네임은 매번 확인한다 (명단 정보가 늦게 바뀌어도 따라가도록). 색: 나 노랑 / 같은 팀 파랑
        item.SetNickname(player.Nickname);
        item.SetNicknameColor(TeamView.ColorOf(player));
        item.SetReady(flow != null && flow.IsConfirmed(player.SessionId));
        item.SetDisconnected(!player.IsOnline);

        string shown;
        shownCharacter.TryGetValue(player.SessionId, out shown);
        string current = player.CharacterId;

        if (string.IsNullOrEmpty(current))
        {
            if (!string.IsNullOrEmpty(shown))
            {
                item.ClearCharacterVisual();
                shownCharacter.Remove(player.SessionId);
            }
            return;
        }
        if (current == shown) return;

        // 카탈로그에서 먼저 찾고, 없으면 캐릭터 버튼에 연결된 정의서에서 찾는다.
        CharacterDef def = null;
        var catalog = GetCatalog();
        if (catalog != null) def = catalog.Get(current);
        if (def == null) def = FindDefInButtons(current);

        if (def == null)
        {
            WarnOnce("nodef" + current, "CharSelectUI: 캐릭터 '" + current + "' 의 CharacterDef 를 찾지 못했습니다. (카탈로그/캐릭터 버튼의 characterId 확인)");
            return;      // 다음 프레임에 다시 시도
        }
        if (def.uiPrefab == null && def.characterIcon == null)
            WarnOnce("noui" + current, "CharSelectUI: '" + def.name + "' 에 uiPrefab 도 icon 도 없어 팀원 칸에 표시할 것이 없습니다.");

        item.SetCharacterVisual(current, def.uiPrefab, def.characterIcon);
        shownCharacter[player.SessionId] = current;
    }

    private void WarnOnce(string key, string message)
    {
        if (warned.Add(key)) NetLog.Warn(message);
    }

    private CharacterDef FindDefInButtons(string id)
    {
        if (characterButtons == null) return null;
        foreach (var b in characterButtons)
            if (b != null && b.characterDef != null && b.CharacterId == id) return b.characterDef;
        return null;
    }

    /// <summary>
    /// 내 팀 패널을 켠다. 씬에 들어오자마자(내 팀이 정해지면 바로) 한 번 적용하고,
    /// 내 팀을 아직 모르면 다음 프레임에 다시 시도한다.
    /// </summary>
    private void RefreshTeamPanels()
    {
        if (!teamTextSet) ApplyMyTeamVisibility();
    }

    /// <summary>내 팀 패널만 보이게 하고 팀 이름("1팀"/"2팀")을 우리 팀 색(파랑)으로 표시</summary>
    private void ApplyMyTeamVisibility()
    {
        var me = net != null ? net.LocalPlayer : null;
        if (me == null || me.Team == NetTeam.None) return;

        bool red = me.Team == NetTeam.Red;
        if (redTeamPanel != null) redTeamPanel.gameObject.SetActive(red);
        if (blueTeamPanel != null) blueTeamPanel.gameObject.SetActive(!red);

        // 보이는 패널(내 팀)의 배경을 우리 팀 색으로
        if (tintMyTeamPanel)
        {
            var shown = red ? redTeamPanel : blueTeamPanel;
            var panelImage = shown != null ? shown.GetComponent<Image>() : null;
            if (panelImage != null) panelImage.color = TeamView.Settings.allyPanelColor;
        }

        if (myTeamText != null && !IsTutorialPractice)
        {
            myTeamText.text = TeamView.TeamName(me.Team);
            myTeamText.color = TeamView.Settings.allyColor;
        }
        teamTextSet = true;
    }

    #endregion
}
