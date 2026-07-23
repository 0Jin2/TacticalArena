using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Projectile : NetworkBehaviour
{
    [Header("총알 스탯")]
    public float speed = 10f;
    public float lifeTime = 2f;
    public int damage = 10;

    [Header("히트 이펙트 (자식 오브젝트 할당)")]
    public SpriteRenderer bulletSprite;   // 비워두면 자동으로 하위에서 찾음
    public GameObject fxHitPlayer;
    public GameObject fxHitBarrier;
    public GameObject fxHitWorld;

    private Rigidbody2D rb;
    [Networked] public float direction { get; set; }
    [Networked] public PlayerRef owner { get; set; }

    private HashSet<PlayerRef> damagedPlayers = new HashSet<PlayerRef>();
    private bool hasImpacted = false;
    private Collider2D myCol;

    private enum HitKind : int { World = 0, Player = 1, Barrier = 2, Other = 3 }

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        myCol = GetComponent<Collider2D>();
        if (bulletSprite == null) bulletSprite = GetComponentInChildren<SpriteRenderer>(true);

        rb.velocity = transform.right * speed;
        Invoke(nameof(DestroySelf), lifeTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!Object.HasStateAuthority) return;

        int layer = other.gameObject.layer;

        // 총알/경계/시체는 무시
        if (layer == LayerMask.NameToLayer("Bullet") ||
            layer == LayerMask.NameToLayer("Confiner") ||
            layer == LayerMask.NameToLayer("DeadBody"))
            return;

        // === Ground/Wall: 멈추고 월드 이펙트 ===
        if (layer == LayerMask.NameToLayer("Ground") ||
            layer == LayerMask.NameToLayer("Wall"))
        {
            ImpactStop(HitKind.World);
            return;
        }

        // === 데미지 대상 검색 ===
        var health = other.GetComponent<Health>() ?? other.GetComponentInParent<Health>();
        var targetNO = other.GetComponent<NetworkObject>() ?? other.GetComponentInParent<NetworkObject>();
        if (health == null || targetNO == null) return;

        if (health.IsDead)
        {
            if (myCol != null) Physics2D.IgnoreCollision(myCol, other, true);
            return;
        }

        // === Player ===
        if (layer == LayerMask.NameToLayer("Player"))
        {
            if (targetNO.InputAuthority == owner) return;
            if (damagedPlayers.Contains(targetNO.InputAuthority)) return;

            // 아군이면 무시
            var tm = TeamAssigner.Instance;
            if (tm != null &&
                tm.Teams.TryGet(owner, out var ownerTeam) &&
                tm.Teams.TryGet(targetNO.InputAuthority, out var targetTeam) &&
                ownerTeam == targetTeam)
            {
                return;
            }

            damagedPlayers.Add(targetNO.InputAuthority);
            health.RPC_ApplyDamage(damage, owner);

            // 💥 플레이어 히트 연출(멈춤 + 이펙트)
            ImpactStop(HitKind.Player);
            return;
        }

        // === Barrier ===
        if (layer == LayerMask.NameToLayer("Barrier"))
        {
            // 자기 방벽이면 통과
            if (targetNO.InputAuthority == owner)
            {
                if (myCol != null) Physics2D.IgnoreCollision(myCol, other, true);
                return;
            }

            var tm = TeamAssigner.Instance;
            if (tm != null &&
                tm.Teams.TryGet(owner, out var ownerTeam) &&
                tm.Teams.TryGet(targetNO.InputAuthority, out var barrierTeam) &&
                ownerTeam == barrierTeam)
            {
                // 아군 방벽: 데미지 없이 이펙트만
                ImpactStop(HitKind.Barrier);
                return;
            }

            // 적 방벽: 데미지 후 이펙트
            health.RPC_ApplyDamage(damage, owner);
            ImpactStop(HitKind.Barrier);
            return;
        }

        // === 기타 Health 오브젝트 ===
        health.RPC_ApplyDamage(damage, owner);
        ImpactStop(HitKind.Other);
    }

    // 비트리거 충돌(타일 콜라이더 등)도 동일 처리
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!Object.HasStateAuthority) return;

        int layer = collision.gameObject.layer;
        if (layer == LayerMask.NameToLayer("Ground") ||
            layer == LayerMask.NameToLayer("Wall"))
        {
            ImpactStop(HitKind.World);
        }
    }

    private void ImpactStop(HitKind kind)
    {
        if (hasImpacted) return;
        hasImpacted = true;

        // 모든 클라에 충돌 연출 전파 (여기서부터 lifeTime 파괴는 취소됨)
        RPC_OnImpact((int)kind);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnImpact(int kindInt)
    {
        // 물리 정지 + 충돌 비활성
        if (rb != null)
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
        }
        if (myCol != null) myCol.enabled = false;

        // ✅ 애니메이션 재생 중 lifeTime으로 파괴되지 않도록 방어
        CancelInvoke(nameof(DestroySelf));

        // 총알 스프라이트 숨김(알파 0)
        if (bulletSprite != null)
        {
            var c = bulletSprite.color;
            c.a = 0f;
            bulletSprite.color = c;
        }

        // 충돌 종류별 이펙트 활성화
        GameObject selected = null;
        HitKind kind = (HitKind)kindInt;
        switch (kind)
        {
            case HitKind.Player:
                if (fxHitPlayer != null) { fxHitPlayer.SetActive(true); selected = fxHitPlayer; }
                break;
            case HitKind.Barrier:
                if (fxHitBarrier != null) { fxHitBarrier.SetActive(true); selected = fxHitBarrier; }
                break;
            case HitKind.World:
            default:
                if (fxHitWorld != null) { fxHitWorld.SetActive(true); selected = fxHitWorld; }
                break;
        }

        // 🔁 이펙트 애니메이션(또는 파티클) 종료까지 대기 후 파괴
        if (selected != null)
            StartCoroutine(Co_WaitEffectThenDespawn(selected));
        else
        {
            // 이펙트가 없으면 즉시 파괴
            if (Object.HasStateAuthority) DestroySelf();
            else RPC_RequestDespawnAfterImpact();
        }
    }

    private System.Collections.IEnumerator Co_WaitEffectThenDespawn(GameObject effectRoot)
    {
        // 이펙트가 켜진 프레임을 넘겨 Animator/Particle이 시작되도록 한 프레임 대기
        yield return null;

        // 1) Animator 기반 이펙트면: 1회 재생 종료까지 대기
        var anim = effectRoot.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            int layer = 0;
            float safety = 10f; // 혹시 모를 루프/전이 꼬임 대비 안전 타임아웃
            while (safety > 0f)
            {
                var st = anim.GetCurrentAnimatorStateInfo(layer);
                if (!anim.IsInTransition(layer) && st.normalizedTime >= 1f)
                    break;

                safety -= (Runner != null ? Runner.DeltaTime : Time.deltaTime);
                yield return null;
            }
        }

        // 최종 파괴(서버 권한에서만)
        if (Object != null && Object.IsValid)
        {
            if (Object.HasStateAuthority) DestroySelf();
            else RPC_RequestDespawnAfterImpact();
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestDespawnAfterImpact()
    {
        if (!Object || !Object.IsValid) return;
        if (hasImpacted) DestroySelf();
    }

    private void DestroySelf()
    {
        if (Object != null && Object.IsValid)
            Runner.Despawn(Object);
    }
}
