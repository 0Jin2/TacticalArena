using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// TeamColorSettings : 팀 표시 색/이름을 한곳에서 정하는 설정 파일
//
//   색 규칙 (보는 사람 기준)
//     나       = 노랑   (내 닉네임, 내 체력바 등)
//     같은 팀  = 파랑
//     상대 팀  = 빨강
//   팀 이름: 1팀 / 2팀 (내부적으로는 Red = 1팀, Blue = 2팀)
//
//   사용법
//     - 이 파일은 Assets/Resources/TeamColorSettings 에셋으로 이미 만들어 두었습니다.
//       색을 바꾸고 싶으면 그 에셋을 클릭해 인스펙터에서 바꾸면 모든 UI 에 한 번에 반영됩니다.
//     - 에셋이 없거나 이름이 다르면 아래 기본값을 사용합니다. (에셋 이름과 위치를 바꾸지 마세요)
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "TeamColorSettings", menuName = "TAA/Team Color Settings")]
public class TeamColorSettings : ScriptableObject
{
    [Header("글자 / 체력바 색 (보는 사람 기준)")]
    public Color selfColor = new Color(1f, 0.85f, 0.2f, 1f);     // 나 (노랑)
    public Color allyColor = new Color(0.35f, 0.6f, 1f, 1f);     // 같은 팀 (파랑)
    public Color enemyColor = new Color(1f, 0.35f, 0.35f, 1f);   // 상대 팀 (빨강)
    public Color unknownColor = Color.white;                     // 팀을 아직 모를 때 (예외 상황)

    [Header("패널 배경 색 (캐릭터 선택 화면의 내 팀 패널 등)")]
    public Color allyPanelColor = new Color(0.09f, 0.15f, 0.25f, 0.85f);   // 우리 팀 패널 배경 (어두운 파랑)
    public Color enemyPanelColor = new Color(0.25f, 0.09f, 0.09f, 0.85f);  // 상대 팀 패널 배경 (어두운 빨강)

    [Header("팀 이름")]
    public string team1Name = "1팀";  // 1팀 이름 (내부 이름 Red)
    public string team2Name = "2팀";  // 2팀 이름 (내부 이름 Blue)
}
