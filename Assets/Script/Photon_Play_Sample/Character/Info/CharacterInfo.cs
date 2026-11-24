using UnityEngine;

[CreateAssetMenu(fileName = "CharacterInfo", menuName = "Character/Info")]
public class CharacterInfo : ScriptableObject
{
    [Header("기본 정보")]
    public string characterId;    // 캐릭터 고유 ID (뒤끝 보유 캐릭터 키와 같아야 함, 예: ChulWoo)
    public string characterName;  // 화면에 표시할 캐릭터 이름
    public Sprite icon;           // 캐릭터 아이콘 이미지

    [Header("프리팹")]
    [Tooltip("캐릭터 프리팹 (예전 Photon NetworkObject 대신 일반 GameObject)")]
    public GameObject prefab;  // 게임에서 생성할 캐릭터 프리팹

    public Sprite characterIcon => icon;

    [Header("UI용 프리팹")]
    public GameObject uiPrefab;  // 캐릭터 선택 화면 등 UI에 보여 줄 프리팹
}