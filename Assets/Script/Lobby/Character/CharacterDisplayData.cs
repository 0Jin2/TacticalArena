using UnityEngine;

[CreateAssetMenu(fileName = "CharacterDisplayData", menuName = "Character/Display Data")]
public class CharacterDisplayData : ScriptableObject
{
    [Header("식별자(뒤끝 key)")]
    public string characterDataKey;

    [Header("기본 정보")]
    public string characterName;
    public Sprite weaponImage;
    [TextArea] public string weaponDescription;
    public Sprite skillIcon;
    [TextArea] public string skillDescription;

    [Header("스탯")]
    public float moveSpeed;
    public int maxHP;
    public int goldPrice;
    public int gemPrice;
}