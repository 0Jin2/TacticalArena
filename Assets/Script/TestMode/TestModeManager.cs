#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 에디터 전용 테스트 모드 (배포 빌드에는 포함되지 않음)
// - 테스트 로비에서 맵/캐릭터를 골라 바로 입장 (매칭 인원 대기 없음)
// - 같은 방 코드로 들어오면 함께 플레이 (MPPM 가상 플레이어, 다른 PC의 에디터)
// - 게임 중 디버그 창에서 맵 변경 / 내 캐릭터 변경
public class TestModeManager : MonoBehaviour
{
    public static TestModeManager Instance { get; private set; }

    public const string TestLobbyScenePath = "Assets/Scenes/Test/TestLobby.unity";
    private const string PlayScenesFolder = "Assets/Scenes/Play";
    private const string SessionPrefix = "TEST_";
    private const string PROP_MODE = "MODE";
    private const string PROP_MAP = "MAP";
    private const string PROP_GEN = "GEN";
    private const string TestModeValue = "TEST"; // 실제 매칭 검색(MODE 비교)에 절대 걸리지 않도록

    [Header("타이머 단축 (초)")]
    public float selectionSeconds = 5f;
    public float finalCountdownSeconds = 2f;

    public string RoomCode { get; private set; }
    public string MapPath { get; private set; }
    public string CharacterId { get; set; }
    public bool IsBusy { get; private set; }
    public string Status { get; private set; } = "";
    public NetworkRunner Runner => runner;
    public bool IsMaster => runner != null && runner.IsRunning && runner.IsSharedModeMasterClient;

    private NetworkRunner runner;
    private int generation;
    private float nextPollTime;
    private float spawnPhaseTime = -1f;
    private FusionTimerData tunedTimer;
    private NetworkObject myCharacter;
    private readonly Dictionary<PlayerRef, (string id, NetworkObject obj)> spawned = new();

    public static TestModeManager Ensure()
    {
        if (Instance != null) return Instance;
        // 로비를 거치지 않으므로 게임 씬이 기대하는 로비 싱글톤을 직접 생성 (스스로 DontDestroyOnLoad)
        if (UserCharacterDataManager.Instance == null)
            new GameObject("UserCharacterDataManager").AddComponent<UserCharacterDataManager>();

        var go = new GameObject("TestMode");
        DontDestroyOnLoad(go);
        go.AddComponent<TestModeDebugWindow>();
        return go.AddComponent<TestModeManager>();
    }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ───────── 맵/캐릭터 목록 (에디터 에셋 기준) ─────────

    public static List<string> FindMapPaths()
    {
        return AssetDatabase.FindAssets("t:Scene", new[] { PlayScenesFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !p.Contains("Tutorial")) // 튜토리얼은 진행 방식이 달라 제외 (일반 로비 연습방으로 테스트)
            .OrderBy(p => p)
            .ToList();
    }

    public static List<string> FindCharacterIds()
    {
        return AssetDatabase.FindAssets("t:CharacterInfo")
            .Select(g => AssetDatabase.LoadAssetAtPath<CharacterInfo>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(info => info != null && !string.IsNullOrEmpty(info.characterId))
            .Select(info => info.characterId)
            .Distinct()
            .ToList();
    }

    public static string MapName(string path) => System.IO.Path.GetFileNameWithoutExtension(path);

    // ───────── 세션 시작/전환 ─────────

    /// <summary>방 코드로 세션 생성 또는 참가 후 맵 로드. 이미 있는 방이면 그 방의 맵을 따른다.</summary>
    public void StartSession(string roomCode, string mapPath, int maxPlayers, int gen = 0)
    {
        if (IsBusy) return;
        StartCoroutine(Co_StartSession(roomCode, mapPath, maxPlayers, gen));
    }

    private IEnumerator Co_StartSession(string roomCode, string mapPath, int maxPlayers, int gen)
    {
        IsBusy = true;
        Status = $"방 '{roomCode}' 접속 중...";

        yield return ShutdownRunners();

        var runnerObject = new GameObject("NetworkRunner");
        DontDestroyOnLoad(runnerObject);
        runner = runnerObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        var sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
        var callback = runnerObject.AddComponent<MatchingRunnerCallback>();
        callback.autoReconnectEnabled = false; // 실제 매치 세션(LastSessionName)으로 재접속 시도하지 않도록
        runner.AddCallbacks(callback);

        var startTask = runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = $"{SessionPrefix}{roomCode}_{gen}",
            Scene = SceneRef.None,
            SceneManager = sceneManager,
            PlayerCount = maxPlayers,
            IsVisible = false,
            SessionProperties = new Dictionary<string, SessionProperty>()
            {
                { PROP_MODE, TestModeValue },
                { PROP_MAP, mapPath },
                { PROP_GEN, gen }
            }
        });
        yield return new WaitUntil(() => startTask.IsCompleted);

        if (!startTask.Result.Ok)
        {
            Status = $"접속 실패: {startTask.Result.ShutdownReason}";
            Debug.LogWarning($"[TestMode] {Status}");
            Destroy(runnerObject);
            runner = null;
            IsBusy = false;
            yield break;
        }

        // 이미 있던 방에 들어온 경우 방장이 정한 맵을 따름
        if (runner.SessionInfo.Properties != null &&
            runner.SessionInfo.Properties.TryGetValue(PROP_MAP, out var mapProp) && mapProp.IsString)
        {
            mapPath = (string)mapProp.PropertyValue;
        }

        RoomCode = roomCode;
        MapPath = mapPath;
        generation = gen;
        ResetMapState();

        Debug.Log($"[TestMode] 방 '{roomCode}' 입장 ({runner.SessionInfo.PlayerCount}명) → {MapName(mapPath)}");
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(mapPath, new LoadSceneParameters(LoadSceneMode.Single));

        Status = "";
        IsBusy = false;
    }

    /// <summary>방장만 가능: 방 전체를 새 맵으로 이동 (모두 새 세션으로 재접속)</summary>
    public void RequestMapChange(string mapPath)
    {
        if (IsBusy || runner == null || !runner.IsRunning) return;
        if (!runner.IsSharedModeMasterClient)
        {
            Status = "맵 변경은 방장만 할 수 있어요";
            return;
        }

        int next = generation + 1;
        runner.SessionInfo.UpdateCustomProperties(new Dictionary<string, SessionProperty>()
        {
            { PROP_MODE, TestModeValue },
            { PROP_MAP, mapPath },
            { PROP_GEN, next }
        });
        StartCoroutine(Co_MoveAfterBroadcast(mapPath, next));
    }

    private IEnumerator Co_MoveAfterBroadcast(string mapPath, int gen)
    {
        IsBusy = true;
        Status = "맵 변경 중...";
        yield return new WaitForSecondsRealtime(1f); // 다른 플레이어가 변경을 받을 시간
        IsBusy = false;
        StartSession(RoomCode, mapPath, runner != null ? runner.SessionInfo.MaxPlayers : 10, gen);
    }

    /// <summary>내 캐릭터 변경 요청 (방장이 기존 캐릭터를 같은 위치에서 교체)</summary>
    public void RequestCharacterChange(string characterId)
    {
        CharacterId = characterId;
        var data = CharacterSelectionNetworkData.Instance;
        if (data != null && runner != null && runner.IsRunning)
            data.RequestSelectCharacter(characterId);
    }

    public void BackToTestLobby()
    {
        StopAllCoroutines();
        StartCoroutine(Co_BackToTestLobby());
    }

    private IEnumerator Co_BackToTestLobby()
    {
        IsBusy = true;
        yield return ShutdownRunners();
        RoomCode = null;
        MapPath = null;
        ResetMapState();
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(TestLobbyScenePath, new LoadSceneParameters(LoadSceneMode.Single));
        IsBusy = false;
        Status = "";
    }

    private IEnumerator ShutdownRunners()
    {
        foreach (var r in FindObjectsByType<NetworkRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null) continue;
            if (r.IsRunning)
            {
                var shutdownTask = r.Shutdown();
                yield return new WaitUntil(() => shutdownTask.IsCompleted);
            }
            if (r != null) Destroy(r.gameObject);
        }
        runner = null;
        yield return null;
    }

    private void ResetMapState()
    {
        spawned.Clear();
        myCharacter = null;
        tunedTimer = null;
        spawnPhaseTime = -1f;
    }

    // ───────── 게임 진행 보조 ─────────

    private void Update()
    {
        if (IsBusy || runner == null || !runner.IsRunning || MapPath == null) return;

        TuneTimer();
        FollowMyCharacter();

        if (Time.unscaledTime < nextPollTime) return;
        nextPollTime = Time.unscaledTime + 0.5f;

        if (CheckMapChangeBroadcast()) return;
        AutoSelectCharacter();
        if (runner.IsSharedModeMasterClient)
            EnsureCharacters();
    }

    // 선택 30초 + 카운트다운 10초 → 테스트용으로 단축 (모든 클라이언트가 같은 값 사용)
    private void TuneTimer()
    {
        var timer = FusionTimerData.Instance;
        if (timer == null || timer == tunedTimer) return;
        timer.SelectionDuration = selectionSeconds;
        timer.FinalCountdownDuration = finalCountdownSeconds;
        tunedTimer = timer;
    }

    private bool CheckMapChangeBroadcast()
    {
        var props = runner.SessionInfo.Properties;
        if (props == null || !props.TryGetValue(PROP_GEN, out var genProp) || !genProp.IsInt) return false;

        int gen = (int)genProp.PropertyValue;
        if (gen <= generation) return false;

        string map = props.TryGetValue(PROP_MAP, out var mapProp) && mapProp.IsString ? (string)mapProp.PropertyValue : MapPath;
        Debug.Log($"[TestMode] 방장이 맵을 변경함 → {MapName(map)}");
        StartSession(RoomCode, map, runner.SessionInfo.MaxPlayers, gen);
        return true;
    }

    private void AutoSelectCharacter()
    {
        var data = CharacterSelectionNetworkData.Instance;
        var spawner = PlaySceneNetworkSpawner.Instance;
        if (data == null || spawner == null || !runner.LocalPlayer.IsValid) return;
        if (data.SelectedCharacters.ContainsKey(runner.LocalPlayer)) return;

        var ids = spawner.CharacterIds;
        if (ids == null || ids.Count == 0) return;
        if (string.IsNullOrEmpty(CharacterId) || !ids.Contains(CharacterId))
            CharacterId = ids[0];

        data.RequestSelectCharacter(CharacterId);
    }

    // 방장 전용: 늦게 들어온 플레이어 팀 배정/캐릭터 생성, 캐릭터 변경 요청 처리
    private void EnsureCharacters()
    {
        var data = CharacterSelectionNetworkData.Instance;
        var teams = TeamAssigner.Instance;
        var timer = FusionTimerData.Instance;
        var spawner = PlaySceneNetworkSpawner.Instance;
        if (data == null || teams == null || timer == null || spawner == null) return;

        // 기본 스포너가 첫 캐릭터를 만든 뒤부터 개입 (중복 생성 방지)
        bool spawnPhase = timer.IsFinalCountdown && timer.GetRemainingFinalCountdown() <= 0f;
        if (!spawnPhase) return;
        if (spawnPhaseTime < 0f) spawnPhaseTime = Time.unscaledTime;
        if (Time.unscaledTime - spawnPhaseTime < 1.5f) return;

        foreach (var player in runner.ActivePlayers)
        {
            if (teams.Object.HasStateAuthority && !teams.Teams.ContainsKey(player))
            {
                int red = teams.Teams.Count(kv => kv.Value == TeamAssigner.Team.Red);
                int blue = teams.Teams.Count(kv => kv.Value == TeamAssigner.Team.Blue);
                teams.Teams.Add(player, red <= blue ? TeamAssigner.Team.Red : TeamAssigner.Team.Blue);
            }

            if (!data.SelectedCharacters.TryGet(player, out var selectedId)) continue;

            spawned.TryGetValue(player, out var current);
            if (current.obj == null)
            {
                var existing = FindCharacterOf(player);
                if (existing != null)
                {
                    spawned[player] = (selectedId, existing); // 기본 스포너가 만든 캐릭터
                    continue;
                }

                var created = spawner.SpawnCharacterFor(player, selectedId);
                if (created != null) spawned[player] = (selectedId, created);
                continue;
            }

            if (current.id != selectedId)
            {
                Vector3 position = current.obj.transform.position;
                runner.Despawn(current.obj);
                var replaced = spawner.SpawnCharacterFor(player, selectedId, position);
                spawned[player] = (selectedId, replaced);
                Debug.Log($"[TestMode] {player} 캐릭터 변경: {current.id} → {selectedId}");
            }
        }
    }

    private NetworkObject FindCharacterOf(PlayerRef player)
    {
        foreach (var controller in FindObjectsByType<PlayerNetworkController>(FindObjectsSortMode.None))
        {
            if (controller.Object != null && controller.Object.InputAuthority == player)
                return controller.Object;
        }
        return null;
    }

    // 캐릭터가 새로 생기거나 바뀌면 카메라가 따라가도록
    private void FollowMyCharacter()
    {
        if (!runner.LocalPlayer.IsValid) return;
        if (myCharacter != null && myCharacter.InputAuthority == runner.LocalPlayer) return;

        var mine = FindCharacterOf(runner.LocalPlayer);
        if (mine == null || mine == myCharacter) return;

        myCharacter = mine;
        runner.SetPlayerObject(runner.LocalPlayer, mine);
        CameraZoomController.Instance?.ZoomInToTarget(mine.transform);
    }
}
#endif
