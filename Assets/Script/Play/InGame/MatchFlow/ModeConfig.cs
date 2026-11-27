using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// ModeConfig.cs : 모드(TDM/BOMB/FLAG/FALL/튜토리얼)별 "숫자 설정표"
//   - 코드를 고치지 않고 Unity 인스펙터에서 시간/규칙을 바꾸기 위한 설정 파일(ScriptableObject)
//   - 모드마다 에셋을 1개씩 만든다:  Project 창 우클릭 > Create > TAA > Mode Config
//   - 각 씬의 MatchFlow에 이 에셋을 끼워 넣는다.
// 모드 전용 값(폭탄 설치 시간 등)은 이 파일이 아니라 각 모드 스크립트에 둔다.
// ─────────────────────────────────────────────────────────────────────────────

public enum GameModeType { TDM, BOMB, FLAG, FALL, TUTORIAL }

[CreateAssetMenu(fileName = "ModeConfig_New", menuName = "TAA/Mode Config")]
public class ModeConfig : ScriptableObject
{
    [Header("기본 정보")]
    public GameModeType modeType = GameModeType.TDM;  // 이 설정을 쓰는 게임 모드 (TDM / BOMB / FLAG / FALL / TUTORIAL)
    public string displayName = "팀 데스매치";        // 화면에 보여 줄 모드 이름
    public int teamSize = 4;                        // 한 팀 인원 (4 = 4vs4)

    [Header("진행 시간 (초)")]
    public float sceneLoadTimeout = 15f;            // 모두의 씬 로딩을 기다리는 최대 시간
    public float introSeconds = 4f;                 // 인트로 애니메이션 길이
    public float characterSelectSeconds = 30f;      // 캐릭터 선택 시간 (FALL은 10, 0 = 무제한)
    public float countdownSeconds = 5f;             // 게임 시작 전 카운트다운 (끝나면 캐릭터 생성 + 카메라 줌인)
    public float zoomWaitTimeout = 10f;             // 캐릭터 생성/줌인이 끝나지 않은 유저를 기다려 주는 최대 시간
    public float matchSeconds = 600f;               // 본 게임 시간. 0 = 시간 제한 없음 (BOMB처럼 모드가 직접 관리)
    public float resultSeconds = 6f;                // 결과 화면 표시 시간

    [Header("캐릭터 선택")]
    public string defaultCharacterId = "";          // 시간 안에 못 고른 유저에게 자동으로 주는 캐릭터

    [Header("전투 공통 규칙")]
    public float respawnSeconds = 5f;               // 부활 대기
    public float invincibleSeconds = 2f;            // 부활 후 무적
    public int scoreLimit = 30;                     // 목표 점수 (TDM 30킬 등. 모드가 해석)
    public bool skillsEnabled = true;               // FALL은 false

    [Header("부활")]
    public bool respawnEnabled = true;              // BOMB은 라운드 중 부활 없음 → false

    [Header("점프 보조 (모드별 On/Off, 세부 수치는 캐릭터 프리팹의 PlayerMotor)")]
    public bool jumpBufferEnabled = true;           // 켜면 점프 버퍼: 착지 직전에 누른 점프를 기억했다가 착지하는 순간 실행
    public bool cornerCorrectionEnabled = true;     // 켜면 코너 보정: 점프 중 발이 발판 윗면보다 살짝 낮게 걸리면 위로 올려 줌

    [Header("보상 (같은 팀 안에서 킬 많고 데스 적은 순서 1~4위)")]
    public bool distributeRewards = true;                         // 켜면 경기 후 골드 보상 지급
    public int[] winGoldByRank = new int[4] { 100, 80, 60, 40 };  // 이긴 팀의 팀 내 1~4위 골드
    public int[] loseGoldByRank = new int[4] { 50, 40, 30, 20 };  // 진 팀의 팀 내 1~4위 골드
    public int[] drawGoldByRank = new int[4] { 60, 50, 40, 30 };  // 무승부일 때 1~4위 골드

    [Header("씬 이동")]
    public string lobbySceneName = "LobbyPage";  // 경기 후 돌아갈 로비 씬 이름

    [Header("연결 끊김 / 재접속")]
    public float reconnectTimeLimit = 30f;          // 끊긴 유저를 기다려 주는 시간
    public HostLossPolicy hostLossPolicy = HostLossPolicy.ContinueWithNewHost;  // 방장(호스트)이 나갔을 때: 새 호스트로 계속 / 즉시 종료
    public int minPlayersPerTeam = 1;               // 한 팀이 이 인원보다 적어지면 매치 중단

    private void OnValidate()
    {
        teamSize = Mathf.Max(1, teamSize);
        sceneLoadTimeout = Mathf.Max(1f, sceneLoadTimeout);
        introSeconds = Mathf.Max(0.1f, introSeconds);
        characterSelectSeconds = Mathf.Max(0f, characterSelectSeconds);   // 0 = 무제한 (튜토리얼)
        countdownSeconds = Mathf.Max(0.1f, countdownSeconds);
        zoomWaitTimeout = Mathf.Max(1f, zoomWaitTimeout);
        matchSeconds = Mathf.Max(0f, matchSeconds);
        resultSeconds = Mathf.Max(0.1f, resultSeconds);
        reconnectTimeLimit = Mathf.Max(0f, reconnectTimeLimit);
        minPlayersPerTeam = Mathf.Max(0, minPlayersPerTeam);
    }
}
