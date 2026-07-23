using Fusion;
using UnityEngine;
using System;

public abstract class ModeHandlerBase : NetworkBehaviour
{
    [Networked] protected float TimeLimit { get; set; }
    [Networked] protected float RespawnDelay { get; set; }
    [Networked] protected TickTimer RoundTimer { get; set; }

    [Networked(OnChanged = nameof(OnMatchStartChanged))]
    protected long MatchStartUnixMillis { get; set; }

    private long _hostToLocalOffsetMillis;

    public override void Spawned()
    {
        if (Object.HasStateAuthority)
        {
            // 서버에서만 라운드 타이머 시작
            StartMatchTimer();
            OnGameStart_ServerOnly();
        }
    }

    public override void FixedUpdateNetwork()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Tutorial"))
        {
            // Tutorial 모드에서는 타이머 계산 및 게임 종료 판정 비활성화
            return;
        }

        // 1) 모든 클라: 호스트 시계 기반으로 남은 시간 계산 → UI 동기화
        float remain = GetRemainingTime();
        if (remain < 0f) remain = 0f;
        GamePlayUIController.Instance?.UpdateTimer(remain);

        // 2) 서버만: TickTimer 로 타임업 판정(권위 유지)
        if (Object.HasStateAuthority && RoundTimer.Expired(Runner))
        {
            EndGame();
        }
    }

    // 서버에서 라운드 타이머 시작(종료 판정용) + 호스트 시작시각 기록(클라 UI용)
    protected void StartMatchTimer()
    {
        if (!Object.HasStateAuthority) return;

        RoundTimer = TickTimer.CreateFromSeconds(Runner, TimeLimit);

        // ⏱ 시작시각(호스트 시계) 최초 1회 세팅
        if (MatchStartUnixMillis == 0L)
        {
            MatchStartUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    // 🔵 클라가 복제/갱신을 받는 순간 “호스트-로컬” 오프셋 재보정
    private static void OnMatchStartChanged(Changed<ModeHandlerBase> changed)
    {
        if (!changed.Behaviour.Object.HasStateAuthority)
        {
            long hostTime = changed.Behaviour.MatchStartUnixMillis;
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            changed.Behaviour._hostToLocalOffsetMillis = hostTime - localNow;
        }
    }

    // 🔵 모든 클라가 동일하게 계산할 남은 시간(초): 호스트 시계 기반
    protected float GetRemainingTime()
    {
        if (MatchStartUnixMillis <= 0L)
        {
            // 안전장치: 시작시각 동기화 전에는 TickTimer 값으로 폴백
            return RoundTimer.RemainingTime(Runner) ?? TimeLimit;
        }

        long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long hostNow = localNow + _hostToLocalOffsetMillis;
        double elapsed = (hostNow - MatchStartUnixMillis) / 1000.0;
        return Mathf.Max(TimeLimit - (float)elapsed, 0f);
    }

    protected abstract void OnGameStart_ServerOnly();
    protected virtual void OnRoundStart_ServerOnly(int roundNumber) { }
    protected abstract void EndGame();

    // 공통 리스폰 예약용(원하면 파생 클래스에서 사용할 수 있게 빈 메서드)
    protected void ScheduleRespawn(PlayerRef player) { }

    // ==== UI 초기화를 모든 클라에 브로드캐스트하기 위한 RPC들 ====
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    protected void RPC_InitTDMUI(float timeLimit, int scoreGoal)
    {
        GamePlayUIController.Instance?.InitTDMUI(timeLimit, scoreGoal);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    protected void RPC_InitFlagUI(float timeLimit, int captureGoal)
    {
        GamePlayUIController.Instance?.InitFlagUI(timeLimit, captureGoal);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    protected void RPC_InitBombUI(int totalRounds, float roundTime)
    {
        GamePlayUIController.Instance?.InitBombUI(totalRounds, roundTime);
    }
}
