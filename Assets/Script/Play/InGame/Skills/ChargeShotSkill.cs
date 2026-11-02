using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// ChargeShotSkill : 알렉스 루벨 - 차지샷
//   스킬 버튼 → 충전 상태(이펙트 ON, 기본 공격 잠금) → 공격 버튼을 누르면 강력한 탄 1발 발사
//   충전 중 스킬 버튼을 다시 누르거나 keepSeconds 가 지나면 취소.
//   총알 수치(피해량/속도)는 chargeProjectilePrefab 의 NetProjectile 에서 조절한다.
// ─────────────────────────────────────────────────────────────────────────────

public class ChargeShotSkill : SkillBase, IAttackOverride
{
    [Header("차지샷")]
    public GameObject chargeEffectObject;               // 충전 이펙트 (평소 꺼 두기)
    public NetProjectile chargeProjectilePrefab;
    [Tooltip("비우면 기본 공격의 발사 위치를 사용한다.")]
    public Transform projectileSpawnPoint;
    public float keepSeconds = 3f;                       // 충전 상태 유지 시간
    public float fireDelay = 0.01f;
    [Tooltip("충전 중 이동 불가. (기존 동작은 처음에 한 번 멈추기만 하고 이동 가능)")]
    public bool lockMovementWhileCharging = false;

    private const byte PhaseCharge = 1;     // [충전 중? bool]
    private const byte PhaseShoot = 2;      // [발사 위치 vec2][방향 byte]

    private bool fireRequested;
    private bool cancelRequested;

    public override bool BlocksBasicAttack { get { return IsActive; } }

    protected override void OnSetup()
    {
        if (chargeEffectObject != null) chargeEffectObject.SetActive(false);
    }

    protected override void OnPressedWhileActive() { cancelRequested = true; }

    /// <summary>충전 중 공격 버튼 입력 (PlayerCombat 이 호출)</summary>
    public void OnAttackPressed() { fireRequested = true; }

    protected override IEnumerator Run()
    {
        fireRequested = false;
        cancelRequested = false;

        Broadcast(PhaseCharge, w => w.WriteBool(true));
        Entity.Combat.AttackOverride = this;
        Entity.Motor.StopHorizontal();
        if (lockMovementWhileCharging) Entity.Motor.SetMovementLock(true);

        float timer = 0f;
        while (!fireRequested && !cancelRequested && timer < keepSeconds)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        Entity.Combat.AttackOverride = null;
        bool fire = fireRequested;

        if (fire)
        {
            yield return new WaitForSeconds(fireDelay);
            if (!Entity.Health.IsDead) Shoot();
            if (Entity.Input != null) Entity.Input.SuppressAttackUntilRelease();
        }

        Entity.Motor.SetMovementLock(false);
        Broadcast(PhaseCharge, w => w.WriteBool(false));
    }

    private void Shoot()
    {
        Transform point = projectileSpawnPoint != null ? projectileSpawnPoint : Entity.Combat.projectileSpawnPoint;
        Vector2 origin = point != null ? (Vector2)point.position : (Vector2)transform.position;
        byte facing = Entity.Motor.Facing < 0f ? (byte)0 : (byte)1;
        Broadcast(PhaseShoot, w => { w.WriteVector2(origin); w.WriteByte(facing); });
    }

    protected override void OnNetEvent(byte phase, NetReader r)
    {
        switch (phase)
        {
            case PhaseCharge:
                if (chargeEffectObject != null) chargeEffectObject.SetActive(r.ReadBool());
                break;

            case PhaseShoot:
                SpawnProjectile(r.ReadVector2(), r.ReadByte() == 0 ? -1f : 1f);
                break;
        }
    }

    private void SpawnProjectile(Vector2 origin, float facing)
    {
        if (chargeProjectilePrefab == null) { NetLog.Warn("ChargeShotSkill: chargeProjectilePrefab 이 비어 있습니다."); return; }
        Vector2 dir = Vector2.right * facing;
        var bullet = Instantiate(chargeProjectilePrefab, origin, Quaternion.FromToRotation(Vector3.right, (Vector3)dir));
        bullet.Launch(Entity.SessionId, dir);
    }

    protected override void OnCancel()
    {
        if (chargeEffectObject != null) chargeEffectObject.SetActive(false);
        if (Entity != null && Entity.Combat != null && ReferenceEquals(Entity.Combat.AttackOverride, this))
            Entity.Combat.AttackOverride = null;
    }
}
