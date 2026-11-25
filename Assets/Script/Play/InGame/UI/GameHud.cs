using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// GameHud : 게임 화면의 모든 UI 를 관리  (기존 GamePlayUIController 대체)
//
//  ▶ 달라진 점 (기존 → 지금)
//    - 기존: 모드 핸들러/스킬/체력 스크립트가 UI 를 직접 호출 (UpdateTimer, InitTDMUI, ShowResultUI ...)
//      지금: HUD 가 스스로 "모드·내 캐릭터·MatchFlow" 를 바라보고 갱신한다. (다른 스크립트는 HUD 를 몰라도 됨)
//    - 입력 : 조이스틱/점프/스킬/재장전은 이벤트로, 공격은 AttackHeld 값으로 PlayerInput 에 전달
//    - 필드 이름은 기존 GamePlayUIController 와 같게 유지했으므로 인스펙터 연결을 그대로 옮기면 됩니다.
//
//  배치: Canvas 아래 기존 GamePlayUIController 가 붙어 있던 오브젝트에 이 스크립트를 붙인다.
// ─────────────────────────────────────────────────────────────────────────────

public class GameHud : MonoBehaviour
{
    public static GameHud Instance { get; private set; }

    // ── 입력 (PlayerInput 이 읽는다) ─────────────────────────────────────────
    public event Action OnJumpClicked;
    public event Action OnSkillClicked;
    public event Action OnReloadClicked;
    /// <summary>공격 버튼을 누르고 있는가</summary>
    public bool AttackHeld { get { return attackHold != null && attackHold.IsHeld && attackButton != null && attackButton.activeInHierarchy; } }

    [Header("팀 점수 TEXT (파랑 = 항상 우리 팀, 빨강 = 항상 상대 팀)")]
    public TMP_Text redTeamScoreText;       // 빨강 점수 글자 = 상대 팀 점수
    public TMP_Text blueTeamScoreText;      // 파랑 점수 글자 = 우리 팀 점수
    [Tooltip("{0} 자리에 점수가 들어갑니다. 색/팀 이름 태그를 넣어 둘 수 있습니다.")]
    public string redScoreFormat = "{0}";   // 빨강(상대 팀) 점수 표시 형식 ({0} = 점수)
    public string blueScoreFormat = "{0}";  // 파랑(우리 팀) 점수 표시 형식 ({0} = 점수)

    [Header("버튼 UI")]
    public GameObject joystick;      // 이동 조이스틱
    public GameObject jumpButton;    // 점프 버튼
    public GameObject attackButton;  // 공격 버튼 (누르고 있으면 연사)
    public GameObject reloadButton;  // 재장전 버튼
    public GameObject skillButton;   // 스킬 버튼 (FALL 모드에서는 숨김)

    [Header("폭탄 상호작용 버튼 (BOMB)")]
    [SerializeField] private GameObject bombInteractButton;  // 폭탄 설치/해제 버튼 (BOMB)
    [SerializeField] private GameObject bombProgressRoot;    // 설치/해제 진행도 묶음
    [SerializeField] private TMP_Text bombProgressLabel;     // 진행도 글자
    [SerializeField] private Image bombProgressFill;         // 진행도 채움 이미지
    public string plantButtonLabel = "폭탄 설치";            // 설치 버튼 글자
    public string defuseButtonLabel = "폭탄 해제";           // 해제 버튼 글자

    [Header("스코어 및 프로필 UI")]
    public GameObject settingsButton;               // 설정 버튼 (게임 중 표시)
    public GameObject myStatusParent;               // 본인 상태 UI가 붙을 부모
    public GameObject myStatusItemPrefab;           // MyStatusItem 프리팹
    public GameObject scoreUI;                      // 상단 점수판 묶음

    [Header("모드별 UI")]
    [SerializeField] private GameObject timerUI;  // 남은 시간 표시 묶음
    [SerializeField] private TMP_Text timerText;  // 남은 시간 글자
    [SerializeField] private GameObject roundUI;  // 라운드 표시 묶음 (BOMB)
    [SerializeField] private TMP_Text roundText;  // 라운드 글자
    [SerializeField] private GameObject goalUI;   // 목표 표시 묶음
    [SerializeField] private TMP_Text goalText;   // 목표 글자 (예: 목표 처치 : 30킬)

    [Header("리로드/스킬 버튼")]
    public TMP_Text reloadButtonText;               // 남은 탄 글자 ("현재 / 최대")
    public Image reloadButtonImage;                 // 재장전 진행 채움 이미지 (Image Type = Filled, Radial)
    public Image skillButtonImage;                  // 스킬 아이콘 + 쿨타임 fill

    [Header("사망 UI")]
    [SerializeField] private GameObject deathUI;                // 죽었을 때 켜지는 화면
    [SerializeField] private TMP_Text deathText;                // 부활 카운트다운 글자
    public string respawnCountdownFormat = "부활까지 {0}초";    // 부활 문구 ({0} = 남은 초)
    [Tooltip("부활이 없는 모드(BOMB)에서 죽었을 때 표시할 문구")]
    public string noRespawnText = "다음 라운드를 기다리는 중";  // 부활이 없는 모드(BOMB)에서 죽었을 때 문구

    [Header("결과 패널 UI")]
    public GameObject resultPanel;                  // 결과 전체 패널
    public GameObject myTeamWinImage;               // 내 팀 승 이미지
    public GameObject myTeamNotWinImage;            // 내 팀 패 이미지
    [Tooltip("(선택) 무승부 이미지. 비워 두면 무승부도 '패' 이미지를 사용 (기존 동작)")]
    public GameObject drawImage;                    // (선택) 무승부 이미지
    public Transform redResultContent;              // 빨강 리스트 부모 = 상대 팀 결과
    public Transform blueResultContent;             // 파랑 리스트 부모 = 우리 팀 결과
    public GameObject resultItemPrefab;             // ResultMemberItem 프리팹
    public TMP_Text myRewardText;                   // 내 보상 텍스트
    [Tooltip("캐릭터 아이콘을 찾을 목록. 비워 두면 PlayerSpawner 의 목록을 사용")]
    public CharacterCatalog catalogOverride;        // (선택) 아이콘을 찾을 캐릭터 목록. 비우면 PlayerSpawner 의 목록 사용

    [Header("공용 알림 UI")]
    [SerializeField] private GameObject alertRoot;  // 화면 가운데 알림 묶음
    [SerializeField] private TMP_Text alertText;    // 알림 글자
    public float alertSeconds = 2f;                 // 알림 표시 시간(초)

    [Header("킬로그")]
    [Tooltip("킬로그 줄이 생성될 부모. VerticalLayoutGroup 을 붙여 두면 위에서부터 차례로 쌓인다.")]
    public Transform killFeedParent;                // 킬로그 줄이 생성될 부모 (VerticalLayoutGroup 권장)
    [Tooltip("내가 킬했을 때 쓰는 프리팹 (가해자 아이콘·닉네임 / 무기·스킬 아이콘 / 피해자 아이콘·닉네임)")]
    public KillFeedItem myKillItemPrefab;           // 킬 프리팹: 내가 킬했을 때
    [Tooltip("같은 팀원이 킬했을 때 쓰는 프리팹 (칸 구성은 위와 같음)")]
    public KillFeedItem allyKillItemPrefab;         // 킬 프리팹: 같은 팀이 킬했을 때
    [Tooltip("적 팀이 킬했을 때 쓰는 프리팹 (칸 구성은 위와 같음)")]
    public KillFeedItem enemyKillItemPrefab;        // 킬 프리팹: 적 팀이 킬했을 때
    [Tooltip("낙사/함정/폭발 등 스스로 죽었을 때 쓰는 프리팹 (죽은 유저의 캐릭터 아이콘 + 닉네임 + 사인 아이콘)")]
    public KillFeedItem selfDeathItemPrefab;        // 자멸 프리팹 (낙사/함정/폭발 등, FALL 모드 포함)
    public int maxKillFeedItems = 4;                // 동시에 보이는 최대 줄 수 (넘치면 가장 오래된 줄 삭제)
    [Tooltip("0 이면 시간이 지나도 사라지지 않고, 최대 개수를 넘칠 때만 삭제된다.")]
    public float killFeedSeconds = 0f;              // 이 시간(초)이 지나면 줄 자동 삭제 (0 = 자동 삭제 안 함)
    public Sprite fallDeathIcon;                    // 자멸 프리팹 사인 아이콘: 낙사
    public Sprite hazardDeathIcon;                  // 자멸 프리팹 사인 아이콘: 함정 (가시 등)
    public Sprite explosionDeathIcon;               // 자멸 프리팹 사인 아이콘: 폭발
    public Sprite defaultDeathIcon;                 // 자멸 프리팹 사인 아이콘: 그 밖 (위 칸이 비어 있을 때도 사용)

    [Header("연결 상태 알림 (선택)")]
    [Tooltip("내 연결이 끊겨 재접속 중일 때 켜지는 배너")]
    [SerializeField] private GameObject reconnectBanner;                           // 내 연결이 끊겨 재접속 중일 때 켜지는 배너
    [SerializeField] private TMP_Text reconnectBannerText;                         // 재접속 배너 글자
    public string reconnectingFormat = "연결이 끊겼습니다. 재접속 중... ({0}초)";  // 재접속 중 문구 ({0} = 남은 초)
    public string opponentDisconnectedFormat = "{0} 님의 연결이 끊겼습니다.";      // 상대 연결 끊김 문구 ({0} = 닉네임)
    public string opponentReconnectedFormat = "{0} 님이 다시 접속했습니다.";       // 상대 재접속 문구 ({0} = 닉네임)
    public string opponentForfeitedFormat = "{0} 님이 게임에서 이탈했습니다.";     // 상대 이탈 문구 ({0} = 닉네임)
    public string reconnectFailedMessage = "연결할 수 없어 로비로 돌아갑니다.";    // 재접속 실패 문구
    public float returnToLobbyAfterFailSeconds = 3f;                               // 재접속 실패 후 로비로 이동하기까지(초)

    [Header("튜토리얼")]
    [SerializeField] private string tutorialSceneKeyword = "Tutorial";  // 씬 이름에 이 글자가 있으면 튜토리얼로 판단

    // ── 내부 ────────────────────────────────────────────────────────────────
    private HoldButton attackHold;
    private PlayerEntity bound;
    private bool gameplayShown;
    private bool resultShown;
    private bool modeUiInitialized;
    private int lastRed = -1, lastBlue = -1;
    private Coroutine alertRoutine;
    private GameModeBase subscribedMode;
    private ResultReporter subscribedReporter;
    private NetConnectionManager subscribedConnection;
    private MatchFlow flow;
    private bool failHandled;
    private bool reconnectBannerShowing;
    private MatchState subscribedState;
    private readonly List<KillFeedItem> killFeed = new List<KillFeedItem>();   // 0번 = 가장 최근 줄

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    private void Awake()
    {
        Instance = this;

        HideAllModeUI();
        HideGameplayUI();
        if (settingsButton != null) settingsButton.SetActive(false);
        if (myStatusParent != null) myStatusParent.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(false);
        if (myTeamWinImage != null) myTeamWinImage.SetActive(false);
        if (myTeamNotWinImage != null) myTeamNotWinImage.SetActive(false);
        if (drawImage != null) drawImage.SetActive(false);
        if (myRewardText != null) myRewardText.text = string.Empty;
        if (deathUI != null) deathUI.SetActive(false);
        if (alertRoot != null) alertRoot.SetActive(false);
        if (bombInteractButton != null) bombInteractButton.SetActive(false);
        if (bombProgressRoot != null) bombProgressRoot.SetActive(false);
        if (reconnectBanner != null) reconnectBanner.SetActive(false);

        BindButtons();
    }

    private void Start()
    {
        flow = MatchFlow.Instance;
        if (flow != null)
        {
            flow.OnPhaseChanged += HandlePhaseChanged;
            if (flow.Phase == MatchPhase.Playing) InitModeUi();
            else if (flow.Phase == MatchPhase.Result) ShowResultUi();
        }

        var cam = CameraZoomController.Instance;
        if (cam != null)
        {
            cam.OnZoomInCompleted += HandleZoomInCompleted;
            if (cam.ZoomInDone) HandleZoomInCompleted();
        }

        var conn = InGameNet.Instance != null ? InGameNet.Instance.Connection : null;
        if (conn != null)
        {
            subscribedConnection = conn;
            conn.OnRemoteDisconnected += HandleRemoteDisconnected;
            conn.OnRemoteReconnected += HandleRemoteReconnected;
            conn.OnRemoteForfeited += HandleRemoteForfeited;
            conn.OnLocalReconnectFailed += HandleLocalReconnectFailed;
        }

        TrySubscribeKillLog();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (flow != null) flow.OnPhaseChanged -= HandlePhaseChanged;

        var cam = CameraZoomController.Instance;
        if (cam != null) cam.OnZoomInCompleted -= HandleZoomInCompleted;

        if (subscribedMode != null) subscribedMode.OnAlert -= ShowAlert;
        if (subscribedReporter != null) subscribedReporter.OnRewardResolved -= HandleRewardResolved;
        if (subscribedState != null) subscribedState.OnKillLog -= HandleKillLog;

        if (subscribedConnection != null)
        {
            subscribedConnection.OnRemoteDisconnected -= HandleRemoteDisconnected;
            subscribedConnection.OnRemoteReconnected -= HandleRemoteReconnected;
            subscribedConnection.OnRemoteForfeited -= HandleRemoteForfeited;
            subscribedConnection.OnLocalReconnectFailed -= HandleLocalReconnectFailed;
        }
        Unbind();
    }

    private void BindButtons()
    {
        if (attackButton != null) attackHold = HoldButton.GetOrAdd(attackButton);

        BindClick(jumpButton, () => { if (OnJumpClicked != null) OnJumpClicked(); });
        BindClick(skillButton, () => { if (OnSkillClicked != null) OnSkillClicked(); });
        BindClick(reloadButton, () => { if (OnReloadClicked != null) OnReloadClicked(); });
        BindClick(bombInteractButton, () =>
        {
            if (BombMode.Instance != null) BombMode.Instance.ToggleInteract();
        });
    }

    /// <summary>Button 이 있으면 onClick, 없으면 터치 즉시(HoldButton.Pressed)로 연결</summary>
    private static void BindClick(GameObject target, Action action)
    {
        if (target == null) return;
        var btn = target.GetComponent<Button>();
        if (btn == null) btn = target.GetComponentInChildren<Button>(true);
        if (btn != null)
        {
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => action());
        }
        else
        {
            HoldButton.GetOrAdd(target).Pressed += action;
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 외부(PlayerInput 등)용

    public FixedJoystick GetJoystick()
    {
        return joystick != null ? joystick.GetComponent<FixedJoystick>() : null;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 매 프레임

    private void Update()
    {
        // 내 캐릭터가 생기거나 바뀌면 연결
        var local = PlayerEntity.Local;
        if (local != bound) Bind(local);

        // MatchState 가 늦게 준비되는 경우 대비: 킬로그 연결
        if (subscribedState == null) TrySubscribeKillLog();

        // 줌 컨트롤러가 없는 씬 대비: 캐릭터가 준비되면 바로 UI 표시
        if (!gameplayShown && CameraZoomController.Instance == null && local != null && local.IsInitialized
            && flow != null && flow.Phase == MatchPhase.Playing)
        {
            HandleZoomInCompleted();
        }

        // 모드 UI 는 모드 스크립트가 준비된 뒤 한 번만
        if (!modeUiInitialized && flow != null && flow.Phase == MatchPhase.Playing && GameModeBase.Current != null)
            InitModeUi();

        UpdateTimer();
        UpdateScore();
        UpdateSkillFill();
        UpdateDeathUi();
        UpdateBombUi();
        UpdateReconnectBanner();
    }

    private void UpdateTimer()
    {
        var mode = GameModeBase.Current;
        if (mode == null || timerText == null || timerUI == null || !timerUI.activeSelf) return;
        timerText.text = FormatTime(mode.DisplaySeconds);
    }

    private void UpdateScore()
    {
        var mode = GameModeBase.Current;
        if (mode == null) return;
        // scoreUI 를 비워 둔 씬에서도 점수 글자는 갱신되도록 (기존: scoreUI 가 null 이면 영영 갱신 안 됨)
        if (scoreUI != null && !scoreUI.activeInHierarchy) return;
        // 파랑 칸 = 우리 팀, 빨강 칸 = 상대 팀 (내 팀을 아직 모르면 1팀을 우리 팀처럼 표시)
        NetTeam myTeam = TeamView.LocalTeam;
        if (myTeam == NetTeam.None) myTeam = NetTeam.Red;
        int red = mode.ScoreOf(TeamView.OtherTeam(myTeam)), blue = mode.ScoreOf(myTeam);
        if (red == lastRed && blue == lastBlue) return;
        lastRed = red; lastBlue = blue;
        if (redTeamScoreText != null) redTeamScoreText.text = string.Format(redScoreFormat, red);
        if (blueTeamScoreText != null) blueTeamScoreText.text = string.Format(blueScoreFormat, blue);
    }

    private void UpdateSkillFill()
    {
        if (!gameplayShown || bound == null || bound.Skill == null || skillButtonImage == null) return;
        if (skillButton != null && !skillButton.activeSelf) return;
        skillButtonImage.fillAmount = bound.Skill.ReadyFill;
    }

    private void UpdateDeathUi()
    {
        if (deathUI == null) return;
        bool dead = bound != null && bound.Health != null && bound.Health.IsDead
                    && flow != null && flow.Phase == MatchPhase.Playing;

        if (deathUI.activeSelf != dead) deathUI.SetActive(dead);
        if (!dead || deathText == null) return;

        var mode = GameModeBase.Current;
        if (mode != null && !mode.AllowRespawn) { deathText.text = noRespawnText; return; }

        var spawner = PlayerSpawner.Instance;
        float remain = spawner != null ? spawner.RespawnRemaining : 0f;
        deathText.text = string.Format(respawnCountdownFormat, Mathf.CeilToInt(remain));
    }

    private void UpdateBombUi()
    {
        var bomb = BombMode.Instance;
        bool inBomb = bomb != null && flow != null && flow.Phase == MatchPhase.Playing && gameplayShown;

        if (bombInteractButton != null)
        {
            bool show = inBomb && bomb.InteractAvailable;
            if (bombInteractButton.activeSelf != show) bombInteractButton.SetActive(show);
            if (show)
            {
                var label = bombInteractButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                    label.text = bomb.IsInteracting ? bomb.InteractLabel : (bomb.InteractIsPlant ? plantButtonLabel : defuseButtonLabel);
            }
        }

        if (bombProgressRoot != null)
        {
            bool show = inBomb && bomb.IsInteracting;
            if (bombProgressRoot.activeSelf != show) bombProgressRoot.SetActive(show);
            if (show)
            {
                if (bombProgressLabel != null) bombProgressLabel.text = bomb.InteractLabel;
                if (bombProgressFill != null) bombProgressFill.fillAmount = bomb.InteractProgress;
            }
        }
    }

    private void UpdateReconnectBanner()
    {
        if (reconnectBanner == null) return;
        var conn = subscribedConnection;
        bool show = conn != null && (conn.State == NetConnectionState.Disconnected
                                     || conn.State == NetConnectionState.Reconnecting
                                     || conn.State == NetConnectionState.Recovering);

        // 배너를 알림(alertRoot)과 같은 오브젝트로 공유하는 씬이 있다.
        // 그때 매 프레임 SetActive(false) 하면 일반 알림이 켜지자마자 꺼지므로, "켜짐↔꺼짐이 바뀔 때만" 손댄다.
        bool shared = reconnectBanner == alertRoot;
        if (show != reconnectBannerShowing)
        {
            reconnectBannerShowing = show;
            if (show && shared) HideAlert();                 // 진행 중인 알림 타이머가 배너를 끄지 않도록
            reconnectBanner.SetActive(show);
        }
        else if (!shared && reconnectBanner.activeSelf != show)
        {
            reconnectBanner.SetActive(show);
        }

        if (show && reconnectBannerText != null)
            reconnectBannerText.text = string.Format(reconnectingFormat, Mathf.CeilToInt(conn.RemainingReconnectSeconds));
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 단계 / 모드 UI

    private void HandlePhaseChanged(MatchPhase phase, bool restored)
    {
        if (phase == MatchPhase.Playing)
        {
            InitModeUi();
            // 모두의 줌인이 끝나 본 게임이 시작됐다 → 내 줌인이 이미 끝났다면 지금 조작 UI 를 만든다.
            var cam = CameraZoomController.Instance;
            if (cam != null && cam.ZoomInDone) HandleZoomInCompleted();
        }
        else if (phase == MatchPhase.Result) ShowResultUi();
    }

    private bool IsTutorial()
    {
        if (SceneManager.GetActiveScene().name.Contains(tutorialSceneKeyword)) return true;
        return flow != null && flow.Config != null && flow.Config.modeType == GameModeType.TUTORIAL;
    }

    private void HideAllModeUI()
    {
        if (timerUI != null) timerUI.SetActive(false);
        if (roundUI != null) roundUI.SetActive(false);
        if (goalUI != null) goalUI.SetActive(false);
        if (scoreUI != null) scoreUI.SetActive(false);
    }

    /// <summary>기존 InitTDMUI / InitFlagUI / InitBombUI : 모드에 맞는 타이머·목표·라운드 표시 켜기</summary>
    private void InitModeUi()
    {
        var mode = GameModeBase.Current;
        if (mode == null) return;                     // 모드가 준비되면 Update 가 다시 호출
        modeUiInitialized = true;

        if (subscribedMode != mode)
        {
            if (subscribedMode != null) subscribedMode.OnAlert -= ShowAlert;
            subscribedMode = mode;
            mode.OnAlert += ShowAlert;
        }

        var reporter = ResultReporter.Instance;
        if (reporter != null && subscribedReporter != reporter)
        {
            if (subscribedReporter != null) subscribedReporter.OnRewardResolved -= HandleRewardResolved;
            subscribedReporter = reporter;
            reporter.OnRewardResolved += HandleRewardResolved;
        }

        HideAllModeUI();
        if (IsTutorial()) return;                      // 튜토리얼: 점수/타이머/라운드/목표 숨김

        if (timerUI != null) timerUI.SetActive(mode.ShowTimer);
        if (goalUI != null) goalUI.SetActive(mode.ShowGoalUI);
        if (roundUI != null) roundUI.SetActive(mode.ShowRoundUI);
        if (goalText != null) goalText.text = mode.GoalText;
        if (roundText != null) roundText.text = mode.RoundText;
        if (timerText != null) timerText.text = FormatTime(mode.DisplaySeconds);
        lastRed = lastBlue = -1;

        mode.OnModeChanged -= HandleModeChanged;
        mode.OnModeChanged += HandleModeChanged;
        if (gameplayShown) ApplyScoreVisibility();
    }

    private void HandleModeChanged()
    {
        var mode = GameModeBase.Current;
        if (mode == null) return;
        if (roundText != null && roundUI != null && roundUI.activeSelf) roundText.text = mode.RoundText;
        if (goalText != null && goalUI != null && goalUI.activeSelf) goalText.text = mode.GoalText;
        lastRed = lastBlue = -1;
    }

    private void ApplyScoreVisibility()
    {
        var mode = GameModeBase.Current;
        bool show = !IsTutorial() && (mode == null || mode.ShowScoreUI);
        if (scoreUI != null) scoreUI.SetActive(show);
        if (redTeamScoreText != null) redTeamScoreText.gameObject.SetActive(show);
        if (blueTeamScoreText != null) blueTeamScoreText.gameObject.SetActive(show);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 조작 UI 표시 (줌인 후)

    private void HandleZoomInCompleted()
    {
        if (gameplayShown) return;
        if (flow != null && flow.Phase == MatchPhase.Result) return;
        // 내 줌인이 먼저 끝나도, 다른 유저를 기다리는 동안(본 게임 시작 전)에는 UI 를 켜지 않는다.
        if (flow != null && flow.Phase != MatchPhase.Playing) return;
        ShowGameplayUI();

        var me = PlayerEntity.Local;
        if (me != null) me.WhenReady(entity => { Bind(entity); CreateStatusUis(); });
        else CreateStatusUis();
    }

    private void CreateStatusUis()
    {
        if (MatchStatusUIManager.Instance != null) MatchStatusUIManager.Instance.CreateAllPlayerStatusItems();
        if (MyStatusUIManager.Instance != null) MyStatusUIManager.Instance.CreateMyStatusUI();
    }

    public void ShowGameplayUI()
    {
        gameplayShown = true;

        SetActive(joystick, true);
        SetActive(jumpButton, true);
        SetActive(attackButton, true);
        SetActive(reloadButton, true);
        SetActive(settingsButton, true);
        SetActive(myStatusParent, true);

        // 스킬 버튼: 이 모드에서 스킬이 꺼져 있으면(FALL) 숨김
        bool skillsOn = flow == null || flow.Config == null || flow.Config.skillsEnabled;
        SetActive(skillButton, skillsOn);

        if (reloadButtonImage != null) reloadButtonImage.fillAmount = 1f;
        ApplyScoreVisibility();
    }

    public void HideGameplayUI()
    {
        SetActive(joystick, false);
        SetActive(jumpButton, false);
        SetActive(attackButton, false);
        SetActive(reloadButton, false);
        SetActive(skillButton, false);
        if (bombInteractButton != null) bombInteractButton.SetActive(false);
        if (bombProgressRoot != null) bombProgressRoot.SetActive(false);
        // 공격 버튼이 숨겨지면 HoldButton.OnDisable 이 눌림 상태를 자동으로 풀어 준다.
    }

    private static void SetActive(GameObject go, bool active)
    {
        if (go != null && go.activeSelf != active) go.SetActive(active);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 내 캐릭터 연결 (탄약 / 재장전 / 스킬 아이콘)

    private void Bind(PlayerEntity entity)
    {
        if (entity == bound) return;
        Unbind();
        bound = entity;
        if (bound == null) return;

        if (bound.Combat != null)
        {
            bound.Combat.AmmoChanged += HandleAmmoChanged;
            bound.Combat.ReloadProgress += HandleReloadProgress;
            bound.Combat.ReloadStateChanged += HandleReloadStateChanged;
            HandleAmmoChanged(bound.Combat.Ammo, bound.Combat.MagazineSize);
        }
        if (skillButtonImage != null && bound.Skill != null && bound.Skill.skillIcon != null)
            skillButtonImage.sprite = bound.Skill.skillIcon;
        if (reloadButtonImage != null) reloadButtonImage.fillAmount = 1f;
    }

    private void Unbind()
    {
        if (bound == null) return;
        if (bound.Combat != null)
        {
            bound.Combat.AmmoChanged -= HandleAmmoChanged;
            bound.Combat.ReloadProgress -= HandleReloadProgress;
            bound.Combat.ReloadStateChanged -= HandleReloadStateChanged;
        }
        bound = null;
    }

    private void HandleAmmoChanged(int current, int max)
    {
        if (reloadButtonText != null) reloadButtonText.text = current + " / " + max;
    }

    private void HandleReloadProgress(float percent)
    {
        if (reloadButtonImage != null) reloadButtonImage.fillAmount = percent;
    }

    private void HandleReloadStateChanged(bool reloading)
    {
        if (reloadButton != null)
        {
            var btn = reloadButton.GetComponent<Button>();
            if (btn != null) btn.interactable = !reloading;
        }
        if (!reloading && reloadButtonImage != null) reloadButtonImage.fillAmount = 1f;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 알림

    public void ShowAlert(string message)
    {
        ShowAlert(message, alertSeconds);
    }

    public void ShowAlert(string message, float seconds)
    {
        if (alertRoot == null || alertText == null || string.IsNullOrEmpty(message)) return;
        if (reconnectBannerShowing && reconnectBanner == alertRoot) return;   // 재접속 배너가 같은 칸을 쓰는 중
        if (alertRoutine != null) StopCoroutine(alertRoutine);
        alertText.text = message;
        alertRoot.SetActive(true);
        alertRoutine = StartCoroutine(HideAlertAfter(seconds));
    }

    public void HideAlert()
    {
        if (alertRoutine != null) { StopCoroutine(alertRoutine); alertRoutine = null; }
        if (alertRoot != null) alertRoot.SetActive(false);
    }

    private IEnumerator HideAlertAfter(float seconds)
    {
        if (seconds > 0f) yield return new WaitForSeconds(seconds);
        alertRoutine = null;
        if (alertRoot != null) alertRoot.SetActive(false);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 킬로그

    private void TrySubscribeKillLog()
    {
        if (subscribedState != null) return;
        var state = MatchState.Instance;
        if (state == null) return;
        subscribedState = state;
        state.OnKillLog += HandleKillLog;
    }

    // MatchState 가 사망을 집계한 직후 모든 기기에서 호출된다. (killer 0 = 자멸)
    private void HandleKillLog(uint victim, uint killer, DeathCause cause)
    {
        if (killFeedParent == null) return;
        var net = InGameNet.Instance;
        if (net == null) return;

        var victimInfo = net.GetPlayer(victim);
        if (victimInfo == null) return;
        var killerInfo = killer != 0 ? net.GetPlayer(killer) : null;

        var victimDef = FindDef(victimInfo.CharacterId);
        Sprite victimSprite = victimDef != null ? victimDef.icon : null;

        KillFeedItem item;
        if (killerInfo != null)
        {
            // 다른 유저에게 죽음 → 가해자가 나 / 같은 팀 / 적 팀 인지에 따라 다른 킬 프리팹
            var prefab = KillPrefabFor(killerInfo);
            if (prefab == null) return;
            item = Instantiate(prefab, killFeedParent);
            var killerDef = FindDef(killerInfo.CharacterId);
            item.SetKill(killerDef != null ? killerDef.icon : null, killerInfo.Nickname, TeamView.ColorOf(killerInfo),
                         MiddleIcon(killerDef, cause),
                         victimSprite, victimInfo.Nickname, TeamView.ColorOf(victimInfo));
        }
        else
        {
            // 낙사/함정/폭발 등 자멸 → 자멸 프리팹 (죽은 유저의 캐릭터 아이콘 + 닉네임 + 사인 아이콘)
            if (selfDeathItemPrefab == null) return;
            item = Instantiate(selfDeathItemPrefab, killFeedParent);
            item.SetSelfDeath(victimSprite, victimInfo.Nickname, TeamView.ColorOf(victimInfo), DeathIcon(cause));
        }

        // 가장 최근 줄을 맨 위로
        item.transform.SetAsFirstSibling();
        killFeed.Insert(0, item);

        // 최대 개수를 넘으면 가장 오래된 줄(맨 아래)부터 삭제
        int max = Mathf.Max(1, maxKillFeedItems);
        while (killFeed.Count > max)
        {
            var oldest = killFeed[killFeed.Count - 1];
            killFeed.RemoveAt(killFeed.Count - 1);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        if (killFeedSeconds > 0f) StartCoroutine(Co_RemoveKillFeedAfter(item, killFeedSeconds));
    }

    private IEnumerator Co_RemoveKillFeedAfter(KillFeedItem item, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (!killFeed.Remove(item)) yield break;    // 이미 넘쳐서 삭제된 줄
        if (item != null) Destroy(item.gameObject);
    }

    // 가해자와 나의 관계로 킬 프리팹 고르기: 나 = 내 킬 / 같은 팀 = 아군 킬 / 그 밖 = 적 팀 킬
    private KillFeedItem KillPrefabFor(NetPlayer killer)
    {
        switch (TeamView.RelationOf(killer))
        {
            case TeamRelation.Self: return myKillItemPrefab;
            case TeamRelation.Ally: return allyKillItemPrefab;
            default: return enemyKillItemPrefab;
        }
    }

    private CharacterDef FindDef(string characterId)
    {
        var catalog = catalogOverride != null ? catalogOverride
            : (PlayerSpawner.Instance != null ? PlayerSpawner.Instance.catalog : null);
        return catalog != null ? catalog.Get(characterId) : null;
    }

    // 가운데 아이콘: 스킬 킬 = 스킬 아이콘 (비어 있으면 무기 아이콘) / 그 밖 (총알, 밀어서 떨어뜨린 킬 등) = 무기 아이콘
    private static Sprite MiddleIcon(CharacterDef def, DeathCause cause)
    {
        if (def == null) return null;
        if (cause == DeathCause.Skill && def.skillIcon != null) return def.skillIcon;
        return def.weaponIcon;
    }

    // 자멸 사인 아이콘: 원인별 아이콘 (비어 있으면 defaultDeathIcon)
    private Sprite DeathIcon(DeathCause cause)
    {
        Sprite icon = null;
        switch (cause)
        {
            case DeathCause.Fall: icon = fallDeathIcon; break;
            case DeathCause.Hazard: icon = hazardDeathIcon; break;
            case DeathCause.Explosion: icon = explosionDeathIcon; break;
        }
        return icon != null ? icon : defaultDeathIcon;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 결과 패널

    private void ShowResultUi()
    {
        if (resultShown) return;
        resultShown = true;

        HideGameplayUI();
        HideAlert();
        if (deathUI != null) deathUI.SetActive(false);
        if (reconnectBanner != null) reconnectBanner.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);

        var net = InGameNet.Instance;
        var me = net != null ? net.LocalPlayer : null;
        NetTeam myTeam = me != null ? me.Team : NetTeam.None;
        NetTeam winner = flow != null ? flow.Winner : NetTeam.None;

        bool isDraw = winner == NetTeam.None;
        bool isWin = !isDraw && myTeam == winner;
        if (myTeamWinImage != null) myTeamWinImage.SetActive(isWin);
        if (drawImage != null)
        {
            drawImage.SetActive(isDraw);
            if (myTeamNotWinImage != null) myTeamNotWinImage.SetActive(!isWin && !isDraw);
        }
        else if (myTeamNotWinImage != null) myTeamNotWinImage.SetActive(!isWin);

        // 파랑 리스트 = 우리 팀, 빨강 리스트 = 상대 팀 (점수판과 같은 규칙)
        NetTeam ourTeam = myTeam != NetTeam.None ? myTeam : NetTeam.Red;
        ClearChildren(redResultContent);
        ClearChildren(blueResultContent);
        PopulateTeam(ourTeam, blueResultContent);
        PopulateTeam(TeamView.OtherTeam(ourTeam), redResultContent);

        // 보상: 확정 전이면 0G, 확정되면 갱신
        var reporter = ResultReporter.Instance;
        if (reporter != null && subscribedReporter != reporter)
        {
            subscribedReporter = reporter;
            reporter.OnRewardResolved += HandleRewardResolved;
        }
        HandleRewardResolved(reporter != null && reporter.RewardResolved ? reporter.LocalReward : 0);
    }

    private void HandleRewardResolved(int gold)
    {
        if (myRewardText != null) myRewardText.text = gold > 0 ? "+" + gold + "G" : "0G";
    }

    private void PopulateTeam(NetTeam team, Transform parent)
    {
        var state = MatchState.Instance;
        if (parent == null || resultItemPrefab == null || state == null) return;

        var catalog = catalogOverride != null ? catalogOverride
            : (PlayerSpawner.Instance != null ? PlayerSpawner.Instance.catalog : null);

        foreach (var p in state.Ranked(team))
        {
            var go = Instantiate(resultItemPrefab, parent);
            var item = go.GetComponent<ResultMemberItem>();
            if (item == null) continue;
            Sprite icon = catalog != null ? catalog.GetIcon(p.CharacterId) : null;
            item.Setup(icon, p.Nickname, state.GetKills(p.SessionId), state.GetDeaths(p.SessionId));
            item.SetNicknameColor(TeamView.ColorOf(p));      // 나 노랑 / 같은 팀 파랑 / 상대 팀 빨강
        }
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null) return;
        for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 연결 상태 알림

    // 알림 속 닉네임도 관계 색으로 (같은 팀 파랑 / 상대 팀 빨강)
    private static string ColoredNick(NetPlayer p)
    {
        return TeamView.Colorize(p.Nickname, TeamView.ColorOf(p));
    }

    private void HandleRemoteDisconnected(NetPlayer p)
    {
        if (p != null) ShowAlert(string.Format(opponentDisconnectedFormat, ColoredNick(p)));
    }

    private void HandleRemoteReconnected(NetPlayer p)
    {
        if (p != null) ShowAlert(string.Format(opponentReconnectedFormat, ColoredNick(p)));
    }

    private void HandleRemoteForfeited(NetPlayer p)
    {
        if (p != null) ShowAlert(string.Format(opponentForfeitedFormat, ColoredNick(p)));
    }

    private void HandleLocalReconnectFailed(ReconnectFailReason reason)
    {
        if (failHandled) return;
        failHandled = true;
        HideGameplayUI();
        ShowAlert(reconnectFailedMessage, returnToLobbyAfterFailSeconds);
        StartCoroutine(Co_ReturnToLobbyAfterFail());
    }

    private IEnumerator Co_ReturnToLobbyAfterFail()
    {
        yield return new WaitForSeconds(returnToLobbyAfterFailSeconds);
        var reporter = ResultReporter.Instance;
        if (reporter != null) { reporter.ReturnToLobby(); yield break; }

        string scene = flow != null && flow.Config != null ? flow.Config.lobbySceneName : "LobbyPage";
        SceneManager.LoadScene(scene);
    }

    #endregion

    private static string FormatTime(float t)
    {
        t = Mathf.Max(0f, t);
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        return m.ToString("00") + ":" + s.ToString("00");
    }
}
