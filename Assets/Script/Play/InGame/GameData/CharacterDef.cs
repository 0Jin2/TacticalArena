using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// CharacterDef : 캐릭터 1명의 "정의서" (기존 CharacterInfo 대체, 포톤 의존 없음)
//   Project 창 우클릭 > Create > TAA > Character Def
//   캐릭터 추가 = Def 에셋 1개 만들고 CharacterCatalog 목록에 끼우기. 코드 수정 없음.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "CharacterDef_New", menuName = "TAA/Character Def")]
public class CharacterDef : ScriptableObject
{
    [Header("기본 정보")]
    public string characterId;    // 캐릭터 고유 ID (뒤끝 보유 캐릭터 키와 같아야 함, 예: ChulWoo)
    public string characterName;  // 화면에 표시할 캐릭터 이름
    public Sprite icon;           // 캐릭터 아이콘 (상단 점수판 / 결과 화면 등에 표시)

    [Header("프리팹")]
    [Tooltip("인게임 캐릭터 프리팹. PlayerEntity가 붙어 있어야 한다.")]
    public GameObject prefab;      // 인게임 캐릭터 프리팹 (PlayerEntity 가 붙어 있어야 함)
    [Tooltip("FALL 모드 전용 프리팹(넉백 총알 등). 비우면 prefab을 사용한다.")]
    public GameObject fallPrefab;  // FALL 모드 전용 프리팹 (비우면 prefab 사용)
    [Tooltip("캐릭터 선택 화면에서 보여줄 UI용 프리팹")]
    public GameObject uiPrefab;    // 캐릭터 선택 화면에 보여 줄 UI 프리팹

    public Sprite characterIcon { get { return icon; } }

    public GameObject GetPrefab(GameModeType mode)
    {
        if (mode == GameModeType.FALL && fallPrefab != null) return fallPrefab;
        return prefab;
    }
}
