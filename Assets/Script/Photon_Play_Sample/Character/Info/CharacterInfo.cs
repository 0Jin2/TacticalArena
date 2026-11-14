using UnityEngine;

[CreateAssetMenu(fileName = "CharacterInfo", menuName = "Character/Info")]
public class CharacterInfo : ScriptableObject
{
    public string characterId;
    public string characterName;
    public Sprite icon;
    [Tooltip("캐릭터 프리팹 (예전 Photon NetworkObject 대신 일반 GameObject)")]
    public GameObject prefab;

    public Sprite characterIcon => icon;

    [Header("UI용 프리팹")]
    public GameObject uiPrefab;
}