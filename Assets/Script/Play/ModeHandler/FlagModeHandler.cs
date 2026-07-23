using Fusion;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class FlagModeHandler : ModeHandlerBase
{
    public static FlagModeHandler Instance { get; private set; }

    [Header("FLAG 기본 설정")]
    [SerializeField] private int inspectorCaptureGoal = 10;
    [SerializeField] private float inspectorRoundTime = 600f;  // 7분
    [SerializeField] private float inspectorRespawnDelay = 5f;

    [Header("FLAG 보상")]
    [SerializeField] private float resultDisplaySeconds = 6f;       // 결과 패널 보여주는 시간
    [SerializeField] private int[] winGoldByRank = new int[4] { 100, 80, 60, 40 };
    [SerializeField] private int[] loseGoldByRank = new int[4] { 50, 40, 30, 20 };
    [SerializeField] private int[] drawGoldByRank = new int[4] { 60, 50, 40, 30 };
    [SerializeField] private bool distributeRewardsOnEnd = true;
    [Networked] private bool IsEnded { get; set; }

    // 네트워크 동기화 상태
    [Networked] private int CaptureGoal { get; set; } = 3;
    [Networked] private float RoundTime { get; set; } = 420f;
    [Networked] private float RespawnDelayTime { get; set; } = 5f;

    // 팀당 "현재 깃발 캐리어" — PlayerRef.RawEncoded를 저장(-1 = 없음)
    [Networked(OnChanged = nameof(OnRedCarrierChanged))] private int RedFlagCarrierEncoded { get; set; }   // 블루가 레드 깃발을 들면 저장
    [Networked(OnChanged = nameof(OnBlueCarrierChanged))] private int BlueFlagCarrierEncoded { get; set; }  // 레드가 블루 깃발을 들면 저장

    private const int NoCarrier = -1;

    private void Awake()
    {
        Instance = this;
    }

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            // 인스펙터 값 -> 네트워크 상태 세팅
            CaptureGoal = inspectorCaptureGoal;
            RoundTime = inspectorRoundTime;
            RespawnDelayTime = inspectorRespawnDelay;

            TimeLimit = RoundTime;
            RespawnDelay = RespawnDelayTime;

            // 타이머/시작
            StartMatchTimer();
            OnGameStart_ServerOnly();

            // 사망 이벤트 구독: 캐리어 사망 시 즉시 원복
            Health.OnAnyDeath -= HandleAnyDeath;
            Health.OnAnyDeath += HandleAnyDeath;

            RedFlagCarrierEncoded = NoCarrier;
            BlueFlagCarrierEncoded = NoCarrier;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Health.OnAnyDeath -= HandleAnyDeath;
    }

    protected override void OnGameStart_ServerOnly()
    {
        TeamAssigner.Instance.currentScoreRule = TeamAssigner.ScoreRule.CaptureBased;
        RPC_InitFlagUI(TimeLimit, CaptureGoal); // 모든 클라에 UI 초기화
        BroadcastTeamScoreUI();                 // 초기 점수 0/0 브로드캐스트
        UpdateGoalUIToMax();                    // 중앙 목표(진행도) 0/Goal
    }

    // === 사망 처리: 캐리어가 죽으면 깃발 즉시 원복 ===
    private void HandleAnyDeath(PlayerRef victim, NetworkObject victimObj)
    {
        if (!Object.HasStateAuthority) return;

        if (IsRedFlagCarrier(victim))
        {
            ClearRedCarrierAndHideFlag();
        }
        if (IsBlueFlagCarrier(victim))
        {
            ClearBlueCarrierAndHideFlag();
        }

        // FLAG는 라운드 내내 리스폰 허용
        StartCoroutine(RespawnAfterDelay(victim, victimObj));
    }

    private IEnumerator RespawnAfterDelay(PlayerRef victim, NetworkObject victimObj)
    {
        float t = 0f;
        while (t < RespawnDelay)
        {
            t += Time.deltaTime;
            yield return null;
        }

        var team = TeamAssigner.Instance.GetTeam(victim);
        var sp = PlaySceneNetworkSpawner.Instance?.GetSpawnPoint(team);
        if (sp == null || victimObj == null) yield break;

        var hp = victimObj.GetComponent<Health>();
        if (hp != null)
            hp.RPC_RequestRespawn(sp.position, sp.rotation);
    }

    private static void OnRedCarrierChanged(Changed<FlagModeHandler> changed)
    {
        var self = changed.Behaviour;
        // 이전/현재 값을 받아 캐리어 비주얼 갱신
        changed.LoadOld(); int oldVal = self.RedFlagCarrierEncoded;
        changed.LoadNew(); int newVal = self.RedFlagCarrierEncoded;

        // 이전 캐리어에게서 레드깃발 비주얼 OFF
        if (TryGetPlayerByEncoded(oldVal, out var oldP))
            FindPlayerNetworkObject(oldP)?.GetComponent<FlagCarrier>()?.RPC_SetCarry(TeamAssigner.Team.Red, false);

        // 새 캐리어에게 레드깃발 비주얼 ON
        if (TryGetPlayerByEncoded(newVal, out var newP))
            FindPlayerNetworkObject(newP)?.GetComponent<FlagCarrier>()?.RPC_SetCarry(TeamAssigner.Team.Red, true);
    }

    private static void OnBlueCarrierChanged(Changed<FlagModeHandler> changed)
    {
        var self = changed.Behaviour;
        changed.LoadOld(); int oldVal = self.BlueFlagCarrierEncoded;
        changed.LoadNew(); int newVal = self.BlueFlagCarrierEncoded;

        if (TryGetPlayerByEncoded(oldVal, out var oldP))
            FindPlayerNetworkObject(oldP)?.GetComponent<FlagCarrier>()?.RPC_SetCarry(TeamAssigner.Team.Blue, false);

        if (TryGetPlayerByEncoded(newVal, out var newP))
            FindPlayerNetworkObject(newP)?.GetComponent<FlagCarrier>()?.RPC_SetCarry(TeamAssigner.Team.Blue, true);
    }

    // === 서버: 탈취 요청 ===
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPickup(TeamAssigner.Team ownerTeam, PlayerRef requester)
    {
        var reqTeam = TeamAssigner.Instance.GetTeam(requester);
        if (reqTeam == TeamAssigner.Team.None) return;

        if (reqTeam == ownerTeam) return;

        if (ownerTeam == TeamAssigner.Team.Red)
        {
            if (RedFlagCarrierEncoded != -1) return;

            RedFlagCarrierEncoded = requester.RawEncoded;

            var carrierObj = FindPlayerNetworkObject(requester);
            var flag = carrierObj != null ? carrierObj.GetComponent<FlagCarrier>() : null;
            flag?.RPC_SetCarry(TeamAssigner.Team.Red, true);

            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Red, "깃발을 뺏겼습니다!", 2f);
            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Blue, "적 깃발을 탈취했습니다!", 2f);
        }
        else if (ownerTeam == TeamAssigner.Team.Blue)
        {
            if (BlueFlagCarrierEncoded != -1) return;

            BlueFlagCarrierEncoded = requester.RawEncoded;

            var carrierObj = FindPlayerNetworkObject(requester);
            var flag = carrierObj != null ? carrierObj.GetComponent<FlagCarrier>() : null;
            flag?.RPC_SetCarry(TeamAssigner.Team.Blue, true);

            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Blue, "깃발을 뺏겼습니다!", 2f);
            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Red, "적 깃발을 탈취했습니다!", 2f);
        }
    }

    // === 서버: 골인(득점) 요청 ===
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestScore(TeamAssigner.Team goalTeam, PlayerRef requester)
    {
        var reqTeam = TeamAssigner.Instance.GetTeam(requester);
        if (reqTeam != goalTeam) return;

        bool scored = false;

        if (goalTeam == TeamAssigner.Team.Red)
        {
            if (IsBlueFlagCarrier(requester))
            {
                ClearBlueCarrierAndHideFlag();
                TeamAssigner.Instance.AddTeamScore(TeamAssigner.Team.Red, 1);
                scored = true;
            }
        }
        else if (goalTeam == TeamAssigner.Team.Blue)
        {
            if (IsRedFlagCarrier(requester))
            {
                ClearRedCarrierAndHideFlag();
                TeamAssigner.Instance.AddTeamScore(TeamAssigner.Team.Blue, 1);
                scored = true;
            }
        }

        if (!scored) return;

        // ✅ 양 팀에 결과 알림
        if (goalTeam == TeamAssigner.Team.Red)
        {
            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Red, "득점 성공!", 2f);
            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Blue, "상대가 득점했습니다!", 2f);
        }
        else
        {
            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Blue, "득점 성공!", 2f);
            RPC_ShowFlagAlertForTeam(TeamAssigner.Team.Red, "상대가 득점했습니다!", 2f);
        }

        BroadcastTeamScoreUI();
        UpdateGoalUIToMax();

        var (r, b) = TeamAssigner.Instance.GetScores();
        if (r >= CaptureGoal || b >= CaptureGoal)
        {
            EndGame();
        }
    }

    // === 내부 유틸 ===
    private static NetworkObject FindPlayerNetworkObject(PlayerRef player)
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null && runner.TryGetPlayerObject(player, out var obj))
            return obj;
        return null;
    }

    private bool IsRedFlagCarrier(PlayerRef p) => RedFlagCarrierEncoded == p.RawEncoded;
    private bool IsBlueFlagCarrier(PlayerRef p) => BlueFlagCarrierEncoded == p.RawEncoded;

    private static bool TryGetPlayerByEncoded(int encoded, out PlayerRef player)
    {
        var runner = FindObjectOfType<NetworkRunner>();
        if (runner != null)
        {
            foreach (var p in runner.ActivePlayers)
            {
                if (p.RawEncoded == encoded) { player = p; return true; }
            }
        }
        player = PlayerRef.None;
        return false;
    }

    private void ClearRedCarrierAndHideFlag()
    {
        if (RedFlagCarrierEncoded == NoCarrier) return;
        if (TryGetPlayerByEncoded(RedFlagCarrierEncoded, out var player))
        {
            var obj = FindPlayerNetworkObject(player);
            obj?.GetComponent<FlagCarrier>()?.RPC_SetCarry(TeamAssigner.Team.Red, false);
        }
        RedFlagCarrierEncoded = NoCarrier;
    }

    private void ClearBlueCarrierAndHideFlag()
    {
        if (BlueFlagCarrierEncoded == NoCarrier) return;
        if (TryGetPlayerByEncoded(BlueFlagCarrierEncoded, out var player))
        {
            var obj = FindPlayerNetworkObject(player);
            obj?.GetComponent<FlagCarrier>()?.RPC_SetCarry(TeamAssigner.Team.Blue, false);
        }
        BlueFlagCarrierEncoded = NoCarrier;
    }

    private void BroadcastTeamScoreUI()
    {
        int red = TeamAssigner.Instance.GetTeamScore(TeamAssigner.Team.Red);
        int blue = TeamAssigner.Instance.GetTeamScore(TeamAssigner.Team.Blue);
        RPC_UpdateTeamScoreUI_All(red, blue);
    }

    private void UpdateGoalUIToMax()
    {
        int red = TeamAssigner.Instance.GetTeamScore(TeamAssigner.Team.Red);
        int blue = TeamAssigner.Instance.GetTeamScore(TeamAssigner.Team.Blue);
        int max = Mathf.Max(red, blue);
        RPC_UpdateGoalUI_All(max, CaptureGoal);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_UpdateTeamScoreUI_All(int red, int blue)
    {
        GamePlayUIController.Instance?.UpdateTeamScoreUI(red, blue);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_UpdateGoalUI_All(int current, int goal)
    {
        GamePlayUIController.Instance?.UpdateGoalUI(current, goal);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ShowFlagAlertForTeam(TeamAssigner.Team target, string message, float seconds)
    {
        GamePlayUIController.Instance?.ShowFlagAlertForTeam(target, message, seconds);
    }

    private void DistributePersonalRewards(TeamAssigner.Team winner)
    {
        if (TeamAssigner.Instance == null) return;

        var redList = RankTeamPlayers(TeamAssigner.Team.Red);
        var blueList = RankTeamPlayers(TeamAssigner.Team.Blue);

        SendRewardRPC(redList, TeamAssigner.Team.Red, winner);
        SendRewardRPC(blueList, TeamAssigner.Team.Blue, winner);
    }

    private List<PlayerRef> RankTeamPlayers(TeamAssigner.Team team)
    {
        var active = new HashSet<PlayerRef>(Runner.ActivePlayers);
        var list = new List<(PlayerRef player, int k, int d)>();

        foreach (var kv in TeamAssigner.Instance.Teams)
        {
            if (kv.Value != team) continue;
            var p = kv.Key;
            if (!active.Contains(p)) continue;

            int k = 0, d = 0;
            MatchStatusTracker.Instance?.TryGetKill(p, out k);
            MatchStatusTracker.Instance?.TryGetDeath(p, out d);
            list.Add((p, k, d));
        }

        return list
            .OrderByDescending(x => x.k)
            .ThenBy(x => x.d)
            .ThenBy(x => x.player.RawEncoded)
            .Select(x => x.player)
            .ToList();
    }

    private void SendRewardRPC(List<PlayerRef> ranked, TeamAssigner.Team thisTeam, TeamAssigner.Team winner)
    {
        var kdList = new List<(PlayerRef p, int k, int d)>(ranked.Count);
        foreach (var p in ranked)
        {
            int k = 0, d = 0;
            MatchStatusTracker.Instance?.TryGetKill(p, out k);
            MatchStatusTracker.Instance?.TryGetDeath(p, out d);
            kdList.Add((p, k, d));
        }

        int rankSlot = -1;
        int prevK = int.MinValue;
        int prevD = int.MaxValue;

        for (int i = 0; i < kdList.Count; i++)
        {
            var cur = kdList[i];
            if (i == 0)
                rankSlot = 0;
            else if (!(cur.k == prevK && cur.d == prevD))
                rankSlot++;

            prevK = cur.k;
            prevD = cur.d;

            int gold = 0;
            if (rankSlot < 4)
            {
                int[] table =
                    (winner == TeamAssigner.Team.None) ? drawGoldByRank :
                    (winner == thisTeam) ? winGoldByRank :
                                           loseGoldByRank;

                gold = (rankSlot >= 0 && rankSlot < table.Length) ? table[rankSlot] : 0;
            }

            RPC_SendPersonalReward(cur.p, gold);
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SendPersonalReward(PlayerRef target, int gold)
    {
        if (target == Runner.LocalPlayer)
        {
            GamePlayUIController.Instance?.ShowPersonalReward(gold);
        }
    }

    private IEnumerator ShutdownAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (Runner != null && Runner.IsRunning)
            Runner.Shutdown();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnnounceMatchEnd(TeamAssigner.Team winner)
    {
        GamePlayUIController.Instance?.ShowResultUI(winner);
    }

    protected override void EndGame()
    {
        if (IsEnded) return;   // ✅ 중복 종료 방어
        IsEnded = true;

        var (red, blue) = TeamAssigner.Instance.GetScores();

        // 승자 판정: 점수 비교 → 같으면 무승부
        TeamAssigner.Team winner =
            red > blue ? TeamAssigner.Team.Red :
            blue > red ? TeamAssigner.Team.Blue :
            TeamAssigner.Team.None;

        // 1) 모든 클라에 결과 패널 열기
        RPC_AnnounceMatchEnd(winner);

        // 2) (선택) 개인 보상 계산/알림
        if (distributeRewardsOnEnd)
            DistributePersonalRewards(winner);

        // 3) 세션 종료는 잠시 딜레이(결과 패널 표시 시간 확보)
        StartCoroutine(ShutdownAfterDelay(resultDisplaySeconds));
    }
}
