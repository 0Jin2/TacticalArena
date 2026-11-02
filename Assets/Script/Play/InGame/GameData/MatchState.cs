using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MatchState : 한 판의 "기록판" - 팀 점수, 유저별 킬/데스/생존
//
//  - 모든 기기가 같은 패킷을 받아 같은 값을 만든다. (주인이 쏜 PlayerDied / 호스트가 쏜 ScoreAdd)
//  - 승패 "판단"은 하지 않는다. (→ GameModeBase 파생 클래스)
//  - 재접속 시 호스트의 스냅샷으로 복구된다. (StateId = MatchState)
//  배치: GameRoot 오브젝트
// ─────────────────────────────────────────────────────────────────────────────

public class MatchState : MonoBehaviour, INetStateProvider
{
    public static MatchState Instance { get; private set; }

    public enum ScoreRule : byte { KillBased = 0, Manual = 1 }   // KillBased: 킬 1회 = 팀 1점 / Manual: 모드가 호스트로 AddScore

    public sealed class PlayerStat { public int Kills; public int Deaths; public bool Dead; }

    [SerializeField] private ScoreRule scoreRule = ScoreRule.KillBased;

    public ScoreRule Rule { get { return scoreRule; } }
    public int RedScore { get; private set; }
    public int BlueScore { get; private set; }

    public event Action<int, int> OnScoreChanged;        // (red, blue)
    public event Action<uint> OnStatChanged;              // 킬/데스/생존이 바뀐 유저
    public event Action<uint, uint> OnPlayerDied;         // (피해자, 가해자) 통계 반영 후
    public event Action<uint> OnPlayerRevived;            // 부활한 유저

    private readonly Dictionary<uint, PlayerStat> stats = new Dictionary<uint, PlayerStat>();
    private InGameNet net;

    public ushort StateId { get { return NetStateIds.MatchState; } }

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        net = InGameNet.Ensure();
        net.Subscribe(NetPacketType.PlayerDied, HandlePlayerDied);
        net.Subscribe(NetPacketType.PlayerSpawn, HandlePlayerSpawn);
        net.Subscribe(NetPacketType.ScoreAdd, HandleScoreAdd);
        net.RegisterStateProvider(this);
    }

    private void OnDisable()
    {
        if (net == null) return;
        net.Unsubscribe(NetPacketType.PlayerDied, HandlePlayerDied);
        net.Unsubscribe(NetPacketType.PlayerSpawn, HandlePlayerSpawn);
        net.Unsubscribe(NetPacketType.ScoreAdd, HandleScoreAdd);
        net.UnregisterStateProvider(this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 조회

    public void SetScoreRule(ScoreRule rule) { scoreRule = rule; }

    public PlayerStat GetStat(uint session)
    {
        PlayerStat s;
        if (!stats.TryGetValue(session, out s)) { s = new PlayerStat(); stats[session] = s; }
        return s;
    }

    public int GetKills(uint session) { return GetStat(session).Kills; }
    public int GetDeaths(uint session) { return GetStat(session).Deaths; }
    public bool IsDead(uint session) { return GetStat(session).Dead; }

    public int GetScore(NetTeam team)
    {
        return team == NetTeam.Red ? RedScore : team == NetTeam.Blue ? BlueScore : 0;
    }

    /// <summary>
    /// 팀 생존자 수 (엘리미네이션 판정용). 영구 이탈(IsForfeited)한 유저는 뺀다.
    /// 잠깐 끊긴 유저(재접속 대기)는 기본적으로 "생존"으로 센다. 끊김 한 번에 라운드를 지는 일을 막기 위해서다.
    /// </summary>
    public int AliveCount(NetTeam team, bool countOfflineAsAlive = true)
    {
        int alive = 0;
        foreach (var p in net.PlayersOf(team))
        {
            if (p.IsForfeited) continue;
            if (!p.IsOnline && !countOfflineAsAlive) continue;
            if (!GetStat(p.SessionId).Dead) alive++;
        }
        return alive;
    }

    /// <summary>팀 내 순위: 킬 많은 순 → 데스 적은 순 → 세션 번호 (이탈자 제외)</summary>
    public List<NetPlayer> Ranked(NetTeam team)
    {
        var list = new List<NetPlayer>();
        foreach (var p in net.PlayersOf(team))
            if (!p.IsForfeited) list.Add(p);

        list.Sort((a, b) =>
        {
            var sa = GetStat(a.SessionId);
            var sb = GetStat(b.SessionId);
            int c = sb.Kills.CompareTo(sa.Kills);
            if (c != 0) return c;
            c = sa.Deaths.CompareTo(sb.Deaths);
            return c != 0 ? c : a.SessionId.CompareTo(b.SessionId);
        });
        return list;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 변경

    /// <summary>[호스트만] 팀 점수를 올린다. 모든 기기에 ScoreAdd가 전달되어 같이 반영된다.</summary>
    public void HostAddScore(NetTeam team, int amount)
    {
        if (!net.IsHost || team == NetTeam.None) return;
        net.Send(NetPacketType.ScoreAdd, w => { w.WriteByte((byte)team); w.WriteInt(amount); }, true);
    }

    /// <summary>모든 유저를 생존으로 되돌린다. (BOMB 라운드 시작) 킬/데스 누적은 유지.</summary>
    public void ResetAlive()
    {
        foreach (var kv in stats) kv.Value.Dead = false;
        foreach (var p in net.Players) OnStatChanged?.Invoke(p.SessionId);
    }

    /// <summary>팀 점수를 0으로 (새 매치 준비용)</summary>
    public void ResetScores()
    {
        RedScore = 0;
        BlueScore = 0;
        OnScoreChanged?.Invoke(0, 0);
    }

    private void AddScoreLocal(NetTeam team, int amount)
    {
        if (team == NetTeam.Red) RedScore += amount;
        else if (team == NetTeam.Blue) BlueScore += amount;
        else return;
        OnScoreChanged?.Invoke(RedScore, BlueScore);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 패킷

    // PlayerDied: [피해자 uint][가해자 uint]
    private void HandlePlayerDied(NetPacket packet)
    {
        var r = packet.OpenReader();
        uint victim = r.ReadUInt();
        uint killer = r.ReadUInt();

        var victimStat = GetStat(victim);
        if (victimStat.Dead) return;                        // 중복 방지
        victimStat.Dead = true;
        victimStat.Deaths++;

        if (killer != 0 && killer != victim)
        {
            GetStat(killer).Kills++;
            if (scoreRule == ScoreRule.KillBased)
            {
                var killerInfo = net.GetPlayer(killer);
                if (killerInfo != null) AddScoreLocal(killerInfo.Team, 1);
            }
            OnStatChanged?.Invoke(killer);
        }

        OnStatChanged?.Invoke(victim);
        OnPlayerDied?.Invoke(victim, killer);
    }

    // PlayerSpawn: 보낸 유저가 (재)스폰했다
    private void HandlePlayerSpawn(NetPacket packet)
    {
        var s = GetStat(packet.Sender);
        if (!s.Dead) return;
        s.Dead = false;
        OnStatChanged?.Invoke(packet.Sender);
        OnPlayerRevived?.Invoke(packet.Sender);
    }

    // ScoreAdd: [팀 byte][점수 int]
    private void HandleScoreAdd(NetPacket packet)
    {
        var r = packet.OpenReader();
        var team = (NetTeam)r.ReadByte();
        int amount = r.ReadInt();
        AddScoreLocal(team, amount);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재접속 복구 (INetStateProvider)

    // v1: [버전][규칙][빨강][파랑][인원수]{[세션][킬][데스][사망]}
    public void WriteState(NetWriter writer)
    {
        writer.WriteByte(1);
        writer.WriteByte((byte)scoreRule);
        writer.WriteInt(RedScore);
        writer.WriteInt(BlueScore);
        writer.WriteUShort((ushort)stats.Count);
        foreach (var kv in stats)
        {
            writer.WriteUInt(kv.Key);
            writer.WriteInt(kv.Value.Kills);
            writer.WriteInt(kv.Value.Deaths);
            writer.WriteBool(kv.Value.Dead);
        }
    }

    public void ReadState(NetReader reader)
    {
        byte version = reader.ReadByte();
        if (version != 1) { NetLog.Warn("MatchState: 알 수 없는 스냅샷 버전 " + version); return; }

        scoreRule = (ScoreRule)reader.ReadByte();
        RedScore = reader.ReadInt();
        BlueScore = reader.ReadInt();
        int count = reader.ReadUShort();

        stats.Clear();
        for (int i = 0; i < count; i++)
        {
            uint session = reader.ReadUInt();
            var s = GetStat(session);
            s.Kills = reader.ReadInt();
            s.Deaths = reader.ReadInt();
            s.Dead = reader.ReadBool();
        }

        OnScoreChanged?.Invoke(RedScore, BlueScore);
        foreach (var kv in stats) OnStatChanged?.Invoke(kv.Key);
    }

    #endregion
}
