using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class ChargeShotProjectile : NetworkBehaviour
{
    public float speed = 15f;
    public float lifeTime = 2f;
    public int damage = 30;
    [Networked] public float direction { get; set; }
    [Networked] public PlayerRef owner { get; set; }

    [Header("히트 이펙트 (공통)")]
    public SpriteRenderer bulletSprite;
    public GameObject fxHit;

    private HashSet<PlayerRef> damagedPlayers = new HashSet<PlayerRef>();
    
    private bool hasImpacted = false;

    public override void Spawned()
    {
        var rb = GetComponent<Rigidbody2D>();
        if (rb != null)
            rb.linearVelocity = Vector2.right * direction * speed;
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

        // ⭐ 환경(땅/벽)은 Health가 없어도 즉시 파괴
        if (layer == LayerMask.NameToLayer("Ground") ||
            layer == LayerMask.NameToLayer("Wall"))
        {
            RPC_OnImpact();
            return;
        }

        // 여기부터는 데미지 낼 수 있는 대상 처리
        var health = other.GetComponent<Health>() ?? other.GetComponentInParent<Health>();
        var targetNO = other.GetComponent<NetworkObject>() ?? other.GetComponentInParent<NetworkObject>();
        if (health == null || targetNO == null)
            return;

        if (health.IsDead)
        {
            var myCol = GetComponent<Collider2D>();
            if (myCol != null) Physics2D.IgnoreCollision(myCol, other, true);
            return;
        }

        // === Player 레이어 ===
        if (layer == LayerMask.NameToLayer("Player"))
        {
            if (targetNO.InputAuthority == owner) return;
            if (damagedPlayers.Contains(targetNO.InputAuthority)) return;

            damagedPlayers.Add(targetNO.InputAuthority);

            var tm = TeamAssigner.Instance;
            if (tm != null &&
                tm.Teams.TryGet(owner, out var ownerTeam) &&
                tm.Teams.TryGet(targetNO.InputAuthority, out var targetTeam) &&
                ownerTeam == targetTeam)
            {
                return;
            }

            health.RPC_ApplyDamage(damage, owner);
            RPC_OnImpact();
            return;
        }

        // === Barrier 레이어 ===
        if (layer == LayerMask.NameToLayer("Barrier"))
        {
            // 자기 방벽이면 무시 (총알 통과)
            if (targetNO.InputAuthority == owner)
            {
                var myCol = GetComponent<Collider2D>();
                if (myCol != null) Physics2D.IgnoreCollision(myCol, other, true);
                return;
            }

            var tm = TeamAssigner.Instance;
            if (tm != null &&
                tm.Teams.TryGet(owner, out var ownerTeam) &&
                tm.Teams.TryGet(targetNO.InputAuthority, out var barrierTeam) &&
                ownerTeam == barrierTeam)
            {
                // 아군 방벽은 히트 이펙트만 내고 소멸
                DestroySelf();
                return;
            }

            // 적 방벽이면 피해
            health.RPC_ApplyDamage(damage, owner);
            RPC_OnImpact();
            return;
        }

        // 기타 Health 오브젝트 처리
        health.RPC_ApplyDamage(damage, owner);
        RPC_OnImpact();
    }

    // 비트리거 충돌(타일/타일맵 콜라이더 등)도 안전하게 파괴
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!Object.HasStateAuthority) return;

        int layer = collision.gameObject.layer;
        if (layer == LayerMask.NameToLayer("Ground") ||
            layer == LayerMask.NameToLayer("Wall"))
        {
            RPC_OnImpact();
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnImpact()
    {
        var rb = GetComponent<Rigidbody2D>();
        var myCol = GetComponent<Collider2D>();
        if (hasImpacted) return;
        hasImpacted = true;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
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

        if (fxHit != null)
        {
            fxHit.SetActive(true);
            StartCoroutine(Co_WaitEffectThenDespawn(fxHit));
        }
        else
        {
            if (Object.HasStateAuthority) DestroySelf();
            else RPC_RequestDespawnAfterImpact();
        }
    }

    private void PlayHitFx()
    {
        if (!Object.HasStateAuthority) return;
        if (hasImpacted) return;
        RPC_OnImpact();
    }

    private IEnumerator Co_WaitEffectThenDespawn(GameObject effectRoot)
    {
        yield return null;

        var anim = effectRoot.GetComponentInChildren<Animator>(true);
        if (anim != null)
        {
            float safety = 10f;
            while (safety > 0f)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                if (!anim.IsInTransition(0) && st.normalizedTime >= 1f) break;
                safety -= (Runner != null ? Runner.DeltaTime : Time.deltaTime);
                yield return null;
            }
        }

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