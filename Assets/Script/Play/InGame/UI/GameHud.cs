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

    [Header("팀 점수 TEXT")]
    public TMP_Text redTeamScoreText;
    public TMP_Text blueTeamScoreText;

    [Header("버튼 UI")]
    public GameObject joystick;
    public GameObject jumpButton;
    public GameObject attackButton;
    public GameObject reloadButton;
    public GameObject skillButton;

    [Header("폭탄 상호작용 버튼 (BOMB)")]
    [SerializeField] private GameObject bombInteractButton;
    [SerializeField] private GameObject bombProgressRoot;
    [SerializeField] private TMP_Text bombProgressLabel;
    [SerializeField] private Image bombProgressFill;
    public string plantButtonLabel = "폭탄 설치";
    public string defuseButtonLabel = "폭탄 해제";

    [Header("스코어 및 프로필 UI")]
    public GameObject settingsButton;
    public GameObject myStatusParent;               // 본인 상태 UI가 붙을 부모
    public GameObject myStatusItemPrefab;           // MyStatusItem 프리팹
    public GameObject scoreUI;

    [Header("모드별 UI")]
    [SerializeField] private GameObject timerUI;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private GameObject roundUI;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private GameObject goalUI;
    [SerializeField] private TMP_Text goalText;

    [Header("리로드/스킬 버튼")]
    public TMP_Text reloadButtonText;               // "현재 / 최대"
    public Image reloadButtonImage;                 // fill radial
    public Image skillButtonImage;                  // 스킬 아이콘 + 쿨타임 fill

    [Header("사망 UI")]
    [SerializeField] private GameObject deathUI;
    [SerializeField] private TMP_Text deathText;
    public string respawnCountdownFormat = "부활까지 {0}초";
    [Tooltip("부활이 없는 모드(BOMB)에서 죽었을 때 표시할 문구")]
    public string noRespawnText = "다음 라운드를 기다리는 중";

    [Header("결과 패널 UI")]
    public GameObject resultPanel;                  // 결과 전체 패널
    public GameObject myTeamWinImage;               // 내 팀 승 이미지
    public GameObject myTeamNotWinImage;            // 내 팀 패 이미지
    [Tooltip("(선택) 무승부 이미지. 비워 두면 무승부도 '패' 이미지를 사용 (기존 동작)")]
    public GameObject drawImage;
    public Transform redResultContent;              // 레드 팀 리스트 부모
    public Transform blueResultContent;             // 블루 팀 리스트 부모
    public GameObject resultItemPrefab;             // ResultMemberItem 프리팹
    public TMP_Text myRewardText;                   // 내 보상 텍스트
    [Tooltip("캐릭터 아이콘을 찾을 목록. 비워 두면 PlayerSpawner 의 목록을 사용")]
    public CharacterCatalog catalogOverride;

    [Header("공용 알림 UI")]
    [SerializeField] private GameObject alertRoot;
    [SerializeField] private TMP_Text alertText;
    public float alertSeconds = 2f;

    [Header("연결 상태 알림 (선택)")]
    [Tooltip("내 연결이 끊겨 재접속 중일 때 켜지는 배너")]
    [SerializeField] private GameObject reconnectBanner;
    [SerializeField] private TMP_Text reconnectBannerText;
    public string reconnectingFormat = "연결이 끊겼습니다. 재접속 중... ({0}초)";
    public string opponentDisconnectedFormat = "{0} 님의 연결이 끊겼습니다.";
    public string opponentReconnectedFormat = "{0} 님이 다시 접속했습니다.";
    public string opponentForfeitedFormat = "{0} 님이 게임에서 이탈했습니다.";
    public string reconnectFailedMessage = "연결할 수 없어 로비로 돌아갑니다.";
    public float returnToLobbyAfterFailSeconds = 3f;

    [Header("튜토리얼")]
    [SerializeField] private string tutorialSceneKeyword = "Tutorial";

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
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (flow != null) flow.OnPhaseChanged -= HandlePhaseChanged;

        var cam = CameraZoomController.Instance;
        if (cam != null) cam.OnZoomInCompleted -= HandleZoomInCompleted;

        if (subscribedMode != null) subscribedMode.OnAlert -= ShowAlert;
        if (subscribedReporter != null) subscribedReporter.OnRewardResolved -= HandleRewardResolved;

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
        if (mode == null || scoreUI == null || !scoreUI.activeInHierarchy) return;
        int red = mode.ScoreOf(NetTeam.Red), blue = mode.ScoreOf(NetTeam.Blue);
        if (red == lastRed && blue == lastBlue) return;
        lastRed = red; lastBlue = blue;
        if (redTeamScoreText != null) redTeamScoreText.text = red.ToString();
        if (blueTeamScoreText != null) blueTeamScoreText.text = blue.ToString();
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
        if (reconnectBanner.activeSelf != show) reconnectBanner.SetActive(show);
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

        ClearChildren(redResultContent);
        ClearChildren(blueResultContent);
        PopulateTeam(NetTeam.Red, redResultContent);
        PopulateTeam(NetTeam.Blue, blueResultContent);

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

    private void HandleRemoteDisconnected(NetPlayer p)
    {
        if (p != null) ShowAlert(string.Format(opponentDisconnectedFormat, p.Nickname));
    }

    private void HandleRemoteReconnected(NetPlayer p)
    {
        if (p != null) ShowAlert(string.Format(opponentReconnectedFormat, p.Nickname));
    }

    private void HandleRemoteForfeited(NetPlayer p)
    {
        if (p != null) ShowAlert(string.Format(opponentForfeitedFormat, p.Nickname));
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
