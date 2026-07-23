using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GamePlayUIController : MonoBehaviour
{
    public static GamePlayUIController Instance;

    [Header("팀 점수 TEXT")]
    public TMP_Text redTeamScoreText;
    public TMP_Text blueTeamScoreText;

    [Header("버튼 UI")]
    public GameObject joystick;
    public GameObject jumpButton;
    public GameObject attackButton;
    public GameObject reloadButton;
    public GameObject skillButton;

    [Header("폭탄 상호작용 버튼")]
    [SerializeField] private GameObject bombInteractButton;

    [Header("스코어 및 프로필 UI")]
    public GameObject settingsButton;
    public GameObject myStatusParent; // 🆕 본인 상태 UI가 붙을 부모
    public GameObject myStatusItemPrefab; // 🆕 MyStatusItem 프리팹
    public GameObject scoreUI;

    [Header("모드별 UI")]
    [SerializeField] private GameObject timerUI;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private GameObject roundUI;
    [SerializeField] private TMP_Text roundText;
    [SerializeField] private GameObject goalUI;
    [SerializeField] private TMP_Text goalText;

    [Header("리로드/스킬 버튼")]
    public TMP_Text reloadButtonText;     // (인스펙터에 직접 할당)
    public Image reloadButtonImage;   // (fill radial 사용)
    public Image skillButtonImage;

    [Header("사망 UI")]
    [SerializeField] private GameObject deathUI;
    [SerializeField] private TMP_Text deathText;

    [Header("결과 패널 UI")]
    public GameObject resultPanel;                // 결과 전체 패널
    public GameObject myTeamWinImage;             // 내 팀 승 이미지
    public GameObject myTeamNotWinImage;
    public Transform redResultContent;            // 레드 팀 리스트 부모
    public Transform blueResultContent;           // 블루 팀 리스트 부모
    public GameObject resultItemPrefab;           // ResultMemberItem 프리팹
    public TMP_Text myRewardText;                 // 내 보상 텍스트(선택)

    [Header("공용 알림 UI")]
    [SerializeField] private GameObject alertRoot;    // 패널(활성/비활성)
    [SerializeField] private TMP_Text alertText;      // 메시지 텍스트

    [Header("BOMB 전용 UI")]
    [SerializeField] private GameObject bombProgressRoot;
    [SerializeField] private TMP_Text bombProgressLabel;
    [SerializeField] private Image bombProgressFill;
    [SerializeField] private RectTransform bombArrow; // 화살표 UI

    private Transform _bombTarget;
    private bool isInteractingBomb = false;
    private BombSite currentTargetSite;
    private Coroutine deathCoroutine;
    private PlayerNetworkController playerController;
    private SkillHandler skillHandler;
    private System.Action savedAttackClickAction;
    private bool isSkillAttackOverride = false;
    private Coroutine _alertCo;
    private int? _pendingGold;

    private void Awake()
    {
        Instance = this;
        HideAllModeUI();

        // 결과 패널 초기 상태
        if (resultPanel != null) resultPanel.SetActive(false);
        if (myTeamWinImage) myTeamWinImage.SetActive(false);
        if (myTeamNotWinImage) myTeamNotWinImage.SetActive(false);
        if (myRewardText) myRewardText.text = "";
    }

    // 🆕 인스펙터 누락/지연 로딩 방어용 동적 참조 복구
    private void EnsureResultUIRefs()
    {
        if (myRewardText == null || redResultContent == null || blueResultContent == null || resultPanel == null)
        {
            // 현재 오브젝트 하위에서 탐색
            if (resultPanel == null)
                resultPanel = GameObject.FindWithTag("ResultPanel") ?? resultPanel;

            if (myRewardText == null)
                myRewardText = GetComponentInChildren<TMP_Text>(true);

            // 필요하면 특정 이름으로 더 정확히 찾도록 보완 가능
            // 예: transform.Find("ResultPanel/MyRewardText") 등
        }
    }

    public void ShowGameplayUI()
    {
        joystick?.SetActive(true);
        jumpButton?.SetActive(true);
        attackButton?.SetActive(true);
        reloadButton?.SetActive(true);
        skillButton?.SetActive(true);
        settingsButton?.SetActive(true);
        scoreUI?.SetActive(true);
        myStatusParent?.SetActive(true);

        if (reloadButtonImage != null)
            reloadButtonImage.fillAmount = 1f;

        string sceneName = SceneManager.GetActiveScene().name;
        if (sceneName.Contains("FD"))
        {
            //Fall Down모드일때는 스킬 버튼 비활성화
            if (skillButton != null)
                skillButton.SetActive(false);
        }

        // 🔴 Tutorial 모드: 스코어/타이머 UI 숨김
        if (sceneName.Contains("Tutorial"))
        {
            scoreUI?.SetActive(false);
            timerUI?.SetActive(false);
            roundUI?.SetActive(false);
            goalUI?.SetActive(false);
            redTeamScoreText?.gameObject.SetActive(false);
            blueTeamScoreText?.gameObject.SetActive(false);
        }
    }

    public void HideGameplayUI()
    {
        joystick?.SetActive(false);
        jumpButton?.SetActive(false);
        attackButton?.SetActive(false);
        reloadButton?.SetActive(false);
        skillButton?.SetActive(false);
    }

    private void HideAllModeUI()
    {
        timerUI.SetActive(false);
        roundUI.SetActive(false);
        goalUI.SetActive(false);
        scoreUI.SetActive(false);
    }

    public void UpdateTeamScoreUI(int redScore, int blueScore)
    {
        if (redTeamScoreText != null)
            redTeamScoreText.text = redScore.ToString();

        if (blueTeamScoreText != null)
            blueTeamScoreText.text = blueScore.ToString();
    }

    public FixedJoystick GetJoystickComponent()
    {
        return joystick?.GetComponent<FixedJoystick>();
    }

    public void InitReloadButton(PlayerNetworkController controller)
    {
        playerController = controller;
        if (reloadButton != null)
        {
            var btn = reloadButton.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnClick_Reload);
        }
    }

    public void InitSkillButton(SkillHandler handler)
    {
        skillHandler = handler;
        if (skillButton != null)
        {
            var btn = skillButton.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OnClick_Skill);

            if (skillButtonImage != null && handler.skillIcon != null)
            {
                skillButtonImage.sprite = handler.skillIcon;
            }
        }
    }

    private void OnClick_Reload()
    {
        playerController?.TryReload();
    }

    private void OnClick_Skill()
    {
        skillHandler?.TryUseSkill();
    }

    // 탄약 텍스트 갱신 ("현재/최대")
    public void UpdateReloadText(int current, int max)
    {
        if (reloadButtonText != null)
            reloadButtonText.text = $"{current} / {max}";
    }

    // 재장전 버튼 비/활성화
    public void SetReloadButtonInteractable(bool interactable)
    {
        if (reloadButton != null)
        {
            var btn = reloadButton.GetComponent<Button>();
            btn.interactable = interactable;
        }
    }

    public void UpdateReloadButtonFill(float percent)
    {
        if (reloadButtonImage != null)
            reloadButtonImage.fillAmount = percent;
    }

    public void UpdateSkillButtonFill(float percent)
    {
        if (skillButtonImage != null)
            skillButtonImage.fillAmount = percent;
    }

    public void OverrideAttackButton(System.Action onClick)
    {
        if (attackButton != null && !isSkillAttackOverride)
        {
            var btn = attackButton.GetComponent<Button>();
            if (btn != null)
            {
                savedAttackClickAction = btn.onClick.GetPersistentEventCount() > 0
                    ? (System.Action)btn.onClick.Invoke
                    : null;

                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => onClick());
                isSkillAttackOverride = true;
            }
        }
    }

    // 공격 버튼 원상복구
    public void RestoreAttackButton()
    {
        if (attackButton != null && isSkillAttackOverride)
        {
            var btnObj = attackButton;
            var btn = btnObj.GetComponent<UnityEngine.UI.Button>();
            btn.onClick.RemoveAllListeners();

            // 기존의 EventTrigger 복구
            var trigger = btnObj.GetComponent<UnityEngine.EventSystems.EventTrigger>();
            if (trigger == null)
                trigger = btnObj.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            trigger.triggers.Clear();

            // PointerDown : isAttackButtonHeld = true
            UnityEngine.EventSystems.EventTrigger.Entry downEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            downEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerDown;
            downEntry.callback.AddListener((_) => {
                if (playerController != null && playerController.localInput != null)
                    playerController.localInput.isAttackButtonHeld = true;
            });

            // PointerUp : isAttackButtonHeld = false
            UnityEngine.EventSystems.EventTrigger.Entry upEntry = new UnityEngine.EventSystems.EventTrigger.Entry();
            upEntry.eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp;
            upEntry.callback.AddListener((_) => {
                if (playerController != null && playerController.localInput != null)
                    playerController.localInput.isAttackButtonHeld = false;
            });

            trigger.triggers.Add(downEntry);
            trigger.triggers.Add(upEntry);

            isSkillAttackOverride = false;

            // 혹시 모를 잔여 입력 클리어
            if (playerController != null && playerController.localInput != null)
                playerController.localInput.isAttackButtonHeld = false;
        }
    }

    public void ShowDeathUI()
    {
        if (deathUI == null || deathText == null) return;
        deathUI.SetActive(true);

        // 재사용 방지
        if (deathCoroutine != null)
            StopCoroutine(deathCoroutine);
        deathCoroutine = StartCoroutine(UpdateDeathCountdown());
    }

    public void HideDeathUI()
    {
        if (deathUI != null) deathUI.SetActive(false);
        if (deathCoroutine != null) StopCoroutine(deathCoroutine);
        deathCoroutine = null;
    }

    private IEnumerator UpdateDeathCountdown()
    {
        float respawnTime = 5f; // 🛠️ 원한다면 캐릭터별로 조정 가능
        float t = respawnTime;

        while (t > 0f)
        {
            deathText.text = $"부활까지 {Mathf.CeilToInt(t)}초";
            yield return new WaitForSeconds(1f);
            t -= 1f;
        }
    }

    public void InitTDMUI(float timeLimit, int scoreGoal)
    {
        HideAllModeUI();
        timerUI.SetActive(true);
        scoreUI.SetActive(true);
        goalUI.SetActive(true); // 🆕 목표 UI 활성화
        goalText.text = $"목표 처치 : {scoreGoal}킬"; // 🆕 텍스트 지정

        timerText.text = FormatTime(timeLimit);
        UpdateTeamScoreUI(0, 0);
    }

    public void InitFlagUI(float timeLimit, int captureGoal)
    {
        HideAllModeUI();
        timerUI.SetActive(true);
        goalUI.SetActive(true);

        timerText.text = FormatTime(timeLimit);
        goalText.text = $"목표 점수 : {captureGoal}";
    }

    public void InitBombUI(int totalRounds, float roundTime)
    {
        HideAllModeUI();
        timerUI.SetActive(true);
        roundUI.SetActive(true);
        timerText.text = FormatTime(roundTime);
        roundText.text = $"{1} / {totalRounds}";
    }

    public void InitBombInteractionButton(BombSite site, bool isPlant)
    {
        if (bombInteractButton == null) return;

        currentTargetSite = site;
        isInteractingBomb = false;

        // ✅ 버튼을 자식까지 탐색해 확실히 잡는다
        var btn = bombInteractButton.GetComponentInChildren<Button>(true);
        if (btn == null)
        {
            Debug.LogError("❌ bombInteractButton에 Button 컴포넌트를 찾지 못했습니다. 프리팹 구조를 확인하세요.");
            return;
        }

        // 텍스트도 자식에서 안전하게 찾기
        var labelText = bombInteractButton.GetComponentInChildren<TMP_Text>(true);
        string label = isPlant ? "폭탄 설치" : "폭탄 해제";
        if (labelText != null) labelText.text = label;

        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            var runner = FindObjectOfType<NetworkRunner>();
            var local = runner != null ? runner.LocalPlayer : PlayerRef.None;

            var myController = FindObjectsOfType<PlayerNetworkController>()
                .FirstOrDefault(c => c.Object != null && c.Object.InputAuthority == local);

            if (!local.IsValid || myController == null) return;

            if (!isInteractingBomb)
            {
                isInteractingBomb = true;

                if (isPlant)
                {
                    site.TryInteractPlant(local, myController.transform);
                    ShowBombProgress("설치 중.");
                }
                else
                {
                    site.TryInteractDefuse(local, myController.transform);
                    ShowBombProgress("해제 중.");
                }

                myController.SetMovementLock(true);
            }
            else
            {
                isInteractingBomb = false;
                HideBombProgress();
                myController.SetMovementLock(false);
                // ⛔ 서버 취소 RPC는 선택사항. 필요 시 별도 추가(아래 설명 참고).
            }
        });

        // 버튼 활성화 및 interactable 보장
        bombInteractButton.SetActive(true);
        btn.interactable = true;

        // 버튼이 보일 때 비주얼도 보장
        if (site != null && site.bombVisual != null)
            site.bombVisual.SetActive(true);
    }


    public void HideBombInteraction()
    {
        bombInteractButton?.SetActive(false);
        isInteractingBomb = false;
        currentTargetSite = null;
    }

    public void UpdateTimer(float remaining)
    {
        timerText.text = FormatTime(remaining);
    }

    public void UpdateGoalUI(int current, int goal)
    {
        if (goalText == null) return;

        var txt = goalText.text ?? string.Empty;

        if (txt.Contains("목표 처치"))
            goalText.text = $"목표 처치 : {goal}킬";
        else if (txt.Contains("목표 점수"))
            goalText.text = $"목표 점수 : {goal}";
        else
            goalText.text = goal.ToString();
    }

    public void UpdateRoundUI(int current, int total)
    {
        roundText.text = $"{current} / {total}";
    }

    private string FormatTime(float t)
    {
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        return $"{m:00}:{s:00}";
    }

    // === 결과 패널 구성 ===
    public void ShowResultUI(TeamAssigner.Team winner)
    {
        EnsureResultUIRefs();

        // ✅ 결과 패널 열기 전에 항상 보상 표기를 초기화 (이전 매치 잔상 방지)
        _pendingGold = 0;
        if (myRewardText != null) myRewardText.text = "0G";

        HideGameplayUI();
        resultPanel?.SetActive(true);

        var runner = FindObjectOfType<NetworkRunner>();
        var myTeam = TeamAssigner.Team.None;
        if (runner != null && TeamAssigner.Instance != null && runner.LocalPlayer.IsValid)
        {
            if (TeamAssigner.Instance.HasTeam(runner.LocalPlayer))
                myTeam = TeamAssigner.Instance.GetTeam(runner.LocalPlayer);
        }

        bool isWin = (winner != TeamAssigner.Team.None) && (myTeam == winner);
        myTeamWinImage?.SetActive(isWin);
        myTeamNotWinImage?.SetActive(!isWin);

        ClearResultContents();

        // 액티브 플레이어만 표기(유령 엔트리 방지)
        var active = new HashSet<PlayerRef>();
        if (runner != null)
            foreach (var p in runner.ActivePlayers) active.Add(p);

        var redPlayers = new List<PlayerRef>();
        var bluePlayers = new List<PlayerRef>();
        var teams = TeamAssigner.Instance;

        foreach (var kv in teams.Teams)
        {
            var p = kv.Key;
            var t = kv.Value;
            if (!active.Contains(p)) continue;
            if (!teams.Nicknames.ContainsKey(p)) continue;

            if (t == TeamAssigner.Team.Red) redPlayers.Add(p);
            if (t == TeamAssigner.Team.Blue) bluePlayers.Add(p);
        }

        PopulateTeamResult(Rank(redPlayers), redResultContent);
        PopulateTeamResult(Rank(bluePlayers), blueResultContent);

        // 보상 텍스트 최종 반영 (보상 RPC 도착 전이면 0G 유지, 도착 시 ShowPersonalReward가 덮어씀)
        UpdateRewardText();
    }

    private List<(PlayerRef p, int k, int d)> Rank(List<PlayerRef> list)
    {
        var arr = new List<(PlayerRef, int, int)>();
        foreach (var p in list)
        {
            int k = 0, d = 0;
            MatchStatusTracker.Instance?.TryGetKill(p, out k);
            MatchStatusTracker.Instance?.TryGetDeath(p, out d);
            arr.Add((p, k, d));
        }

        return arr
            .OrderByDescending(x => x.Item2)
            .ThenBy(x => x.Item3)
            .ThenBy(x => x.Item1.RawEncoded)
            .Select(x => (x.Item1, x.Item2, x.Item3))
            .ToList();
    }

    public void ShowPersonalReward(int gold)
    {
        EnsureResultUIRefs();

        _pendingGold = gold;

        // ✅ 결과 패널이 떠 있든 아니든 항상 텍스트 반영
        if (myRewardText != null)
        {
            myRewardText.text = $"+{gold}G";
        }

        if (gold > 0)
        {
            UserInfoManager.Instance?.AddGold(gold);
            Debug.Log($"💰 골드 지급 완료: {gold}G → UserInfoManager 반영");
        }
    }

    private void UpdateRewardText()
    {
        Debug.Log($"[UpdateRewardText] _pendingGold = {_pendingGold}");
        if (myRewardText != null)
        {
            // ✅ 텍스트가 이미 "0G"라면 갱신
            if (_pendingGold.HasValue && _pendingGold.Value > 0)
            {
                myRewardText.text = $"+{_pendingGold.Value}G";
            }
            else
            {
                // 비어있으면 "0G"로, 이미 "0G"면 유지
                if (string.IsNullOrEmpty(myRewardText.text) || myRewardText.text == "0G")
                    myRewardText.text = "0G";
            }
        }
    }

    public void ShowResultPanel()
    {
        EnsureResultUIRefs();
        resultPanel.SetActive(true);

        // 기존 승/패, 리스트 설정 로직 유지
        UpdateRewardText();
    }

    private void ClearResultContents()
    {
        void Clear(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
                Destroy(root.GetChild(i).gameObject);
        }
        Clear(redResultContent);
        Clear(blueResultContent);
    }

    private void PopulateTeamResult(List<(PlayerRef p, int k, int d)> rankList, Transform parent)
    {
        if (parent == null || resultItemPrefab == null || TeamAssigner.Instance == null)
            return;

        foreach (var (p, k, d) in rankList)
        {
            if (!TeamAssigner.Instance.Nicknames.TryGet(p, out var nickname))
                continue;

            Sprite icon = null;
            if (CharacterSelectionNetworkData.Instance != null &&
                CharacterSelectionNetworkData.Instance.SelectedCharacters.TryGet(p, out var charId))
            {
                icon = PlaySceneNetworkSpawner.Instance != null
                    ? PlaySceneNetworkSpawner.Instance.GetCharacterIconById(charId)
                    : null;
            }

            var go = Instantiate(resultItemPrefab, parent);
            var item = go.GetComponent<ResultMemberItem>();
            if (item != null)
                item.Setup(icon, nickname, k, d);
        }
    }

    public void ShowBombProgress(string label)
    {
        if (bombProgressRoot) bombProgressRoot.SetActive(true);
        if (bombProgressLabel) bombProgressLabel.text = label;
        if (bombProgressFill) bombProgressFill.fillAmount = 0f;
    }

    public void UpdateBombProgress(float normalized)
    {
        if (bombProgressFill) bombProgressFill.fillAmount = Mathf.Clamp01(normalized);
    }

    public void HideBombProgress()
    {
        if (bombProgressRoot) bombProgressRoot.SetActive(false);
    }

    public void ShowBombAlert(Transform target, string message, float seconds = 2f)
    {
        ShowAlert(message, seconds);
    }

    public void ShowFlagAlert(string message, float seconds = 2f)
    {
        ShowAlert(message, seconds);
    }

    public void ShowFlagAlertForTeam(TeamAssigner.Team target, string message, float seconds = 2f)
    {
        ShowAlertForTeam(target, message, seconds);
    }

    public void HideBombAlert()
    {
        // 모드 한정 알림 오브젝트(bombAlertRoot) 제거: 공용 alertRoot만 사용
        if (alertRoot) alertRoot.SetActive(false);

        // 방향 화살표는 그대로 사용(있으면 끄기)
        if (bombArrow) bombArrow.gameObject.SetActive(false);

        // 폭탄 대상 포인터 초기화
        _bombTarget = null;
    }

    public void ShowAlert(string message, float seconds = 2f)
    {
        if (alertRoot == null || alertText == null) return;
        StopAllCoroutines();
        alertText.text = message;
        alertRoot.SetActive(true);
        StartCoroutine(HideAlertAfter(seconds));
    }

    public void ShowAlertForTeam(TeamAssigner.Team target, string message, float seconds = 2f)
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner == null || !runner.LocalPlayer.IsValid || TeamAssigner.Instance == null) return;
        var myTeam = TeamAssigner.Instance.GetTeam(runner.LocalPlayer);
        if (myTeam != target) return;
        ShowAlert(message, seconds);
    }

    public void ShowDirectionalAlert(Transform worldTarget, string message, float seconds = 2f)
    {
        // alertPointer/_pointerTarget 제거 정책에 따라 방향 포인터는 더 이상 쓰지 않음.
        // 목표 Transform은 무시하고 공용 알림만 출력.
        ShowAlert(message, seconds);
    }

    private System.Collections.IEnumerator HideAlertAfter(float sec)
    {
        if (sec <= 0f)
            yield break;

        yield return new WaitForSeconds(sec);

        if (alertRoot != null)
            alertRoot.SetActive(false);
    }
}