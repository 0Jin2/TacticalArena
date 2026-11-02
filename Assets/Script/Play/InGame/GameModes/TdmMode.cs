using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// TdmMode : 팀 데스매치 / 낙하(FALL) / 튜토리얼 공용 모드  (기존 TDMModeHandler + FDModeHandler 대체)
//
//  규칙 (기존과 동일)
//   - 적을 처치하면 팀 점수 +1 (MatchState 가 PlayerDied 패킷으로 자동 계산)
//   - 어느 한 팀이 목표 점수(ModeConfig.scoreLimit)에 먼저 도달하면 종료. 둘이 동점으로 동시에 도달하면 계속 진행.
//   - 시간이 끝나면 점수가 높은 팀 승리, 같으면 무승부.
//   - 죽으면 respawnSeconds 후 팀 스폰 포인트 중 한 곳에서 부활 (PlayerSpawner 가 처리)
//
//  모드별 차이는 ModeConfig 로 조절한다. (이 스크립트는 그대로 재사용)
//   TDM     : scoreLimit 30(예), matchSeconds 600, skillsEnabled true
//   FALL    : skillsEnabled false, characterSelectSeconds 10  (낙하 사망은 NetKillZone 이 처리)
//   TUTORIAL: scoreLimit 0(목표 없음), matchSeconds 0(시간 제한 없음) → 자동 종료 판정 없음
// ─────────────────────────────────────────────────────────────────────────────

public class TdmMode : GameModeBase
{
    [Header("표시 문구")]
    [Tooltip("{0} 자리에 목표 점수가 들어갑니다.")]
    public string goalFormat = "목표 처치 : {0}킬";

    [Header("테스트")]
    [Tooltip("켜면 목표 점수에 도달해도 경기를 끝내지 않습니다. (혼자 연습/튜토리얼용)")]
    public bool disableGoalCheck = false;

    public int ScoreGoal { get { return Config != null ? Config.scoreLimit : 0; } }

    public override string GoalText
    {
        get { return ScoreGoal > 0 ? string.Format(goalFormat, ScoreGoal) : string.Empty; }
    }

    protected override void OnPlayingStarted(bool restored)
    {
        RaiseModeChanged();
    }

    protected override void OnHostUpdate()
    {
        if (disableGoalCheck) return;
        if (ModeType == GameModeType.TUTORIAL) return;       // 튜토리얼은 자동 종료 없음
        int goal = ScoreGoal;
        if (goal <= 0) return;

        int red = ScoreOf(NetTeam.Red);
        int blue = ScoreOf(NetTeam.Blue);
        if (red < goal && blue < goal) return;

        // 한 팀만 목표를 넘겼거나 점수 차이가 있으면 높은 쪽 승리. 완전 동점이면 다음 득점까지 계속.
        if (red == blue) return;
        TryEndMatch(red > blue ? NetTeam.Red : NetTeam.Blue);
    }

    protected override void OnTimeUpHost()
    {
        // 튜토리얼처럼 시간 제한이 없으면 MatchFlow 가 이 이벤트를 부르지 않는다.
        TryEndMatch(WinnerByScore());
    }
}
