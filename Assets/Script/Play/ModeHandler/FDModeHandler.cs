using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

public class FDModeHandler : ModeHandlerBase
{
    [Header("FD 기본 설정 (Inspector)")]
    [SerializeField] private int inspectorScoreGoal = 20;
    [SerializeField] private float inspectorRoundTime = 600f;
    [SerializeField] private float inspectorRespawnDelay = 5f;
    [SerializeField] private float resultDisplaySeconds = 6f;

    [Header("골드 보상 (각 팀 1~4위)")]
    [SerializeField] private int[] winGoldByRank = new int[4] { 100, 80, 60, 40 };
    [SerializeField] private int[] loseGoldByRank = new int[4] { 50, 40, 30, 20 };
    [SerializeField] private int[] drawGoldByRank = new int[4] { 60, 50, 40, 30 };
    [SerializeField] private bool distributeRewardsOnEnd = true;

    [Networked] private int ScoreGoal { get; set; } = 20;
    [Networked] private float RoundTime { get; set; } = 600f;
    [Networked] private float RespawnDelayTime { get; set; } = 5f;
    [Networked] private bool IsEnded { get; set; }
    [Networked(OnChanged = nameof(OnWinnerChanged))]
    private TeamAssigner.Team Winner { get; set; }

    private int _lastRedScore = -1;
    private int _lastBlueScore = -1;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            ScoreGoal = inspectorScoreGoal;
            RoundTime = inspectorRoundTime;
            RespawnDelayTime = inspectorRespawnDelay;

            TimeLimit = RoundTime;
            RespawnDelay = RespawnDelayTime;

            StartMatchTimer();
            OnGameStart_ServerOnly();

            Health.OnAnyDeath -= HandleAnyDeath;
            Health.OnAnyDeath += HandleAnyDeath;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Health.OnAnyDeath -= HandleAnyDeath;
    }

    private void OnDestroy()
    {
        Health.OnAnyDeath -= HandleAnyDeath;
    }

    protected override void OnGameStart_ServerOnly()
    {
        TeamAssigner.Instance.currentScoreRule = TeamAssigner.ScoreRule.KillBased;
        RPC_InitTDMUI(TimeLimit, ScoreGoal);

        // 🛑 FD 모드에서는 스킬 버튼 비활성화
        Invoke(nameof(DisableSkillButtonUI), 0.2f);
    }

    private void DisableSkillButtonUI()
    {
        if (GamePlayUIController.Instance != null)
        {
            GamePlayUIController.Instance.skillButton?.SetActive(false);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || IsEnded)
        {
            base.FixedUpdateNetwork();
            return;
        }

        var (red, blue) = TeamAssigner.Instance.GetScores();
        bool redReached = red >= ScoreGoal;
        bool blueReached = blue >= ScoreGoal;

        if (_lastRedScore < 0 && _lastBlueScore < 0)
        {
            _lastRedScore = red;
            _lastBlueScore = blue;
        }

        if (!IsEnded && (redReached || blueReached))
        {
            TeamAssigner.Team winner;
            if (red != blue)
                winner = (red > blue) ? TeamAssigner.Team.Red : TeamAssigner.Team.Blue;
            else
                winner = TeamAssigner.Team.None;

            if (winner != TeamAssigner.Team.None)
                EndGameWithWinner(winner);
        }

        _lastRedScore = red;
        _lastBlueScore = blue;

        base.FixedUpdateNetwork();
    }

    private void HandleAnyDeath(PlayerRef victim, NetworkObject victimObj)
    {
        if (!Object.HasStateAuthority) return;
        if (victimObj == null) return;
        StartCoroutine(RespawnAfterDelay(victim, victimObj));
    }

    private IEnumerator RespawnAfterDelay(PlayerRef victim, NetworkObject victimObj)
    {
        yield return new WaitForSeconds(RespawnDelay);

        if (victimObj == null) yield break;

        var team = TeamAssigner.Instance.GetTeam(victim);
        var sp = PlaySceneNetworkSpawner.Instance?.GetSpawnPoint(team);
        if (sp == null) yield break;

        var hp = victimObj.GetComponent<Health>();
        if (hp != null)
        {
            hp.RPC_RequestRespawn(sp.position, sp.rotation);
        }
    }

    protected override void EndGame()
    {
        if (IsEnded) return;
        IsEnded = true;

        var (red, blue) = TeamAssigner.Instance.GetScores();
        TeamAssigner.Team winner =
            red > blue ? TeamAssigner.Team.Red :
            blue > red ? TeamAssigner.Team.Blue :
            TeamAssigner.Team.None;

        RPC_AnnounceMatchEnd(winner);

        if (distributeRewardsOnEnd)
            DistributePersonalRewards(winner);

        StartCoroutine(ShutdownAfterDelay(resultDisplaySeconds));
    }

    private void EndGameWithWinner(TeamAssigner.Team explicitWinner)
    {
        if (IsEnded) return;
        IsEnded = true;

        RPC_AnnounceMatchEnd(explicitWinner);
        if (distributeRewardsOnEnd)
            DistributePersonalRewards(explicitWinner);

        StartCoroutine(ShutdownAfterDelay(resultDisplaySeconds));
    }

    private IEnumerator ShutdownAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (Runner != null && Runner.IsRunning)
            Runner.Shutdown();
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
                    (winner == thisTeam) ? winGoldByRank : loseGoldByRank;

                gold = (rankSlot >= 0 && rankSlot < table.Length) ? table[rankSlot] : 0;
            }

            Debug.Log($"[FD 보상] Team={thisTeam}, RankSlot={rankSlot + 1}, K={cur.k}, D={cur.d}, Gold={gold}, Player={cur.p}");
            RPC_SendPersonalReward(cur.p, gold);
        }
    }

    private static void OnWinnerChanged(Changed<FDModeHandler> changed)
    {
        changed.LoadNew();
        var w = changed.Behaviour.Winner;
        GamePlayUIController.Instance?.ShowResultUI(w);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnnounceMatchEnd(TeamAssigner.Team winner)
    {
        GamePlayUIController.Instance?.ShowResultUI(winner);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SendPersonalReward(PlayerRef target, int gold)
    {
        if (target == Runner.LocalPlayer)
        {
            Debug.Log($"✅ [FD 보상 수신] Gold={gold}");
            GamePlayUIController.Instance?.ShowPersonalReward(gold);
        }
    }
}
