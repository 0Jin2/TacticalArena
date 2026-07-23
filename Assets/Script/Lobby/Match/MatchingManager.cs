using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Fusion;
using Fusion.Sockets;
using UnityEngine.SceneManagement;

public class MatchingManager : MonoBehaviour
{
    [Header("UI 오브젝트")]
    public Button playButton;
    public Button leftModeButton;
    public Button rightModeButton;
    public TMP_Text modeText;
    public TMP_Text matchStatusText;
    public GameObject matchStatusPanel; // 매칭 현황 프리팹(오브젝트)
    public TMP_Text matchCountText;

    [Header("세팅")]
    public List<string> modeList = new() { "팀데스매치", "깃발 탈취", "FD", "연습방" };
    public List<int> maxPlayersList = new() { 8, 6, 6, 1 };
    public List<List<string>> sceneLists = new()
    {
        new List<string>() { "TDM_Mine" },              // TDM
        new List<string>() { "FLAG_PirateTown" },       // FLAG
        new List<string>() { "FD_SpaceArena", "FD_PirateArena" }, // FD
        new List<string>() { "Tutorial" }               // 연습방
    };

    [Header("모드 타입 / 설명 관련")]
    public TMP_Text modeTypeText; // 모드 타입 표시 텍스트
    public Button infoButton; // 설명 버튼
    public List<GameObject> modeInfoObjects; // 모드별 설명 오브젝트

    [Header("튜토리얼")]
    public GameObject tutorialPopup;

    private int modeIndex = 0;
    private bool isMatching = false;
    private NetworkRunner runner;
    private int currentPlayerCount = 1;
    private int maxPlayers = 6;
    private bool matchComplete = false;
    private Coroutine matchingCoroutine; // 코루틴 참조용

    // 세션 프로퍼티 키 (통일된 상수)
    private const string PROP_MODE = "MODE";
    private const string PROP_MAP = "MAP";

    void Start()
    {
        leftModeButton.onClick.AddListener(OnLeftModeClicked);
        rightModeButton.onClick.AddListener(OnRightModeClicked);
        playButton.onClick.AddListener(OnPlayButtonClicked);

        modeIndex = 0;

        if (UserInfoManager.Instance != null)
        {
            //중복 구독 방지
            UserInfoManager.Instance.OnTutorialDataLoaded -= ApplyTutorialRestriction;
            UserInfoManager.Instance.OnTutorialDataLoaded += ApplyTutorialRestriction;

            //로비 씬에서 Start 시점에도 "반드시" 최신 뒤끝 데이터를 다시 로드
            UserInfoManager.Instance.LoadUserData();
        }

        //데이터 로드 결과를 즉시 반영 (이벤트를 놓치더라도 여기서 보정됨)
        ApplyTutorialRestriction();

        UpdateModeUI();
        matchStatusPanel.SetActive(false);
        matchComplete = false;
    }

    private void ApplyTutorialRestriction()
    {
        bool mustTutorial = (UserInfoManager.Instance != null && !UserInfoManager.Instance.IsTutorialCompleted);

        if (mustTutorial)
        {
            int tutorialIndex = modeList.IndexOf("연습방");
            if (tutorialIndex >= 0) modeIndex = tutorialIndex;

            // 튜토리얼 미완료 시 "모드 변경 버튼" 비활성화
            if (leftModeButton != null) leftModeButton.interactable = false;
            if (rightModeButton != null) rightModeButton.interactable = false;

            // (있다면) 버튼 오브젝트 자체도 클릭 막기 + 비활성화 느낌 주기
            if (leftModeButton != null) leftModeButton.gameObject.SetActive(false);
            if (rightModeButton != null) rightModeButton.gameObject.SetActive(false);

            if (tutorialPopup != null)
                tutorialPopup.SetActive(true);
        }
        else
        {
            //튜토리얼 완료 시 다시 활성화
            if (leftModeButton != null)
            {
                leftModeButton.gameObject.SetActive(true);
                leftModeButton.interactable = true;
            }

            if (rightModeButton != null)
            {
                rightModeButton.gameObject.SetActive(true);
                rightModeButton.interactable = true;
            }

            if (tutorialPopup != null)
                tutorialPopup.SetActive(false);
        }

        UpdateModeUI();
    }

    void UpdateModeUI()
    {
        modeText.text = modeList[modeIndex];
        maxPlayers = maxPlayersList[modeIndex];

        if (!isMatching)
            matchCountText.text = $"1/{maxPlayers}";

        // === 모드 타입 표시 ===
        string currentMode = modeList[modeIndex];
        switch (currentMode)
        {
            case "팀데스매치":
            case "깃발 탈취":
                modeTypeText.text = "클래식";
                break;
            case "폴 다운":
                modeTypeText.text = "아케이드";
                break;
            default:
                modeTypeText.text = "";
                break;
        }

        // === 🔹 모드 설명 오브젝트 제어 ===
        if (modeInfoObjects != null && modeInfoObjects.Count == modeList.Count)
        {
            for (int i = 0; i < modeInfoObjects.Count; i++)
            {
                if (modeInfoObjects[i] != null)
                    modeInfoObjects[i].SetActive(false);
            }

            // 🔹 버튼 클릭 시 해당 모드 설명 오브젝트만 활성화
            if (infoButton != null)
            {
                infoButton.onClick.RemoveAllListeners();
                int index = modeIndex; // 클로저 캡처 방지
                infoButton.onClick.AddListener(() =>
                {
                    for (int i = 0; i < modeInfoObjects.Count; i++)
                    {
                        if (modeInfoObjects[i] != null)
                            modeInfoObjects[i].SetActive(i == index);
                    }
                });
            }
        }
    }

    void OnLeftModeClicked()
    {
        if (isMatching) return;
        modeIndex = (modeIndex - 1 + modeList.Count) % modeList.Count;
        UpdateModeUI();
    }

    void OnRightModeClicked()
    {
        if (isMatching) return;
        modeIndex = (modeIndex + 1) % modeList.Count;
        UpdateModeUI();
    }

    void OnPlayButtonClicked()
    {
        if (isMatching) CancelMatching();
        else matchingCoroutine = StartCoroutine(StartMatchingRoutine());
    }

    IEnumerator StartMatchingRoutine()
    {
        isMatching = true;
        matchComplete = false;
        playButton.GetComponentInChildren<TMP_Text>().text = "매칭취소";
        leftModeButton.interactable = false;
        rightModeButton.interactable = false;
        playButton.interactable = true;

        matchStatusPanel.SetActive(true);
        if (modeList[modeIndex] == "연습방")
            matchStatusText.text = "연습방 입장중";
        else
            matchStatusText.text = $"{modeList[modeIndex]} 모드 매칭 중...";

        matchCountText.text = $"1/{maxPlayers}";

        // 기존 Runner 정리
        if (runner != null)
        {
            runner.Shutdown();
            Destroy(runner.gameObject);
            runner = null;
        }

        // 러너 생성
        GameObject runnerObject = new GameObject("NetworkRunner");
        runner = runnerObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        DontDestroyOnLoad(runnerObject);
        var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();

        var callback = runnerObject.AddComponent<MatchingRunnerCallback>(); // 콜백 컴포넌트
        runner.AddCallbacks(callback);

        // === 1) 로비 입장 후 같은 모드의 빈 슬롯이 있는 방을 먼저 찾는다
        var joinsTask = FindJoinableSessionNameAsync(runner, modeList[modeIndex], maxPlayers);
        yield return new WaitUntil(() => joinsTask.IsCompleted);
        string joinTargetName = joinsTask.Result;

        // 생성자가 뽑을 맵(생성 케이스에서만 사용)
        var scenes = sceneLists[modeIndex];
        string mapSelectedByCreator = scenes[Random.Range(0, scenes.Count)];

        if (!string.IsNullOrEmpty(joinTargetName))
        {
            // ✅ 2) 존재하는 방이 있으면 그 방에 조인
            var joinArgs = new StartGameArgs()
            {
                GameMode = GameMode.Shared,
                SessionName = joinTargetName,
                Scene = SceneRef.None,
                SceneManager = sceneManager
            };

            var joinResultTask = runner.StartGame(joinArgs);
            yield return new WaitUntil(() => joinResultTask.IsCompleted);

            if (!joinResultTask.Result.Ok)
            {
                Debug.LogWarning($"❌ 방 조인 실패 → 생성 시도: {joinResultTask.Result.ShutdownReason}");
                joinTargetName = null; // 생성 플로우로 전환
            }
        }

        if (string.IsNullOrEmpty(joinTargetName))
        {
            // ✅ 3) 조인할 방이 없거나 조인 실패 → 새 방 생성
            var sessionProps = new Dictionary<string, SessionProperty>()
            {
                { PROP_MODE, modeList[modeIndex] },          // string → SessionProperty (암시적 변환)
                { PROP_MAP,  mapSelectedByCreator }          // string → SessionProperty (암시적 변환)
            };

            var startOkTask = TryStartGameWithUniqueSessionAsync(
                runner: runner,
                basePrefix: $"Match_{modeList[modeIndex]}",
                maxPlayers: maxPlayers,
                sceneManager: sceneManager,
                sessionProps: sessionProps,
                maxAttempts: 8
            );
            yield return new WaitUntil(() => startOkTask.IsCompleted);

            if (!startOkTask.Result)
            {
                matchStatusText.text = "매칭 실패";
                playButton.GetComponentInChildren<TMP_Text>().text = "플레이";
                isMatching = false;
                leftModeButton.interactable = true;
                rightModeButton.interactable = true;
                matchStatusPanel.SetActive(false);
                Destroy(runnerObject);
                yield break;
            }
        }

        // ✅ StartGame 성공 → 세션 이름 저장
        if (runner != null && runner.SessionInfo.IsValid)
        {
            PlayerPrefs.SetString("LastSessionName", runner.SessionInfo.Name);
            PlayerPrefs.Save();
        }

        // 인원 모니터링
        matchStatusText.text = $"{modeList[modeIndex]} 모드 매칭 중...";
        while (isMatching && runner != null && runner.SessionInfo != null && runner.SessionInfo.PlayerCount < maxPlayers)
        {
            currentPlayerCount = runner.SessionInfo.PlayerCount;
            matchCountText.text = $"{currentPlayerCount}/{maxPlayers}";
            yield return new WaitForSeconds(0.5f);
        }

        if (!isMatching) yield break;

        matchStatusText.text = "매칭 완료!";
        matchCountText.text = "";
        playButton.interactable = false;
        matchComplete = true;

        yield return new WaitForSeconds(2f);

        if (runner == null || runner.SessionInfo == null)
        {
            matchStatusText.text = "매칭 실패";
            ResetUIAfterFail();
            yield break;
        }

        currentPlayerCount = runner.SessionInfo.PlayerCount;
        if (currentPlayerCount < maxPlayers)
        {
            matchStatusText.text = $"매칭 인원 부족...";
            ResetUIAfterFail();
            yield break;
        }

        // === 최종: 모든 플레이어가 같은 맵으로 ===
        string selectedScene = null;

        if (runner.SessionInfo != null &&
            runner.SessionInfo.Properties != null &&
            runner.SessionInfo.Properties.TryGetValue(PROP_MAP, out var mapProp))
        {
            // ✅ 핵심: ToString() 금지, 실제 값으로 변환
            selectedScene = mapProp;
        }

        if (string.IsNullOrEmpty(selectedScene))
        {
            // (방 생성자 경로의 안전망)
            selectedScene = mapSelectedByCreator;
        }

        yield return SceneManager.LoadSceneAsync(selectedScene);
    }

    // ✅ 추가: 같은 모드의 조인 가능 방 검색 (없으면 null)
    private async Task<string> FindJoinableSessionNameAsync(NetworkRunner runner, string mode, int maxPlayers)
    {
        // 1) 로비 진입
        var lobbyResult = await runner.JoinSessionLobby(SessionLobby.Shared);
        if (lobbyResult.ShutdownReason != ShutdownReason.Ok)
        {
            Debug.LogWarning($"❌ 로비 진입 실패: {lobbyResult.ShutdownReason}");
            return null;
        }

        // 2) 최신 세션 목록 대기 + 필터링 (MODE가 동일하고, 오픈/슬롯 여유 있는 방)
        float timeout = 3.0f;
        float t = 0f;

        while (t < timeout)
        {
            var list = MatchingRunnerCallback.LatestSessions; // 최신 세션 목록 (콜백에서 업데이트)
            if (list != null)
            {
                foreach (var s in list)
                {
                    if (!s.IsOpen || !s.IsVisible) continue;
                    if (s.PlayerCount >= s.MaxPlayers) continue;
                    if (s.Properties == null) continue;

                    if (s.Properties.TryGetValue(PROP_MODE, out var modeProp))
                    {
                        // ✅ 핵심: ToString()이 아닌 실제 값 비교 (SessionProperty → string)
                        string sessionMode = modeProp;
                        if (string.Equals(sessionMode, mode, System.StringComparison.Ordinal))
                        {
                            Debug.Log($"🔎 조인 가능 세션 발견: name={s.Name}, mode={sessionMode}, {s.PlayerCount}/{s.MaxPlayers}");
                            return s.Name;
                        }
                    }
                }
            }

            await Task.Delay(250);
            t += 0.25f;
        }

        // 조건에 맞는 방 없음
        return null;
    }

    // 세션 이름 중복 시 재시도하며 StartGame을 시도하는 보조 메서드 (필요 최소 추가)
    private async Task<bool> TryStartGameWithUniqueSessionAsync(
        NetworkRunner runner,
        string basePrefix,
        int maxPlayers,
        NetworkSceneManagerDefault sceneManager,
        Dictionary<string, SessionProperty> sessionProps,
        int maxAttempts = 8)
    {
        // 예: Match_TDM#1234
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            string sessionName = $"{basePrefix}#{Random.Range(1000, 10000)}";

            var startArgs = new StartGameArgs()
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                Scene = SceneRef.None,                 // 씬은 세션 프로퍼티로 통일 → 각 클라가 동일 이름으로 로드
                PlayerCount = maxPlayers,
                SceneManager = sceneManager,
                SessionProperties = sessionProps
            };

            var result = await runner.StartGame(startArgs);
            if (result.Ok)
            {
                // 세션 생성 or 조인 성공
                return true;
            }

            // 이름 중복 등으로 실패 → 다음 이름으로 재시도
            await Task.Yield();
        }
        return false;
    }

    void CancelMatching()
    {
        isMatching = false;
        matchComplete = false;
        matchStatusPanel.SetActive(false);

        playButton.GetComponentInChildren<TMP_Text>().text = "플레이";
        leftModeButton.interactable = true;
        rightModeButton.interactable = true;
        playButton.interactable = true;

        if (matchingCoroutine != null)
        {
            StopCoroutine(matchingCoroutine);
            matchingCoroutine = null;
        }

        if (runner != null)
        {
            runner.Shutdown();
            Destroy(runner.gameObject);
            runner = null;
        }
    }

    private void ResetUIAfterFail()
    {
        playButton.GetComponentInChildren<TMP_Text>().text = "플레이";
        playButton.interactable = true;
        isMatching = false;
        matchComplete = false;
        leftModeButton.interactable = true;
        rightModeButton.interactable = true;
    }
}