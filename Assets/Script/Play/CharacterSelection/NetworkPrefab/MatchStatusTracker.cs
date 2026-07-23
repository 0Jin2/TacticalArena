using Fusion;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

public class MatchStatusTracker : NetworkBehaviour
{
    public static MatchStatusTracker Instance;

    [Networked, Capacity(100)] private NetworkDictionary<PlayerRef, int> _kills => default;
    [Networked, Capacity(100)] private NetworkDictionary<PlayerRef, int> _deaths => default;
    [Networked, Capacity(100)] private NetworkDictionary<PlayerRef, bool> _deathFlags => default;

    public override void Spawned()
    {
        if (Instance == null)
            Instance = this;
    }

    public bool TryGetKill(PlayerRef player, out int value)
    {
        return _kills.TryGet(player, out value);
    }

    public bool TryGetDeath(PlayerRef player, out int value)
    {
        return _deaths.TryGet(player, out value);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RegisterKill(PlayerRef killer, PlayerRef victim)
    {
        if (_kills.ContainsKey(killer))
            _kills.Set(killer, _kills[killer] + 1);
        else
            _kills.Add(killer, 1);

        if (TeamAssigner.Instance.currentScoreRule == TeamAssigner.ScoreRule.KillBased)
        {
            var team = TeamAssigner.Instance.GetTeam(killer);
            TeamAssigner.Instance.AddTeamScore(team, 1);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RegisterDeath(PlayerRef victim)
    {
        if (_deathFlags.TryGet(victim, out var isAlreadyDead) && isAlreadyDead)
            return;

        _deathFlags.Set(victim, true);

        if (_deaths.ContainsKey(victim))
            _deaths.Set(victim, _deaths[victim] + 1);
        else
            _deaths.Add(victim, 1);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ClearDeathFlag(PlayerRef victim)
    {
        // 리스폰 시 다시 사망 집계가 되도록 플래그 해제
        if (_deathFlags.ContainsKey(victim))
            _deathFlags.Set(victim, false);
        else
            _deathFlags.Add(victim, false);
    }

    /// <summary>팀별 생존자 수 계산</summary>
    public int GetAliveCount(TeamAssigner.Team team)
    {
        int alive = 0;
        foreach (var kv in TeamAssigner.Instance.Teams)
        {
            if (kv.Value != team) continue;
            var player = kv.Key;
            if (_deathFlags.TryGet(player, out var isDead) && !isDead)
                alive++;
            else if (!_deathFlags.ContainsKey(player))
                alive++; // 플래그 없으면 생존으로 간주
        }
        return alive;
    }

    /// <summary>해당 팀 전원이 사망했는지</summary>
    public bool IsTeamEliminated(TeamAssigner.Team team)
    {
        return GetAliveCount(team) == 0;
    }

    /// <summary>라운드 시작 시 전체 사망 플래그 초기화</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_ResetAllDeathFlags()
    {
        // ⚠️ NetworkDictionary에는 Keys 프로퍼티가 없으므로 Enumerator로 키 수집
        var keys = new List<PlayerRef>();
        var it = _deathFlags.GetEnumerator();
        while (it.MoveNext())
        {
            keys.Add(it.Current.Key);
        }

        foreach (var k in keys)
        {
            _deathFlags.Set(k, false);
        }
    }
}