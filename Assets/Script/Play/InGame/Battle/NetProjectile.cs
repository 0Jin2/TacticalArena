using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// NetProjectile : 모든 총알 공통 스크립트
//   기존 Projectile / KnockbackProjectile / ChargeShotProjectile 3개를 하나로 합쳤다.
//   차이는 인스펙터 값으로 만든다:
//     일반탄    : 기본값
//     차지샷    : damage 를 크게, speed/lifeTime 조정
//     넉백탄(FALL): stopOnWorld = false, applyKnockback = true
//
//  빠른 총알 관통 방지
//   총알은 물리 계산 1번(0.02초)마다 "속도 × 0.02" 만큼 순간이동한다. (속도 50 이면 한 번에 1m)
//   캐릭터 몸이 그보다 얇으면 총알이 몸을 "건너뛰어" 충돌 이벤트가 안 생긴다 → 가까이에서는 안 맞고 거리가 맞아야 맞는 현상.
//   그래서 FixedUpdate 에서 "이번에 이동할 길"을 미리 훑어(Cast) 그 사이에 있는 대상도 맞은 것으로 처리한다.
//
//  동작 방식 (릴레이 서버 구조)
//   - 사격자가 Fire 패킷을 보내면 "모든 기기가 같은 총알을 각자 만들어" 날린다. (서버가 총알을 대신 관리하지 않음)
//   - 맞았는지 판정하고 Damage 패킷을 보내는 사람은 "총알 주인(사격자)의 기기" 한 곳뿐이다.
//     다른 기기의 총알 복사본은 충돌 연출(이펙트)만 재생한다.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class NetProjectile : MonoBehaviour
{
    [Header("총알 수치")]
    public float speed = 10f;
    public float lifeTime = 2f;
    public int damage = 10;

    [Header("충돌 규칙")]
    [Tooltip("땅/벽에 부딪히면 멈추고 사라진다. 끄면 맵을 통과한다. (FALL 넉백탄)")]
    public bool stopOnWorld = true;

    [Header("넉백 (FALL 모드 등)")]
    public bool applyKnockback = false;
    public float knockbackForce = 25f;
    public float knockbackUpBias = 0.4f;
    public float knockbackLockSeconds = 0.2f;
    [Tooltip("true: 총알이 가던 방향으로 밀어낸다 / false: 반대쪽으로 끌어당긴다")]
    public bool knockAlongBulletDirection = true;

    [Header("히트 이펙트 (자식 오브젝트 연결)")]
    public SpriteRenderer bulletSprite;
    public GameObject fxHitPlayer;
    public GameObject fxHitBarrier;
    public GameObject fxHitWorld;
    [Tooltip("이펙트 애니메이션이 끝나기를 기다리는 최대 시간")]
    public float effectTimeout = 10f;

    public uint OwnerSession { get; private set; }
    /// <summary>이 총알의 주인이 "이 기기"인가 → 맞았는지 판정하고 Damage 를 보내는 쪽</summary>
    public bool IsAuthority { get; private set; }

    private enum HitKind { World = 0, Player = 1, Barrier = 2, Other = 3 }

    private Rigidbody2D body;
    private Collider2D myCollider;
    private NetTeam ownerTeam = NetTeam.None;
    private bool hasImpacted;
    private bool launched;
    private readonly HashSet<ulong> damaged = new HashSet<ulong>();

    // 이동 경로 훑기(관통 방지)용
    private static readonly RaycastHit2D[] castBuffer = new RaycastHit2D[16];
    private ContactFilter2D castFilter;
    private bool hasImpactPos;
    private Vector2 impactPos;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        myCollider = GetComponent<Collider2D>();
        if (bulletSprite == null) bulletSprite = GetComponentInChildren<SpriteRenderer>(true);

        castFilter = new ContactFilter2D();
        castFilter.NoFilter();                 // 모든 레이어, 트리거 포함 (무엇을 무시할지는 HandleContact 가 정한다)
        castFilter.useTriggers = true;
    }

    /// <summary>생성 직후 호출. dir 은 정규화된 진행 방향.</summary>
    public void Launch(uint ownerSession, Vector2 dir)
    {
        OwnerSession = ownerSession;
        var net = InGameNet.Instance;
        if (net != null)
        {
            IsAuthority = ownerSession == net.LocalSessionId;
            var info = net.GetPlayer(ownerSession);
            ownerTeam = info != null ? info.Team : NetTeam.None;
        }

        body.linearVelocity = dir.normalized * speed;
        launched = true;
        Invoke(nameof(DestroySelf), lifeTime);
    }

    // 물리 계산 직전: 이번 한 칸 동안 지나갈 길에 있는 대상을 미리 찾아 처리한다. (얇은 몸을 건너뛰지 않게)
    private void FixedUpdate()
    {
        if (!launched || hasImpacted) return;

        Vector2 velocity = body.linearVelocity;
        float distance = velocity.magnitude * Time.fixedDeltaTime;
        if (distance <= 0.0001f) return;

        int count = myCollider.Cast(velocity.normalized, castFilter, castBuffer, distance);
        if (count <= 0) return;

        // 가까운 것부터 처리 (총알은 처음 맞은 것에서 멈춘다)
        for (int i = 1; i < count; i++)
        {
            var key = castBuffer[i];
            int j = i - 1;
            while (j >= 0 && castBuffer[j].distance > key.distance) { castBuffer[j + 1] = castBuffer[j]; j--; }
            castBuffer[j + 1] = key;
        }

        for (int i = 0; i < count && !hasImpacted; i++)
        {
            var hit = castBuffer[i];
            var col = hit.collider;
            if (col == null) continue;
            if (!stopOnWorld && GameLayers.IsWorld(col.gameObject.layer)) continue;   // 통과하는 총알은 맵을 무시

            impactPos = hit.centroid;           // 맞은 자리에서 이펙트가 나오도록
            hasImpactPos = true;
            HandleContact(col);
        }
        hasImpactPos = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!launched || hasImpacted) return;
        HandleContact(other);
    }

    // 트리거가 아닌 충돌(타일맵 콜라이더 등)도 땅/벽이면 똑같이 처리
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!launched || hasImpacted) return;
        if (stopOnWorld && GameLayers.IsWorld(collision.gameObject.layer)) Impact(HitKind.World);
    }

    private void HandleContact(Collider2D other)
    {
        int layer = other.gameObject.layer;
        if (GameLayers.IsIgnoredByBullet(layer)) return;

        // 맵(땅/벽)
        if (GameLayers.IsWorld(layer))
        {
            if (stopOnWorld) Impact(HitKind.World);
            else Physics2D.IgnoreCollision(myCollider, other, true);     // 스치듯 통과
            return;
        }

        // 맞을 수 있는 대상 찾기 (캐릭터, 방벽 ...)
        var target = other.GetComponentInParent<IDamageTarget>();
        if (target == null) return;

        // 이미 죽은 대상은 통과
        if (!target.IsAlive)
        {
            Physics2D.IgnoreCollision(myCollider, other, true);
            return;
        }

        bool isMine = target.OwnerSession == OwnerSession;
        bool isAlly = ownerTeam != NetTeam.None && target.Team == ownerTeam;

        if (target.Kind == DamageTargetKind.Player)
        {
            if (isMine || isAlly) return;                                 // 자기 자신/아군은 통과
            if (!damaged.Add(Key(target))) return;                        // 한 총알이 같은 대상을 두 번 때리지 않음

            if (IsAuthority) DealDamage(target, other);
            Impact(HitKind.Player);
            return;
        }

        // 설치물(방벽 등)
        if (isMine)
        {
            Physics2D.IgnoreCollision(myCollider, other, true);           // 내 방벽은 통과
            return;
        }
        if (isAlly)
        {
            Impact(HitKind.Barrier);                                      // 아군 방벽: 피해 없이 총알만 소멸
            return;
        }

        if (IsAuthority && damaged.Add(Key(target))) DealDamage(target, other);
        Impact(HitKind.Barrier);
    }

    private static ulong Key(IDamageTarget t) { return ((ulong)t.OwnerSession << 8) | t.Slot; }

    private void DealDamage(IDamageTarget target, Collider2D other)
    {
        DamageRegistry.Send(target, OwnerSession, damage);

        if (applyKnockback && target.Kind == DamageTargetKind.Player)
        {
            Vector2 dir = body.linearVelocity.sqrMagnitude > 0.0001f
                ? body.linearVelocity.normalized
                : ((Vector2)other.bounds.center - (Vector2)transform.position).normalized;

            uint victim = target.OwnerSession;
            bool opposite = !knockAlongBulletDirection;
            var net = InGameNet.Instance;
            if (net != null)
            {
                net.Send(NetPacketType.Knockback, w =>
                {
                    w.WriteUInt(victim);
                    w.WriteVector2(dir);
                    w.WriteFloat(knockbackForce);
                    w.WriteFloat(knockbackUpBias);
                    w.WriteFloat(knockbackLockSeconds);
                    w.WriteBool(opposite);
                }, true);
            }
        }
    }

    private void Impact(HitKind kind)
    {
        if (hasImpacted) return;
        hasImpacted = true;
        CancelInvoke(nameof(DestroySelf));        // 이펙트 재생 중 수명으로 사라지지 않게

        if (hasImpactPos)
        {
            body.position = impactPos;
            transform.position = new Vector3(impactPos.x, impactPos.y, transform.position.z);
        }

        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.bodyType = RigidbodyType2D.Kinematic;
        body.constraints = RigidbodyConstraints2D.FreezeAll;
        myCollider.enabled = false;

        if (bulletSprite != null)
        {
            var c = bulletSprite.color;
            c.a = 0f;
            bulletSprite.color = c;
        }

        GameObject fx = null;
        switch (kind)
        {
            case HitKind.Player: fx = fxHitPlayer; break;
            case HitKind.Barrier: fx = fxHitBarrier; break;
            default: fx = fxHitWorld; break;
        }

        if (fx != null)
        {
            fx.SetActive(true);
            StartCoroutine(Co_WaitEffectThenDestroy(fx));
        }
        else
        {
            DestroySelf();
        }
    }

    private IEnumerator Co_WaitEffectThenDestroy(GameObject effectRoot)
    {
        yield return null;      // Animator 가 시작될 한 프레임

        var anim = effectRoot.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            float safety = effectTimeout;
            while (safety > 0f)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                if (!anim.IsInTransition(0) && st.normalizedTime >= 1f) break;
                safety -= Time.deltaTime;
                yield return null;
            }
        }
        DestroySelf();
    }

    private void DestroySelf()
    {
        if (this != null && gameObject != null) Destroy(gameObject);
    }
}
