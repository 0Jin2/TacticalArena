using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MatchingRunnerCallback : MonoBehaviour, INetworkRunnerCallbacks
{
    public static List<SessionInfo> LatestSessions { get; private set; } = new List<SessionInfo>();
    public event System.Action<PlayerRef, bool> OnPlayerConnectionChanged;

    // === 추가: 자동 재접속 설정 ===
    [Header("Auto Reconnect")]
    [Tooltip("포커스 상실/복귀 시 자동 재접속을 시도합니다.")]
    public bool autoReconnectEnabled = true;

    [Tooltip("포커스 상실 후 재접속을 시도하기 전에 대기하는 시간(초, Real-time)")]
    public float reconnectDelayOnPause = 0.6f;

    [Tooltip("재접속 시도 최대 횟수")]
    public int reconnectMaxAttempts = 3;

    [Tooltip("재접속 시도 간격(초, Real-time)")]
    public float reconnectRetryInterval = 0.8f;

    private bool _reconnectInProgress;

    void Update()
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null && runner.LocalPlayer.IsValid)
        {
            runner.ProvideInput = true;
        }
    }

    // === 추가: 앱 포커스/일시정지 훅 ===
    void OnApplicationPause(bool paused)
    {
        if (!autoReconnectEnabled) return;

        if (paused)
        {
            // 백그라운드 진입 직후: 잠깐 기다렸다가 “정말 끊겼으면” 자동 재접속
            StartCoroutine(TryAutoReconnectWithDelay(reconnectDelayOnPause));
        }
        else
        {
            // 포그라운드 복귀: 즉시 재접속 점검
            StartCoroutine(TryAutoReconnectWithDelay(0f));
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!autoReconnectEnabled) return;

        if (hasFocus)
            StartCoroutine(TryAutoReconnectWithDelay(0f));
    }

    private IEnumerator TryAutoReconnectWithDelay(float delayRealtime)
    {
        if (_reconnectInProgress) yield break;
        if (delayRealtime > 0f)
            yield return new WaitForSecondsRealtime(delayRealtime);

        yield return TryAutoReconnectCoroutine();
    }

    private IEnumerator TryAutoReconnectCoroutine()
    {
        if (_reconnectInProgress) yield break;
        _reconnectInProgress = true;

        var runner = FindObjectOfType<NetworkRunner>();
        string lastSession = PlayerPrefs.GetString("LastSessionName", string.Empty);

        // 러너가 없다면 만들 이유는 없습니다(이 컴포넌트는 러너에 붙어있기 때문)
        if (runner == null || string.IsNullOrEmpty(lastSession))
        {
            _reconnectInProgress = false;
            yield break;
        }

        // 이미 정상 연결 중이면 아무 것도 안 함
        // (간단 체크: 유효한 세션이면 연결 유지 중인 경우가 많습니다)
        if (runner.SessionInfo.IsValid && runner.SessionInfo.IsOpen)
        {
            _reconnectInProgress = false;
            yield break;
        }

        // SceneManager 확보
        var sceneMgr = runner.GetComponent<NetworkSceneManagerDefault>();
        if (sceneMgr == null)
            sceneMgr = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        int attempts = 0;
        bool ok = false;

        while (attempts < reconnectMaxAttempts && !ok)
        {
            attempts++;

            // 세션 이름으로 재조인 시도 (Shared 모드)
            var startArgs = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = lastSession,
                Scene = SceneRef.None,                // 씬은 세션 상태를 따르거나, 현재 씬 유지
                SceneManager = sceneMgr
            };

            var startTask = runner.StartGame(startArgs);
            yield return new WaitUntil(() => startTask.IsCompleted);

            if (startTask.Result.Ok)
            {
                ok = true;
                break;
            }

            yield return new WaitForSecondsRealtime(reconnectRetryInterval);
        }

        if (ok)
        {
            // 입력 활성화 보장
            runner.ProvideInput = true;

            // 선택/타이머 UI 바인딩 재연결(원래 콜백이 하던 역할과 일관 유지)
            CharacterSelectionManager.Instance?.RebindTimerEvents();

            Debug.Log("✅ 자동 재접속 성공");
        }
        else
        {
            Debug.LogWarning("❌ 자동 재접속 실패");
        }

        _reconnectInProgress = false;
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
        Debug.Log("✅ 서버에 연결되었습니다.");
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[OnPlayerJoined] player={player}");

        if (player == runner.LocalPlayer && !runner.ProvideInput)
        {
            runner.ProvideInput = true;
            Debug.Log("✅ ProvideInput 강제 설정 (Join 시)");
        }
        OnPlayerConnectionChanged?.Invoke(player, true);

        // ✅ 로컬 플레이어라면 닉네임 재송신 보장 (TeamAssigner/CSND 복제 지연 대비)
        if (player == runner.LocalPlayer)
            StartCoroutine(Co_EnsureNicknameAfterJoin(runner));
    }

    private IEnumerator Co_EnsureNicknameAfterJoin(NetworkRunner runner)
    {
        float timeout = 8f;
        float t = 0f;

        while (t < timeout && (CharacterSelectionNetworkData.Instance == null || TeamAssigner.Instance == null))
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (CharacterSelectionNetworkData.Instance != null && TeamAssigner.Instance != null)
        {
            var local = runner.LocalPlayer;
            var nick = GPGS_BACKEND.MyNickname;

            if (!TeamAssigner.Instance.Nicknames.ContainsKey(local))
            {
                CharacterSelectionNetworkData.Instance.RequestNickname(nick);
                Debug.Log("🔁 조인 직후 닉네임 재요청");
            }
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"[OnPlayerLeft] player={player}");
        OnPlayerConnectionChanged?.Invoke(player, false);

        // 플레이어가 떠났을 때 즉시 캐릭터가 사라지는 느낌을 줄이려면
        // 실제 Despawn 로직을 소유 스크립트(스포너/컨트롤러)에서
        // “그레이스 타임(예: 3~5초)” 후 처리하도록 옮기는 것을 권장합니다.
        // (여기서는 네트워크 기본 콜백만 유지)
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"❌ 러너 종료됨: {shutdownReason}");
    }

    public void OnDisconnectedFromServer(NetworkRunner runner)
    {
        Debug.Log("❌ 서버와 연결이 끊어졌습니다.");

        // 포그라운드에 있다면 즉시 재접속 시도
        if (autoReconnectEnabled && Application.isFocused)
            StartCoroutine(TryAutoReconnectWithDelay(0f));
    }

    //컴파일 오류 방지용
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        // ✅ 최신 세션 목록을 정적 프로퍼티에 저장
        LatestSessions = sessionList ?? new List<SessionInfo>();
        // Debug.Log($"[Lobby] 세션 {LatestSessions.Count}개 수신");
    }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data) { }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var player = runner.LocalPlayer;

        if (!runner.TryGetPlayerObject(player, out var obj))
        {
            foreach (var nObj in FindObjectsOfType<NetworkObject>())
            {
                if (nObj.InputAuthority == player)
                {
                    runner.SetPlayerObject(player, nObj);
                    obj = nObj;
                    break;
                }
            }

            if (obj == null)
            {
                return;
            }
        }

        var localInput = obj.GetComponent<PlayerLocalInput>();
        if (localInput != null)
        {
            var inputData = localInput.GetInputData();
            input.Set(inputData);
        }
        else
        {
            Debug.LogWarning("❌ PlayerLocalInput 컴포넌트 없음");
        }
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner runner, NetAddress remoteAddress) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }

    /// <summary>
    /// 호스트 마이그레이션 발생 시 세션을 재시작하고,
    /// 캐릭터 선택 UI 매니저가 타이머 이벤트를 재구독하도록 트리거합니다.
    /// </summary>
    public async void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
        Debug.Log("🌀 Host Migration 발생 - 새로운 호스트로 전환됩니다");

        var result = await runner.StartGame(new StartGameArgs
        {
            HostMigrationToken = hostMigrationToken,
            SceneManager = runner.GetComponent<NetworkSceneManagerDefault>(),
        });

        if (!result.Ok)
        {
            Debug.LogError($"❌ Host Migration 재시작 실패: {result.ShutdownReason}");
            return;
        }

        Debug.Log("✅ Host Migration 재시작 완료");

        // 기존 FusionTimerData 인스턴스를 그대로 사용하도록 UI만 리바인딩
        CharacterSelectionManager.Instance?.RebindTimerEvents();
    }

    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
}
