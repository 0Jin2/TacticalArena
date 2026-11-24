using UnityEngine;

[CreateAssetMenu(fileName = "CharacterStatData", menuName = "Character/Character Stat Data")]
public class CharacterStatData : ScriptableObject
{
    [Header("기본 스탯")]
    public float moveSpeed = 5f;          // 이동 속도
    public float jumpPower = 10f;         // 점프 힘 (클수록 높이 뜀)
    public int maxHP = 100;               // 최대 체력
    public bool allowDoubleJump = false;  // 켜면 2단 점프 가능

    [Header("스킬")]
    public float skillCooldown = 5f;                 // 스킬 재사용 대기 시간(초)
    public string skillAnimName = "Skill_AnimName";  // 스킬 사용 시 재생할 애니메이션 이름

    [Header("총기/탄환 관련")]
    public float attackCooldown = 0.5f;  // 공격 간격(초). 작을수록 빠르게 연사
    public int magazineSize = 30;        // 탄창 크기 (재장전 없이 쏠 수 있는 탄 수)
    public float reloadTime = 2f;        // 재장전 시간(초)

    [Header("발사 옵션 (모든 총기 공통)")]
    public int pelletCount = 1;         // 한 번에 발사되는 총알 수 (AR/SMG=1, 샷건=6 등)
    public float spreadAngle = 0f;      // 탄퍼짐 각도 (AR/SMG=0, 샷건=30 등)
}