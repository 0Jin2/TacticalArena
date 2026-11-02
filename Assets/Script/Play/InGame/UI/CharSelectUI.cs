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
//    Countdown  단계 : 시작 전 마지막 카운트다운 (선택 잠금, 패널은 유지) → 끝나면 패널이 닫히고 본 게임 시작
//    Loading / Intro 단계 : (선택) 로딩 화면, 인트로 화면 켜기
//
//  기존과 같은 점
//   - 내 팀 패널만 보이고, 내 팀 이름/색이 표시됨
//   - 준비를 누르면 캐릭터 변경 불가. 준비를 다시 누르면 취소 (마지막 1초 이후엔 불가)
//   - 선택 시간 안에 못 고르면 자동 선택 (※ 기존은 잠긴 캐릭터도 뽑힐 수 있었음 → 지금은 잠긴 캐릭터 제외)
//   - 튜토리얼 연습방(미완료): 카운트다운/팀 문구 숨김, 준비를 누르면 패널이 바로 닫힘
//   - 잠긴 캐릭터는 선택 불가 (튜토리얼 제외)
//
//  배치: 기존 CharacterSelectionManager 가 붙어 있던 오브젝트
// ─────────────────────────────────────────────────────────────────────────────

public class CharSelectUI : MonoBehaviour
{
    public static CharSelectUI Instance { get; private set; }

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

    [Header("단계별 화면 (선택)")]
    [Tooltip("Loading 단계에서만 켜지는 화면")]
    public GameObject loadingPanel;
    [Tooltip("Intro 단계에서만 켜지는 화면")]
    public GameObject introPanel;

    [Header("내 팀 표시")]
    public string redTeamName = "레드팀";
    public string blueTeamName = "블루팀";
    public Color redTeamColor = new Color(1f, 0.3f, 0.3f);
    public Color blueTeamColor = new Color(0.3f, 0.5f, 1f);

    [Header("기타")]
    [Tooltip("캐릭터 목록. 비워 두면 PlayerSpawner 의 목록을 사용")]
    public CharacterCatalog catalogOverride;
    [SerializeField] private string tutorialSceneKeyword = "Tutorial";
    [Tooltip("잠금 상태를 다시 확인하는 간격(초). 보유 캐릭터 데이터가 늦게 도착해도 반영되게 함")]
    [SerializeField] private float lockRefreshInterval = 1f;

    private MatchFlow flow;
    private InGameNet net;
    private bool subscribed;
    private bool teamTextSet;
    private float nextLockRefreshAt;

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
        if (introPanel != null) introPanel.SetActive(false);
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
        }

        if (characterButtons != null)
            foreach (var b in characterButtons) if (b != null) b.Setup(this);

        flow.OnPhaseChanged += HandlePhaseChanged;
        flow.OnCharacterPicked += HandleCharacterPicked;
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
                if (flow.autoPickProvider == (System.Func<string>)PickRandomAvailable) flow.autoPickProvider = null;
            }
            if (net != null) net.PlayersChanged -= SyncItems;
        }
        if (Instance == this) Instance = null;
    }

    private void HandlePhaseChanged(MatchPhase phase, bool restored)
    {
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
        if (introPanel != null) introPanel.SetActive(phase == MatchPhase.Intro);

        bool selecting = phase == MatchPhase.CharSelect;
        bool finalCountdown = phase == MatchPhase.Countdown;
        if (!selecting && !finalCountdown)
        {
            if (selectionPanel != null && selectionPanel.activeSelf) selectionPanel.SetActive(false);
            return;
        }

        var me = net.LocalPlayer;
        bool myReady = me != null && flow.IsConfirmed(me.SessionId);
        string mySelected = me != null ? me.CharacterId : null;

        if (!teamTextSet) ApplyMyTeamVisibility();
        if (items.Count != CountDisplayablePlayers()) SyncItems();

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
            if (readyButton != null) readyButton.interactable = !myReady && !string.IsNullOrEmpty(mySelected);
            UpdateCharacterButtons(mySelected, myReady, false);
            SetPanel(!myReady && selecting);
            UpdateItems();
            return;
        }

        bool timed = flow.HasTimeLimit;
        float remain = flow.RemainingSeconds;
        // 마지막 1초 / 시작 전 카운트다운에는 변경 잠금
        bool finalLock = finalCountdown || (timed && remain <= 1f);

        if (readyButton != null) readyButton.interactable = !finalLock && !string.IsNullOrEmpty(mySelected);
        UpdateCharacterButtons(mySelected, myReady, finalLock);

        SetPanel(true);
        if (countdownText != null)
            countdownText.text = (timed || finalCountdown) ? Mathf.CeilToInt(remain).ToString() : string.Empty;

        UpdateItems();
    }

    private void SetPanel(bool active)
    {
        if (selectionPanel != null && selectionPanel.activeSelf != active) selectionPanel.SetActive(active);
    }

    private void UpdateCharacterButtons(string mySelectedId, bool myReady, bool finalLock)
    {
        if (characterButtons == null) return;
        foreach (var btn in characterButtons)
        {
            if (btn == null || btn.characterDef == null) continue;
            if (btn.IsLocked) { btn.SetInteractable(false); continue; }

            bool isMyPick = !string.IsNullOrEmpty(mySelectedId) && btn.CharacterId == mySelectedId;
            btn.SetInteractable(!isMyPick && !finalLock && !myReady);
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 입력

    /// <summary>[CharacterButton] 캐릭터 선택 요청</summary>
    public void Select(string characterId)
    {
        if (flow == null || string.IsNullOrEmpty(characterId)) return;
        flow.SelectCharacter(characterId);
    }

    /// <summary>[준비 버튼] 준비/준비 취소. 캐릭터를 고르지 않았으면 무시</summary>
    public void OnClick_Ready()
    {
        if (flow == null || net == null) return;
        var me = net.LocalPlayer;
        if (me == null || string.IsNullOrEmpty(me.CharacterId))
        {
            NetLog.Warn("CharSelectUI: 캐릭터 미선택 상태에서는 준비할 수 없습니다.");
            return;
        }
        flow.SetReady(!flow.IsConfirmed(me.SessionId));
    }

    /// <summary>시간 안에 못 고른 내 캐릭터를 정한다. 잠기지 않은 캐릭터 중 무작위</summary>
    private string PickRandomAvailable()
    {
        var candidates = new List<string>();
        if (characterButtons != null)
        {
            foreach (var b in characterButtons)
            {
                if (b == null || b.characterDef == null) continue;
                b.RefreshLock(IsTutorial);
                if (!b.IsLocked) candidates.Add(b.CharacterId);
            }
        }
        if (candidates.Count > 0) return candidates[Random.Range(0, candidates.Count)];

        var catalog = GetCatalog();
        if (catalog != null)
        {
            string id = catalog.RandomId(cid => !IsTutorial && UserCharacterDataManager.Instance != null && UserCharacterDataManager.Instance.IsCharacterLocked(cid));
            if (!string.IsNullOrEmpty(id)) return id;
        }
        return flow != null && flow.Config != null ? flow.Config.defaultCharacterId : string.Empty;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 팀원 칸

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

    /// <summary>명단과 칸을 맞춘다 (추가/제거)</summary>
    private void SyncItems()
    {
        if (net == null) return;

        // 사라진/이탈한 유저 칸 제거
        var remove = new List<uint>();
        foreach (var kv in items)
        {
            var p = net.GetPlayer(kv.Key);
            if (p == null || p.IsForfeited) remove.Add(kv.Key);
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
            if (!items.ContainsKey(p.SessionId)) AddItem(p);
        }

        ApplyMyTeamVisibility();
        UpdateItems();
    }

    private void AddItem(NetPlayer player)
    {
        bool red = player.Team == NetTeam.Red;
        Transform parent = red ? redTeamPanel : blueTeamPanel;
        GameObject prefab = (player.IsLocal && myTeamItemPrefab != null) ? myTeamItemPrefab
            : (red ? redTeamItemPrefab : blueTeamItemPrefab);
        if (parent == null || prefab == null) return;

        var go = Instantiate(prefab, parent);
        var item = go.GetComponent<TeamMemberItem>();
        if (item == null) { Destroy(go); return; }

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

        item.SetReady(flow != null && flow.IsConfirmed(player.SessionId));
        item.SetDisconnected(!player.IsOnline);

        string shown;
        shownCharacter.TryGetValue(player.SessionId, out shown);
        if (!string.IsNullOrEmpty(player.CharacterId) && player.CharacterId != shown)
        {
            var catalog = GetCatalog();
            var def = catalog != null ? catalog.Get(player.CharacterId) : FindDefInButtons(player.CharacterId);
            if (def != null)
            {
                item.SetCharacterVisual(def.uiPrefab);
                shownCharacter[player.SessionId] = player.CharacterId;
            }
        }
    }

    private CharacterDef FindDefInButtons(string id)
    {
        if (characterButtons == null) return null;
        foreach (var b in characterButtons)
            if (b != null && b.characterDef != null && b.CharacterId == id) return b.characterDef;
        return null;
    }

    /// <summary>내 팀 패널만 보이게 하고 팀 이름/색을 표시</summary>
    private void ApplyMyTeamVisibility()
    {
        var me = net != null ? net.LocalPlayer : null;
        if (me == null || me.Team == NetTeam.None) return;

        bool red = me.Team == NetTeam.Red;
        if (redTeamPanel != null) redTeamPanel.gameObject.SetActive(red);
        if (blueTeamPanel != null) blueTeamPanel.gameObject.SetActive(!red);

        if (myTeamText != null && !IsTutorialPractice)
        {
            myTeamText.text = red ? redTeamName : blueTeamName;
            myTeamText.color = red ? redTeamColor : blueTeamColor;
        }
        teamTextSet = true;
    }

    #endregion
}
