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
    public GameModeType modeType = GameModeType.TDM;
    public string displayName = "팀 데스매치";
    public int teamSize = 4;                        // 한 팀 인원 (4 = 4vs4)

    [Header("진행 시간 (초)")]
    public float sceneLoadTimeout = 15f;            // 모두의 씬 로딩을 기다리는 최대 시간
    public float introSeconds = 4f;                 // 인트로 애니메이션 길이
    public float characterSelectSeconds = 30f;      // 캐릭터 선택 시간 (FALL은 10, 0 = 무제한)
    public float countdownSeconds = 5f;             // 게임 시작 전 카운트다운
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

    [Header("보상 (같은 팀 안에서 킬 많고 데스 적은 순서 1~4위)")]
    public bool distributeRewards = true;
    public int[] winGoldByRank = new int[4] { 100, 80, 60, 40 };
    public int[] loseGoldByRank = new int[4] { 50, 40, 30, 20 };
    public int[] drawGoldByRank = new int[4] { 60, 50, 40, 30 };

    [Header("씬 이동")]
    public string lobbySceneName = "LobbyPage";

    [Header("연결 끊김 / 재접속")]
    public float reconnectTimeLimit = 30f;          // 끊긴 유저를 기다려 주는 시간
    public HostLossPolicy hostLossPolicy = HostLossPolicy.ContinueWithNewHost;
    public int minPlayersPerTeam = 1;               // 한 팀이 이 인원보다 적어지면 매치 중단

    private void OnValidate()
    {
        teamSize = Mathf.Max(1, teamSize);
        sceneLoadTimeout = Mathf.Max(1f, sceneLoadTimeout);
        introSeconds = Mathf.Max(0.1f, introSeconds);
        characterSelectSeconds = Mathf.Max(0f, characterSelectSeconds);   // 0 = 무제한 (튜토리얼)
        countdownSeconds = Mathf.Max(0.1f, countdownSeconds);
        matchSeconds = Mathf.Max(0f, matchSeconds);
        resultSeconds = Mathf.Max(0.1f, resultSeconds);
        reconnectTimeLimit = Mathf.Max(0f, reconnectTimeLimit);
        minPlayersPerTeam = Mathf.Max(0, minPlayersPerTeam);
    }
}
