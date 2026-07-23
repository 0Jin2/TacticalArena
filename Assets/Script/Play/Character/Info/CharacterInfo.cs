using UnityEngine;
using Fusion;

[CreateAssetMenu(fileName = "CharacterInfo", menuName = "Character/Info")]
public class CharacterInfo : ScriptableObject
{
    public string characterId;
    public string characterName;
    public Sprite icon;
    public NetworkObject prefab;

    public Sprite characterIcon => icon;

    [Header("UI용 프리팹")]
    public GameObject uiPrefab;
}