using Fusion;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class PlaySceneNetworkSpawner : SimulationBehaviour
{
    public static PlaySceneNetworkSpawner Instance { get; private set; }

    [Header("네트워크 프리팹")]
    [SerializeField] private NetworkPrefabRef teamAssignerPrefab;
    [SerializeField] private NetworkPrefabRef characterSelectionNetworkDataPrefab;
    [SerializeField] private NetworkPrefabRef fusionTimerDataPrefab;
    [SerializeField] private NetworkPrefabRef matchStatusTrackerPrefab;

    [Header("캐릭터 프리팹들 (NetworkPrefabRef)")]
    [SerializeField] private List<NetworkPrefabRef> characterPrefabRefs;

    [Header("캐릭터 ID 매핑 순서")]
    [SerializeField] private List<string> characterIds;

    [Header("캐릭터 아이콘 매핑 (ID 순서와 동일)")]
    [SerializeField] private List<CharacterInfo> characterInfos;

    [Header("스폰 포인트")]
    [SerializeField] private Transform[] redTeamSpawnPoints;
    [SerializeField] private Transform[] blueTeamSpawnPoints;

    [Header("모드 핸들러 프리팹")]
    [SerializeField] private NetworkPrefabRef tdmModeHandlerPrefab;
    [SerializeField] private NetworkPrefabRef flagModeHandlerPrefab;
    [SerializeField] private NetworkPrefabRef bombModeHandlerPrefab;
    [SerializeField] private NetworkPrefabRef fdModeHandlerPrefab;

    [Header("BOMB 모드")]
    public NetworkPrefabRef bombSitePrefab;
    public Transform[] bombSiteAnchors = new Transform[2];
    private readonly List<NetworkObject> _bombSiteInstances = new List<NetworkObject>(2);

    private NetworkRunner runner;

    private void Awake()
    {
        Instance = this;
    }

    private IEnumerator Start()
    {
        var candidates = FindObjectsOfType<NetworkRunner>(true);
        runner = candidates.FirstOrDefault(r => r != null && r.IsRunning)
              ?? candidates.FirstOrDefault();

        if (runner == null)
        {
            Debug.LogError("❌ NetworkRunner를 찾지 못함");
            yield break;
        }

        // ✅ 마스터 클라이언트가 필요한 네트워크 오브젝트만 생성
        if (runner.IsSharedModeMasterClient)
        {
            runner.Spawn(matchStatusTrackerPrefab);

            if (FindObjectsOfType<TeamAssigner>().Length == 0)
                runner.Spawn(teamAssignerPrefab);

            if (FindObjectsOfType<FusionTimerData>().Length == 0)
                runner.Spawn(fusionTimerDataPrefab);

            if (FindObjectsOfType<CharacterSelectionNetworkData>().Length == 0)
                runner.Spawn(characterSelectionNetworkDataPrefab);
        }

        // 네트워크 싱글톤 준비 대기
        while (CharacterSelectionNetworkData.Instance == null || TeamAssigner.Instance == null || FusionTimerData.Instance == null)
            yield return null;

        bool isTutorialPractice =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Tutorial" &&
            UserInfoManager.Instance != null &&
            !UserInfoManager.Instance.IsTutorialCompleted;

        // ✅ 튜토리얼 미완료 연습방: "내가 캐릭터 선택 + Ready"면 즉시 스폰
        if (isTutorialPractice)
        {
            Debug.Log("🧪 튜토리얼 연습방 감지 → Ready 즉시 캐릭터 스폰 대기");

            // 내 선택 + Ready 완료될 때까지 대기
            while (true)
            {
                var data = CharacterSelectionNetworkData.Instance;

                bool hasSelected = data.SelectedCharacters.TryGet(runner.LocalPlayer, out var id) && !string.IsNullOrEmpty(id);
                bool isReady = data.ReadyStates.TryGet(runner.LocalPlayer, out var r) && r;

                if (hasSelected && isReady)
                    break;

                yield return null;
            }

            Debug.Log("✅ 튜토리얼 연습방: 조건 충족 → 캐릭터 생성 시작");

            if (runner.IsSharedModeMasterClient)
            {
                SpawnCharacters(); // 마스터가 모든 캐릭터 스폰
            }

            SpawnModeHandler(); // 핸들러 스폰
            yield return StartCoroutine(WaitForMyCharacterAndZoom());
            yield break;
        }

        // ✅ 일반 매치: 파이널 카운트다운 끝나야만 캐릭터 생성(기존 그대로)
        yield return new WaitUntil(() =>
            FusionTimerData.Instance != null &&
            FusionTimerData.Instance.IsFinalCountdown &&
            FusionTimerData.Instance.GetRemainingFinalCountdown() <= 0f
        );

        Debug.Log("⏳ 캐릭터 생성 시작");

        if (runner.IsSharedModeMasterClient)
        {
            SpawnCharacters(); // 마스터가 모든 캐릭터 스폰
        }

        SpawnModeHandler(); // 먼저 핸들러부터 스폰
        yield return StartCoroutine(WaitForMyCharacterAndZoom());
    }

    public void SpawnCharacters()
    {
        var data = CharacterSelectionNetworkData.Instance;
        var teams = TeamAssigner.Instance;

        if (data == null || teams == null)
        {
            Debug.LogError("❌ 캐릭터 생성에 필요한 네트워크 데이터 없음");
            return;
        }

        foreach (var player in runner.ActivePlayers)
        {
            if (!data.SelectedCharacters.TryGet(player, out var charId))
            {
                Debug.LogWarning($"⚠️ 선택 캐릭터 없음: {player}");
                continue;
            }

            if (!teams.Teams.TryGet(player, out TeamAssigner.Team team))
            {
                Debug.LogWarning($"⚠️ 팀 정보 없음: {player}");
                continue;
            }

            int index = characterIds.IndexOf(charId);
            if (index == -1 || index >= characterPrefabRefs.Count)
            {
                Debug.LogError($"❌ 캐릭터 ID '{charId}'에 해당하는 프리팹 없음");
                continue;
            }

            var prefabRef = characterPrefabRefs[index];
            var spawnPoint = GetSpawnPoint(team);
            if (spawnPoint == null)
            {
                Debug.LogError($"❌ 스폰 위치 없음: {team}");
                continue;
            }

            runner.Spawn(prefabRef, spawnPoint.position, spawnPoint.rotation, inputAuthority: player, (runner, obj) =>
            {
                if (runner.TryGetPlayerObject(player, out var existingObj))
                {
                    Debug.LogWarning($"⚠️ PlayerObject 중복 등록 시도 차단: {player}");
                    return;
                }

                runner.SetPlayerObject(player, obj);
                Debug.Log($"✅ 캐릭터 생성 및 등록 완료: {player}");
            });
        }
    }

    public Transform GetSpawnPoint(TeamAssigner.Team team)
    {
        if (team == TeamAssigner.Team.Red && redTeamSpawnPoints.Length > 0)
            return redTeamSpawnPoints[Random.Range(0, redTeamSpawnPoints.Length)];
        if (team == TeamAssigner.Team.Blue && blueTeamSpawnPoints.Length > 0)
            return blueTeamSpawnPoints[Random.Range(0, blueTeamSpawnPoints.Length)];
        return null;
    }

    private IEnumerator ZoomInAfterSpawned(NetworkObject characterObj)
    {
        float timeout = 5f;
        float timer = 0f;

        while (!characterObj || !characterObj.transform)
        {
            timer += Time.deltaTime;
            if (timer > timeout)
            {
                Debug.LogError("❌ 캐릭터 transform 접근 실패 (줌인 불가)");
                yield break;
            }
            yield return null;
        }

        yield return new WaitForSeconds(0.2f); // 애니메이션 등 초기화 대기

        Debug.Log($"✅ 내 캐릭터 Transform 확인 → 카메라 줌인 시작: {characterObj.name}");
        CameraZoomController.Instance.ZoomInToTarget(characterObj.transform);
    }

    private IEnumerator WaitForMyCharacterAndZoom()
    {
        float timeout = 10f;
        float timer = 0f;
        NetworkObject myCharacter = null;

        Debug.Log("👀 내 캐릭터 스폰 대기 시작");

        while (timer < timeout)
        {
            if (runner != null && runner.IsRunning &&
            runner.TryGetPlayerObject(runner.LocalPlayer, out myCharacter) &&
            myCharacter != null && myCharacter.HasInputAuthority)
                break;

            // fallback: InputAuthority로 직접 찾기 (단 한 번만 등록 시도)
            if (myCharacter == null)
            {
                foreach (var obj in FindObjectsOfType<NetworkObject>())
                {
                    if (obj.InputAuthority == runner.LocalPlayer)
                    {
                        myCharacter = obj;
                        Debug.LogWarning("⚠️ Fallback 성공: 내 캐릭터 직접 검색됨");
                        break;
                    }
                }
            }

            if (myCharacter != null && myCharacter.HasInputAuthority)
                break;

            timer += Time.deltaTime;
            yield return null;
        }

        if (myCharacter == null || !myCharacter.HasInputAuthority)
        {
            Debug.LogError($"❌ [줌인 실패] 내 캐릭터 오브젝트를 끝까지 찾지 못함. LocalPlayer={runner.LocalPlayer}");
            yield break;
        }

        yield return new WaitForSeconds(0.5f);
        Debug.Log($"✅ 내 캐릭터 발견됨 → 줌인 시작: {myCharacter.name}");
        CameraZoomController.Instance.ZoomInToTarget(myCharacter.transform);
    }

    private void SpawnModeHandler()
    {
        var mode = GameModeHelper.GetModeFromScene();
        NetworkPrefabRef prefab = mode switch
        {
            GameModeHelper.GameMode.TDM => tdmModeHandlerPrefab,
            GameModeHelper.GameMode.FLAG => flagModeHandlerPrefab,
            GameModeHelper.GameMode.BOMB => bombModeHandlerPrefab,
            GameModeHelper.GameMode.FD => fdModeHandlerPrefab,
            _ => tdmModeHandlerPrefab
        };

        if (runner.IsSharedModeMasterClient)
            runner.Spawn(prefab);
    }

    public Sprite GetCharacterIconById(string characterId)
    {
        if (string.IsNullOrEmpty(characterId) || characterInfos == null) return null;

        // 1) ID 인덱스 기반(인스펙터 순서 보장)
        int idx = characterIds != null ? characterIds.IndexOf(characterId) : -1;
        if (idx >= 0 && characterInfos != null && idx < characterInfos.Count && characterInfos[idx] != null)
            return characterInfos[idx].characterIcon;

        // 2) 혹시 순서가 어긋나도 대비(직접 ID 검색)
        var info = characterInfos.FirstOrDefault(ci => ci != null && ci.characterId == characterId);
        return info != null ? info.characterIcon : null;
    }

    /// <summary>BOMB 라운드용 사이트 2개 보장 생성(SharedMode의 StateAuthority만 호출)</summary>
    public void EnsureBombSites()
    {
        // ✅ SimulationBehaviour이므로 Object 체크 금지
        if (runner == null) runner = FindObjectOfType<NetworkRunner>();
        if (runner == null)
        {
            Debug.LogError("❌ EnsureBombSites: NetworkRunner를 찾지 못함");
            return;
        }

        // ✅ Shared Mode의 마스터만 스폰
        if (!runner.IsSharedModeMasterClient) return;

        // 기존 것 정리
        for (int i = 0; i < _bombSiteInstances.Count; i++)
        {
            if (_bombSiteInstances[i] != null)
                runner.Despawn(_bombSiteInstances[i]);
        }
        _bombSiteInstances.Clear();

        // 2개 생성
        for (int i = 0; i < bombSiteAnchors.Length && i < 2; i++)
        {
            var anchor = bombSiteAnchors[i];
            if (anchor == null) continue;

            var no = runner.Spawn(bombSitePrefab, anchor.position, anchor.rotation);
            var site = no.GetComponent<BombSite>();
            if (site != null) site.siteIndex = i;

            _bombSiteInstances.Add(no);
        }

        Debug.Log($"✅ EnsureBombSites: {_bombSiteInstances.Count}개 스폰 완료");
    }

    /// <summary>라운드 시작 시 사이트 초기화/활성</summary>
    public void ResetBombSitesForNewRound()
    {
        for (int i = 0; i < _bombSiteInstances.Count; i++)
        {
            var no = _bombSiteInstances[i];
            if (no == null) continue;

            var site = no.GetComponent<BombSite>();
            if (site == null) continue;

            site.ResetForRound();
            site.ActivateForRound();
        }
        Debug.Log("✅ ResetBombSitesForNewRound: 사이트 초기화/활성 완료");
    }

    public BombSite GetSite(int index)
    {
        if (index < 0 || index >= _bombSiteInstances.Count) return null;
        var no = _bombSiteInstances[index];
        return no ? no.GetComponent<BombSite>() : null;
    }
}