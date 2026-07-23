using System;
using Fusion;
using UnityEngine;
using System.Linq;

public class TeamAssigner : NetworkBehaviour
{
    public static TeamAssigner Instance { get; private set; }

    [Networked, Capacity(10)] public NetworkDictionary<PlayerRef, Team> Teams { get; }
    [Networked, Capacity(10)] public NetworkDictionary<PlayerRef, string> Nicknames { get; }
    [Networked(OnChanged = nameof(OnScoreChanged))] public int redTeamScore { get; set; }
    [Networked(OnChanged = nameof(OnScoreChanged))] public int blueTeamScore { get; set; }

    public event Action<PlayerRef> OnPlayerTeamAssigned;
    public event Action<PlayerRef, string> OnNicknameUpdated;

    public enum ScoreRule { KillBased, CaptureBased, RoundBased }
    public ScoreRule currentScoreRule;

    [Networked] public Team AttackerTeam { get; private set; } = Team.Red; // 설치팀
    [Networked] public Team DefenderTeam { get; private set; } = Team.Blue; // 해제팀

    public int GetTeamScore(Team team)
        => team == Team.Red? redTeamScore :
           team == Team.Blue? blueTeamScore : 0;

    public (int red, int blue) GetScores()
        => (redTeamScore, blueTeamScore);

    public override void Spawned()
    {
        // ✅ 모든 클라이언트에서 Instance 등록
        Instance = this;

        // ✅ 팀 배정은 StateAuthority만 수행
        if (!Object.HasStateAuthority)
            return;

        int index = 0;
        foreach (var player in Runner.ActivePlayers)
        {
            if (!Teams.ContainsKey(player))
            {
                Team team = (index % 2 == 0) ? Team.Red : Team.Blue;
                Teams.Add(player, team);
                OnPlayerTeamAssigned?.Invoke(player);
                index++;
            }
        }
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void Rpc_SubmitNickname(PlayerRef player, string nickname)
    {
        Debug.Log($"🛰 닉네임 서버 제출: {player} → {nickname}");
        RegisterNickname(player, nickname);
    }

    // 닉네임 등록은 권한 있는 인스턴스에서만
    public void RegisterNickname(PlayerRef player, string nickname)
    {
        if (!Object.HasStateAuthority) return;
        if (Nicknames.ContainsKey(player)) Nicknames.Set(player, nickname);
        else Nicknames.Add(player, nickname);
        OnNicknameUpdated?.Invoke(player, nickname);
    }

    public bool HasTeam(PlayerRef player)
    {
        return Teams.ContainsKey(player);
    }

    public Team GetTeam(PlayerRef player)
    {
        return Teams[player];
    }

    public void AddTeamScore(Team team, int amount)
    {
        if (team == Team.Red) redTeamScore += amount;
        else if (team == Team.Blue) blueTeamScore += amount;
    }

      // 점수 변경 시 모든 클라이언트에서 UI를 갱신
    static void OnScoreChanged(Changed<TeamAssigner> changed)
    {
        var b = changed.Behaviour;
        GamePlayUIController.Instance?.UpdateTeamScoreUI(b.redTeamScore, b.blueTeamScore);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SetRoundRoles(bool firstRound)
    {
        if (firstRound)
        {
            AttackerTeam = Team.Red;
            DefenderTeam = Team.Blue;
        }
        else
        {
            // 단순 스왑
            var prevAtk = AttackerTeam;
            AttackerTeam = DefenderTeam;
            DefenderTeam = prevAtk;
        }
    }

    public Team GetInstallerTeam() => AttackerTeam; // 명시적 별칭
    public Team GetDefuseTeam() => DefenderTeam;

    public enum Team
    {
        Red = 0,
        Blue = 1,
        None = 2,
        Attacker = Red,
        Defender = Blue
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}