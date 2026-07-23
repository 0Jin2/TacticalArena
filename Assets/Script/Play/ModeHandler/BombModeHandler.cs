using System.Collections;
using Fusion;
using UnityEngine;

/// <summary>
/// BOMB(폭탄 설치) 규칙 구현:
/// - 5라운드(선승 3), 라운드마다 설치팀<->해제팀 교대
/// - 라운드 타이머 5분, 설치 시 60초로 전환
/// - 라운드 중 리스폰 없음
/// - 설치 전 전멸: 생존 팀 즉시 승
/// - 설치 후: 해제팀 전멸 또는 양 팀 전멸 즉시 설치팀 승 / 해제 성공 시 해제팀 승 / 60초 만료 시 설치팀 승
/// </summary>
public class BombModeHandler : ModeHandlerBase
{
    // ----- 네트워크 상태 -----
    [Networked] public int TotalRounds { get; private set; } = 5;
    [Networked] public int CurrentRound { get; private set; } = 1;

    [Networked] public float RoundTime { get; private set; } = 300f;   // 5분
    [Networked] public float BombCountdown { get; private set; } = 60f; // 1분

    [Networked] public bool BombPlanted { get; private set; }
    [Networked] public int ActiveSiteIndex { get; private set; } = -1;

    [Networked] private TickTimer RoundTimer { get; set; }

    [Networked] public NetworkBool PlantInProgress { get; private set; } // ✅ 중복 설치 방지
    [SerializeField] private float plantTime = 6f; // ✅ 요구사항: 6초
    [SerializeField] private float defuseTime = 6f;

    private PlaySceneNetworkSpawner _spawner;
    private TeamAssigner _teams;
    private MatchStatusTracker _status;
    private GamePlayUIController _ui;

    [Networked] private bool IsEnded { get; set; }
    [Networked(OnChanged = nameof(OnWinnerChanged))]
    private TeamAssigner.Team Winner { get; set; }

    // ===== 공용 유틸 =====
    private void StartRoundTimer(float seconds)
    {
        RoundTimer = TickTimer.CreateFromSeconds(Runner, seconds);
    }

    private bool IsRoundTimerExpired()
    {
        return RoundTimer.Expired(Runner);
    }

    private float GetRemainingRoundTime()
    {
        return Mathf.Max(0f, (float)(RoundTimer.RemainingTime(Runner) ?? 0f));
    }

    public override void Spawned()
    {
        _spawner = FindObjectOfType<PlaySceneNetworkSpawner>();
        _teams = TeamAssigner.Instance;
        _status = FindObjectOfType<MatchStatusTracker>();
        _ui = GamePlayUIController.Instance;

        if (Object.HasStateAuthority)
        {
            _spawner.EnsureBombSites(); // 강제 스폰
            _spawner.ResetBombSitesForNewRound(); // 초기화

            StartRoundTimer(RoundTime);
            OnGameStart_ServerOnly();
        }
    }

    // ====== 매치 시작(서버에서 1회) ======
    protected override void OnGameStart_ServerOnly()
    {
        // 첫 라운드: 레드=설치팀, 블루=해제팀
        _teams.RPC_SetRoundRoles(true);
        Server_StartRound(1);
    }

    private static void OnWinnerChanged(Changed<BombModeHandler> changed)
    {
        var w = changed.Behaviour.Winner;
        GamePlayUIController.Instance?.ShowResultUI(w);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnnounceMatchEnd(TeamAssigner.Team winner)
    {
        GamePlayUIController.Instance?.ShowResultUI(winner);
    }

    private IEnumerator ShutdownAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (Runner != null && Runner.IsRunning)
            Runner.Shutdown();
    }

    // NOTE: ModeHandlerBase에 OnNextRound_ServerOnly(int)가 없어서 오버라이드하지 않음.
    // 다음 라운드는 라운드 승 처리 후 내부에서 직접 시작한다.

    // ====== 서버: 라운드 시작 시 초기화 ======
    private void Server_StartRound(int round)
    {
        CurrentRound = round;

        // 상태 리셋
        BombPlanted = false;
        ActiveSiteIndex = -1;

        // 사망 플래그 초기화
        _status.RPC_ResetAllDeathFlags();

        // BOMB 사이트 준비
        _spawner.EnsureBombSites();
        _spawner.ResetBombSitesForNewRound();

        // 라운드 타이머 5분으로 시작
        StartRoundTimer(RoundTime);

        // UI 초기화/라운드 정보 브로드캐스트
        RPC_BroadcastInitUI(TotalRounds, RoundTime);
        RPC_BroadcastRoundUI(CurrentRound, TotalRounds);
    }

    // ====== 네트워크 틱 ======
    // Base는 public이므로 접근지정자 변경 금지
    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        // 타이머 표시는 모든 클라에서 갱신
        if (_ui != null)
            _ui.UpdateTimer(GetRemainingRoundTime());

        if (!Object.HasStateAuthority)
            return;

        // 라운드 타이머 만료 → 설치 여부로 분기
        if (IsRoundTimerExpired())
        {
            if (!BombPlanted)
            {
                // 설치 실패 → 해제팀 승
                Server_RegisterRoundWin(_teams.GetDefuseTeam());
            }
            else
            {
                // 설치됨 + 60초 만료 → 설치팀 승(폭발)
                Server_RegisterRoundWin(_teams.GetInstallerTeam());
            }
            return;
        }

        // 전멸 판정(매 틱 서버에서 확인)
        Server_CheckEliminationWin();
    }

    // ====== 설치/해제 RPC ======
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestPlant(int siteIndex, PlayerRef requester)
    {
        if (BombPlanted || PlantInProgress) return; // ✅ 중복 설치 차단
        if (siteIndex < 0) return;

        if (_teams.GetTeam(requester) != _teams.GetInstallerTeam())
            return;

        var site = _spawner.GetSite(siteIndex);
        if (site == null || !site.IsActive || site.IsPlantedHere)
            return;

        PlantInProgress = true; // ✅ 설치 시작 플래그
        StartCoroutine(Server_PlantRoutine(siteIndex, site, requester));
    }

    private IEnumerator Server_PlantRoutine(int siteIndex, BombSite site, PlayerRef requester)
    {
        // ✅ 설치 진행 시작 알림(네트워크 UI 동기화용)
        site.Server_BeginInstall();

        float t = 0f;
        while (t < plantTime)
        {
            // 취소 조건: 사이트 비활/이미 설치
            if (!site.IsActive || site.IsPlantedHere)
            {
                PlantInProgress = false;
                site.Server_EndInstall();
                UnlockMovement(requester);
                yield break;
            }

            // 요청자 유효/거리 체크
            var ctrl = FindPlayerController(requester);
            if (ctrl == null)
            {
                PlantInProgress = false;
                site.Server_EndInstall();
                yield break;
            }

            if (Vector3.Distance(ctrl.transform.position, site.worldAnchor.position) > site.interactRange)
            {
                PlantInProgress = false;
                site.Server_EndInstall();
                yield break;
            }

            // ✅ 진행도 브로드캐스트(네트워크 변수 갱신)
            t += Runner.DeltaTime;
            site.Server_UpdateInstall(t / plantTime);

            yield return null;
        }

        // ✅ 설치 확정
        BombPlanted = true;
        ActiveSiteIndex = siteIndex;
        PlantInProgress = false;
        site.Server_EndInstall();

        site.RPC_OnPlanted();
        StartRoundTimer(BombCountdown);
        RPC_OnBombPlantedAlert(siteIndex);
    }

    private void UnlockMovement(PlayerRef player)
    {
        var ctrl = FindPlayerController(player);
        if (ctrl != null)
            ctrl.SetMovementLock(false);
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestDefuse(int siteIndex, PlayerRef requester)
    {
        if (!BombPlanted) return;
        if (siteIndex != ActiveSiteIndex) return;

        if (_teams.GetTeam(requester) != _teams.GetDefuseTeam())
            return;

        var site = _spawner.GetSite(siteIndex);
        if (site == null || !site.IsActive || !site.IsPlantedHere)
            return;

        StartCoroutine(Server_DefuseRoutine(siteIndex, site, requester));
    }

    private IEnumerator Server_DefuseRoutine(int siteIndex, BombSite site, PlayerRef requester)
    {
        float t = 0f;
        while (t < defuseTime)
        {
            if (!BombPlanted || siteIndex != ActiveSiteIndex || !site.IsPlantedHere)
                yield break;

            var ctrl = FindPlayerController(requester);
            if (ctrl == null) yield break;

            if (Vector3.Distance(ctrl.transform.position, site.worldAnchor.position) > site.interactRange)
                yield break;

            t += Runner.DeltaTime;
            yield return null;
        }

        // 해제 성공 → 해제팀 즉시 승
        Server_RegisterRoundWin(_teams.GetDefuseTeam());
    }

    // ====== 전멸 판정 ======
    private void Server_CheckEliminationWin()
    {
        var atk = _teams.GetInstallerTeam();
        var def = _teams.GetDefuseTeam();

        int atkAlive = _status.GetAliveCount(atk);
        int defAlive = _status.GetAliveCount(def);

        if (!BombPlanted)
        {
            // 설치 전: 한 팀 전멸 → 생존 팀 승
            if (atkAlive == 0)
            {
                Server_RegisterRoundWin(def);
                return;
            }
            if (defAlive == 0)
            {
                Server_RegisterRoundWin(atk);
                return;
            }
        }
        else
        {
            // 설치 후: 해제팀 전멸 or 양팀 전멸 → 설치팀 즉시 승
            if (defAlive == 0 || (atkAlive == 0 && defAlive == 0))
            {
                Server_RegisterRoundWin(atk);
                return;
            }
            // 설치팀 전멸만으로는 즉시 종료 아님(해제 가능)
        }
    }

    // ====== 라운드 승 처리 & 다음 라운드 / 종료 ======
    private void Server_RegisterRoundWin(TeamAssigner.Team winner)
    {
        // 점수 1 증가(라운드 득점)
        _teams.AddTeamScore(winner, 1);

        // 선승 3 확인
        var scores = _teams.GetScores();
        int red = scores.red;
        int blue = scores.blue;
        int winNeed = (TotalRounds / 2) + 1;

        if (red >= winNeed || blue >= winNeed || CurrentRound >= TotalRounds)
        {
            // 매치 종료
            Server_EndMatch(winner);
        }
        else
        {
            // 다음 라운드
            StartCoroutine(Server_StartNextRoundAfter(2f)); // 2초 대기 후
        }
    }

    private IEnumerator Server_StartNextRoundAfter(float delay)
    {
        yield return new WaitForSeconds(delay);

        // 라운드 역할 교대
        _teams.RPC_SetRoundRoles(false);

        int next = CurrentRound + 1;
        Server_StartRound(next);
    }

    private void Server_EndMatch(TeamAssigner.Team winner)
    {
        if (IsEnded) return;
        IsEnded = true;

        Winner = winner;                 // ✅ 네트워크 동기화(늦게 들어온 클라 포함)
        RPC_AnnounceMatchEnd(winner);    // ✅ 즉시 모든 클라에 결과 패널 표시

        // 선택: 결과 패널 감상 시간 확보 후 세션 종료
        StartCoroutine(ShutdownAfterDelay(6f));
    }

    // ====== UI 브로드캐스트 ======
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastInitUI(int totalRounds, float roundTime)
    {
        GamePlayUIController.Instance?.InitBombUI(totalRounds, roundTime);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BroadcastRoundUI(int current, int total)
    {
        GamePlayUIController.Instance?.UpdateRoundUI(current, total);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnBombPlantedAlert(int siteIndex)
    {
        var site = PlaySceneNetworkSpawner.Instance?.GetSite(siteIndex);
        var target = site != null && site.worldAnchor != null ? site.worldAnchor : site?.transform;

        // 👀 로컬 플레이어가 해제팀(수비)일 때만 알림/화살표 표시
        var runner = Object != null ? Runner : FindObjectOfType<NetworkRunner>();
        var local = runner != null ? runner.LocalPlayer : PlayerRef.None;

        bool show = false;
        if (local.IsValid && TeamAssigner.Instance != null && TeamAssigner.Instance.HasTeam(local))
        {
            var myTeam = TeamAssigner.Instance.GetTeam(local);
            show = (myTeam == TeamAssigner.Instance.GetDefuseTeam());
        }

        if (show)
            GamePlayUIController.Instance?.ShowBombAlert(target, "폭탄이 설치되었습니다.");
    }

    // ====== 매치 종료 처리 (Base 추상 구현) ======
    private TeamAssigner.Team _matchWinner = TeamAssigner.Team.None;

    protected override void EndGame()
    {
        // 안전장치: 누가 호출해도 전 클라에 동일하게 결과 표시
        RPC_AnnounceMatchEnd(Winner);
    }

    // ====== 헬퍼 ======
    private PlayerNetworkController FindPlayerController(PlayerRef player)
    {
        var ctrls = FindObjectsOfType<PlayerNetworkController>();
        foreach (var c in ctrls)
        {
            if (c != null && c.Object != null && c.Object.InputAuthority == player)
                return c;
        }
        return null;
    }
}
