using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using UnityEngine;

public class TDMModeHandler : ModeHandlerBase
{
    [Header("TDM 기본 설정 (Inspector)")]
    [SerializeField] private int inspectorScoreGoal = 20;          // 목표 킬 (기본 20)
    [SerializeField] private float inspectorRoundTime = 600f;       // 제한시간 (초)
    [SerializeField] private float inspectorRespawnDelay = 5f;      // 리스폰 딜레이
    [SerializeField] private float resultDisplaySeconds = 6f;       // 결과 패널 보여주는 시간(선택)

    [Header("골드 보상 (각 팀 1~4위)")]
    [SerializeField] private int[] winGoldByRank = new int[4] { 100, 80, 60, 40 };
    [SerializeField] private int[] loseGoldByRank = new int[4] { 50, 40, 30, 20 };
    [SerializeField] private int[] drawGoldByRank = new int[4] { 60, 50, 40, 30 };
    [SerializeField] private bool distributeRewardsOnEnd = true;    // true면 종료 시 개인 보상 RPC 통지

    // 네트워크로 동기화되는 경기 설정/상태
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
        // ✅ hasState 여부와 무관하게 항상 해제 (이전 매치 구독자 누수 차단)
        Health.OnAnyDeath -= HandleAnyDeath;
    }

    private void OnDestroy()
    {
        // ✅ 씬 전환/Runner 종료 등 예외 케이스 대비 이중 안전망
        Health.OnAnyDeath -= HandleAnyDeath;
    }

    protected override void OnGameStart_ServerOnly()
    {
        TeamAssigner.Instance.currentScoreRule = TeamAssigner.ScoreRule.KillBased;
        RPC_InitTDMUI(TimeLimit, ScoreGoal);
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority || IsEnded)
        {
            // UI 갱신은 부모에서 처리하므로 계속 호출
            base.FixedUpdateNetwork();
            return;
        }

        // 1) 목표 킬 우선 판정 (타임아웃보다 먼저!)
        var (red, blue) = TeamAssigner.Instance.GetScores();
        bool redReached = red >= ScoreGoal;
        bool blueReached = blue >= ScoreGoal;

        // 직전 프레임 대비 "누가 막 골선을 넘었는지" 판정 보강용(없으면 단순 현재 우세팀으로)
        // 안전하게 정적 필드/지역 캐시 사용
        if (_lastRedScore < 0 && _lastBlueScore < 0)
        {
            _lastRedScore = red;
            _lastBlueScore = blue;
        }

        if (!IsEnded && (redReached || blueReached))
        {
            TeamAssigner.Team winner;
            if (red != blue)
            {
                winner = (red > blue) ? TeamAssigner.Team.Red : TeamAssigner.Team.Blue;
            }
            else
            {
                // 동시 달성(점수도 동일) 드문 케이스 → "누가 이번 틱에 막 골선을 넘었는가?"로 판정
                bool redJustCrossed = (_lastRedScore < ScoreGoal) && redReached;
                bool blueJustCrossed = (_lastBlueScore < ScoreGoal) && blueReached;

                if (redJustCrossed && !blueJustCrossed) winner = TeamAssigner.Team.Red;
                else if (blueJustCrossed && !redJustCrossed) winner = TeamAssigner.Team.Blue;
                else
                {
                    // 그래도 판정 불가하면 현재 틱은 우승 보류 → 다음 틱에서 비교되도록 진행
                    // (희박하므로 안전장치로 두고, 대부분 위의 분기에서 끝납니다)
                    winner = TeamAssigner.Team.None;
                }
            }

            if (winner != TeamAssigner.Team.None)
            {
                EndGameWithWinner(winner); // 승/패 확정 → 보상 테이블이 loseGoldByRank로 정상 분기됨
                                           // 이후 base 호출로 타임아웃 체크가 와도 IsEnded=true라서 중복 종료 불가
            }
        }

        _lastRedScore = red;
        _lastBlueScore = blue;

        // 2) 타이머/UI 갱신 및 만료 판정(부모) — 이미 IsEnded면 부모의 EndGame은 재호출 안 됨
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

        // 승자 판정:
        // 1) 목표 킬 도달 시 더 먼저/더 큰 쪽 승리
        // 2) 시간 종료 시 다득점 승리
        // 3) 동점이면 무승부
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

    // 명시적 승자 종료 루틴 (기존 EndGame() 재사용하면서 승자 주입)
    private void EndGameWithWinner(TeamAssigner.Team explicitWinner)
    {
        if (IsEnded) return;
        IsEnded = true;

        // 결과 브로드캐스트 & 보상
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
        // NetworkDictionary 자체는 null 비교 불가 → 인스턴스만 확인
        if (TeamAssigner.Instance == null) return;

        // 팀별 K/D 랭킹 리스트 산출
        var redList = RankTeamPlayers(TeamAssigner.Team.Red);
        var blueList = RankTeamPlayers(TeamAssigner.Team.Blue);

        // 각 플레이어에게 본인 보상만 전달
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
            if (!active.Contains(p)) continue; // ✅ 액티브만 반영

            int k = 0, d = 0;
            MatchStatusTracker.Instance?.TryGetKill(p, out k);
            MatchStatusTracker.Instance?.TryGetDeath(p, out d);
            list.Add((p, k, d));
        }

        // Kill ↓, Death ↑, PlayerRef.RawEncoded ↑
        return list
            .OrderByDescending(x => x.k)
            .ThenBy(x => x.d)
            .ThenBy(x => x.player.RawEncoded)
            .Select(x => x.player)
            .ToList();
    }

    private void SendRewardRPC(List<PlayerRef> ranked, TeamAssigner.Team thisTeam, TeamAssigner.Team winner)
    {
        // ranked는 이미 "Kill ↓, Death ↑, RawEncoded ↑"로 정렬된 상태
        // 동순위(동일 K/D)는 동일 랭크 슬롯을 공유하도록 처리한다.
        var kdList = new List<(PlayerRef p, int k, int d)>(ranked.Count);
        foreach (var p in ranked)
        {
            int k = 0, d = 0;
            MatchStatusTracker.Instance?.TryGetKill(p, out k);
            MatchStatusTracker.Instance?.TryGetDeath(p, out d);
            kdList.Add((p, k, d));
        }

        int rankSlot = -1;           // 0-based (0=1위 구간)
        int prevK = int.MinValue;
        int prevD = int.MaxValue;

        for (int i = 0; i < kdList.Count; i++)
        {
            var cur = kdList[i];
            if (i == 0)
            {
                rankSlot = 0;
            }
            else
            {
                // K/D가 완전히 같으면 공동순위 → rankSlot 유지
                // 하나라도 다르면 다음 순위 슬롯으로 증가
                if (!(cur.k == prevK && cur.d == prevD))
                    rankSlot++;
            }

            prevK = cur.k;
            prevD = cur.d;

            int gold = 0;
            if (rankSlot < 4) // 최대 4구간까지만 지급
            {
                int[] table =
                    (winner == TeamAssigner.Team.None) ? drawGoldByRank :
                    (winner == thisTeam) ? winGoldByRank :
                                                          loseGoldByRank; // 1~4위 보상표

                gold = (rankSlot >= 0 && rankSlot < table.Length) ? table[rankSlot] : 0;
            }

            Debug.Log($"[보상] Team={thisTeam}, RankSlot={rankSlot + 1}, K={cur.k}, D={cur.d}, Gold={gold}, Player={cur.p}");
            RPC_SendPersonalReward(cur.p, gold);
        }
    }

    private int SafeByRank(int[] arr, int rankIndex)
    {
        if (arr == null || arr.Length == 0) return 0;
        if (rankIndex < 0) return arr[0];
        if (rankIndex >= arr.Length) return arr[^1];
        return arr[rankIndex];
    }

    // 경기 종료 시 호출하는 내부 메서드(예: EndGame 흐름 안)
    private void FinishMatch(TeamAssigner.Team winner)
    {
        if (!Object.HasStateAuthority) return;

        // 1) RPC로 즉시 알림 (기존)
        RPC_AnnounceMatchEnd(winner);

        // 2) 상태 복제도 함께 (늦게 붙은 클라/타이밍 이슈 대비)
        Winner = winner;

        // 보상 송신 등은 기존대로...
    }

    // ✅ OnChanged: 모든 클라에서 UI 강제 동기화
    private static void OnWinnerChanged(Changed<TDMModeHandler> changed)
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
            Debug.Log($"✅ [보상 수신] Gold={gold}");
            GamePlayUIController.Instance?.ShowPersonalReward(gold);
        }
    }
}
