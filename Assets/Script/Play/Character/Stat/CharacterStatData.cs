using UnityEngine;

[CreateAssetMenu(fileName = "CharacterStatData", menuName = "Character/Character Stat Data")]
public class CharacterStatData : ScriptableObject
{
    [Header("기본 스탯")]
    public float moveSpeed = 5f;
    public float jumpPower = 10f;
    public int maxHP = 100;
    public bool allowDoubleJump = false;

    [Header("스킬")]
    public float skillCooldown = 5f;
    public string skillAnimName = "Skill_AnimName";

    [Header("총기/탄환 관련")]
    public float attackCooldown = 0.5f; // 연사 속도
    public int magazineSize = 30;
    public float reloadTime = 2f;

    [Header("발사 옵션 (모든 총기 공통)")]
    public int pelletCount = 1;         // 한 번에 발사되는 총알 수 (AR/SMG=1, 샷건=6 등)
    public float spreadAngle = 0f;      // 탄퍼짐 각도 (AR/SMG=0, 샷건=30 등)
}