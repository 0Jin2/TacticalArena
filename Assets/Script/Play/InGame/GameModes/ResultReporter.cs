using System;
using System.Collections.Generic;
using BackEnd;
using BackEnd.Tcp;
using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
// ResultReporter : 경기가 끝난 뒤의 뒷처리 담당
//   (기존 각 모드 핸들러의 DistributePersonalRewards / RPC_SendPersonalReward / ShutdownAfterDelay 대체)
//
//  1) 개인 보상 : 결과 단계가 시작되면 "내 기기가 스스로" 내 보상을 계산해서 골드를 지급한다.
//                 (포톤은 방장이 모두의 보상을 계산해 RPC 로 보냈지만, 모두가 같은 점수/킬/데스 기록을 갖고 있으므로
//                  각자 계산해도 결과가 같다. 지급은 한 번만.)
//                 같은 팀 안에서 킬 많은 순 → 데스 적은 순 → 세션 번호 순으로 순위를 매기고,
//                 킬/데스가 같으면 같은 순위 칸을 공유한다. (기존과 동일)
//  2) 결과 보고 : 호스트(슈퍼 게이머)가 뒤끝 서버에 MatchEnd 로 승/패/무를 보고한다.
//  3) 로비 복귀 : 결과 화면 시간이 끝나면(MatchFlow.OnMatchFinished) 서버에서 나가고 로비 씬으로 이동한다.
//
//  배치: GameRoot (MatchFlow 와 같은 오브젝트)
// ─────────────────────────────────────────────────────────────────────────────

public class ResultReporter : MonoBehaviour
{
    public static ResultReporter Instance { get; private set; }

    [Header("보상")]
    [Tooltip("끄면 이 스크립트가 골드를 직접 지급하지 않습니다. (보상 표시만 하고 지급은 다른 곳에서 할 때)")]
    public bool grantGold = true;

    [Header("뒤끝 서버 보고")]
    [Tooltip("결과 단계가 시작된 뒤 이 시간(초)이 지나면 호스트가 MatchEnd 를 보냅니다. (다른 유저가 결과 패킷을 받을 시간을 주기 위함)")]
    public float matchEndDelaySeconds = 1.5f;
    public bool reportMatchEnd = true;

    [Header("로비 복귀")]
    public bool returnToLobbyAutomatically = true;
    [Tooltip("비워 두면 ModeConfig.lobbySceneName 을 사용")]
    public string lobbySceneOverride = "";

    /// <summary>내 보상 골드 (결과 단계 시작 때 확정, 그 전에는 0)</summary>
    public int LocalReward { get; private set; }
    /// <summary>내 팀 안에서의 순위 칸 (1부터, 0 = 모름)</summary>
    public int LocalRankSlot { get; private set; }
    public bool RewardResolved { get; private set; }
    public bool Reported { get; private set; }

    /// <summary>보상이 확정되었을 때 (골드). 결과 UI 가 "+100G" 를 표시</summary>
    public event Action<int> OnRewardResolved;

    private MatchFlow flow;
    private InGameNet net;
    private bool subscribed;
    private double reportAt;
    private bool leaving;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        net = InGameNet.Ensure();
        flow = MatchFlow.Instance;
        if (flow == null) { NetLog.Error("ResultReporter: MatchFlow 가 씬에 없습니다."); return; }

        flow.OnPhaseChanged += HandlePhaseChanged;
        flow.OnMatchFinished += HandleMatchFinished;
        subscribed = true;

        // 이미 결과 단계로 복구된 경우(재접속)
        if (flow.Phase == MatchPhase.Result) BeginResult();
    }

    private void OnDestroy()
    {
        if (subscribed && flow != null)
        {
            flow.OnPhaseChanged -= HandlePhaseChanged;
            flow.OnMatchFinished -= HandleMatchFinished;
        }
        if (Instance == this) Instance = null;
    }

    private void HandlePhaseChanged(MatchPhase phase, bool restored)
    {
        if (phase == MatchPhase.Result) BeginResult();
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region 결과 시작

    private void BeginResult()
    {
        if (RewardResolved) return;

        ResolveLocalReward();

        // 이후 서버 접속이 끊겨도 정상 종료로 보게 한다. (재접속 시도 금지, 저장된 매치 기록 삭제)
        reportAt = NetClock.Now + Mathf.Max(0f, matchEndDelaySeconds);

        // 서버 보고에 필요한 명단/세션은 호스트가 보고하기 전까지 그대로 유지된다.
        if (net != null && net.Connection != null) net.Connection.NotifyMatchEnded();
    }

    private void Update()
    {
        if (flow == null || flow.Phase != MatchPhase.Result) return;
        if (Reported || !reportMatchEnd) return;
        if (NetClock.Now < reportAt) return;
        if (net == null || !net.IsHost) return;           // 호스트(슈퍼 게이머)만 보고

        Reported = true;
        ReportMatchEnd();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 보상

    private void ResolveLocalReward()
    {
        RewardResolved = true;
        LocalReward = 0;
        LocalRankSlot = 0;

        var config = flow.Config;
        var state = MatchState.Instance;
        var me = net != null ? net.LocalPlayer : null;

        if (config != null && config.distributeRewards && me != null && me.Team != NetTeam.None && state != null)
        {
            int slot = RankSlotOf(state, me);
            LocalRankSlot = slot + 1;

            int[] table =
                flow.Winner == NetTeam.None ? config.drawGoldByRank :
                flow.Winner == me.Team ? config.winGoldByRank :
                config.loseGoldByRank;

            // 기존: 4구간까지만 지급
            if (slot >= 0 && slot < 4 && table != null && slot < table.Length) LocalReward = table[slot];
        }

        NetLog.Info("[보상] 팀=" + (me != null ? me.Team.ToString() : "?") + " 순위칸=" + LocalRankSlot + " 골드=" + LocalReward);

        if (grantGold && LocalReward > 0 && UserInfoManager.Instance != null)
            UserInfoManager.Instance.AddGold(LocalReward);

        if (OnRewardResolved != null) OnRewardResolved(LocalReward);
    }

    /// <summary>같은 팀 안에서의 순위 칸(0부터). 킬·데스가 모두 같으면 같은 칸을 공유한다.</summary>
    public static int RankSlotOf(MatchState state, NetPlayer target)
    {
        var ranked = state.Ranked(target.Team);       // 킬 ↓, 데스 ↑, 세션 ↑ (영구 이탈자 제외)
        int slot = -1;
        int prevK = int.MinValue, prevD = int.MaxValue;

        for (int i = 0; i < ranked.Count; i++)
        {
            int k = state.GetKills(ranked[i].SessionId);
            int d = state.GetDeaths(ranked[i].SessionId);
            if (i == 0) slot = 0;
            else if (!(k == prevK && d == prevD)) slot++;
            prevK = k; prevD = d;

            if (ranked[i].SessionId == target.SessionId) return slot;
        }
        return -1;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 뒤끝 서버 보고

    private void ReportMatchEnd()
    {
        if (net == null || net.IsOfflineTest)
        {
            NetLog.Info("ResultReporter: 오프라인 테스트라 MatchEnd 를 보내지 않습니다.");
            return;
        }

        try
        {
            var result = new MatchGameResult();
            result.m_winners = new List<SessionId>();
            result.m_losers = new List<SessionId>();
            result.m_draws = new List<SessionId>();

            NetTeam winner = flow.Winner;
            foreach (var p in net.Players)
            {
                if (winner == NetTeam.None) result.m_draws.Add(p.RawSession);
                else if (p.Team == winner && !p.IsForfeited) result.m_winners.Add(p.RawSession);
                else result.m_losers.Add(p.RawSession);            // 상대 팀 + 이탈한 유저
            }

            Backend.Match.MatchEnd(result);
            NetLog.Info("MatchEnd 전송: 승=" + result.m_winners.Count + " 패=" + result.m_losers.Count + " 무=" + result.m_draws.Count);
        }
        catch (Exception exception)
        {
            // SDK 버전에 따라 MatchGameResult 의 필드 이름이 다르면 이 부분만 맞춰 주세요.
            NetLog.Error("MatchEnd 전송 실패: " + exception.Message);
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 로비 복귀

    private void HandleMatchFinished()
    {
        if (returnToLobbyAutomatically) ReturnToLobby();
    }

    /// <summary>[버튼용] 지금 바로 서버에서 나가고 로비로 이동한다.</summary>
    public void ReturnToLobby()
    {
        if (leaving) return;
        leaving = true;

        // 호스트가 아직 보고를 못 했다면 지금 보고 (나가면 보고할 수 없다)
        if (!Reported && reportMatchEnd && net != null && net.IsHost)
        {
            Reported = true;
            ReportMatchEnd();
        }

        try { if (Backend.IsInitialized) Backend.Match.LeaveGameServer(); }
        catch (Exception exception) { NetLog.Warn("LeaveGameServer 예외: " + exception.Message); }

        string scene = !string.IsNullOrEmpty(lobbySceneOverride) ? lobbySceneOverride
            : (flow != null && flow.Config != null ? flow.Config.lobbySceneName : "LobbyPage");
        SceneManager.LoadScene(scene);
    }

    #endregion
}
