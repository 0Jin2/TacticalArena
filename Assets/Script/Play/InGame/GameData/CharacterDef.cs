using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// CharacterDef : 캐릭터 1명의 "정의서" (기존 CharacterInfo 대체, 포톤 의존 없음)
//   Project 창 우클릭 > Create > TAA > Character Def
//   캐릭터 추가 = Def 에셋 1개 만들고 CharacterCatalog 목록에 끼우기. 코드 수정 없음.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "CharacterDef_New", menuName = "TAA/Character Def")]
public class CharacterDef : ScriptableObject
{
    public string characterId;
    public string characterName;
    public Sprite icon;

    [Header("프리팹")]
    [Tooltip("인게임 캐릭터 프리팹. PlayerEntity가 붙어 있어야 한다.")]
    public GameObject prefab;
    [Tooltip("FALL 모드 전용 프리팹(넉백 총알 등). 비우면 prefab을 사용한다.")]
    public GameObject fallPrefab;
    [Tooltip("캐릭터 선택 화면에서 보여줄 UI용 프리팹")]
    public GameObject uiPrefab;

    public Sprite characterIcon { get { return icon; } }

    public GameObject GetPrefab(GameModeType mode)
    {
        if (mode == GameModeType.FALL && fallPrefab != null) return fallPrefab;
        return prefab;
    }
}
