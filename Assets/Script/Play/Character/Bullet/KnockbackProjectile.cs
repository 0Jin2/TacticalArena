using System.Collections;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class KnockbackProjectile : NetworkBehaviour
{
    [Header("총알 스탯")]
    public float speed = 30f;
    public float lifeTime = 3f;
    public int damage = 10;

    [Header("넉백 설정")]
    public float knockbackForce = 25f;
    public float knockbackUpBias = 0.4f;

    [Header("히트 이펙트 (플레이어용)")]
    public SpriteRenderer bulletSprite;
    public GameObject fxHitPlayer;

    private Rigidbody2D rb;
    [Networked] public float direction { get; set; }
    [Networked] public PlayerRef owner { get; set; }

    private readonly HashSet<PlayerRef> damagedPlayers = new HashSet<PlayerRef>();
    private bool hasImpacted = false;

    public override void Spawned()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.velocity = transform.right * speed;
        Invoke(nameof(DestroySelf), lifeTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!Object.HasStateAuthority) return;

        int layer = other.gameObject.layer;

        // 총알/경계/시체 충돌 무시
        if (layer == LayerMask.NameToLayer("Bullet") ||
            layer == LayerMask.NameToLayer("Confiner") ||
            layer == LayerMask.NameToLayer("DeadBody"))
            return;

        // ✅ 땅/벽과는 부딪혀도 파괴하지 않음(그냥 통과/스치도록 무시 처리만 선택적으로 수행)
        if (layer == LayerMask.NameToLayer("Ground") ||
            layer == LayerMask.NameToLayer("Wall"))
        {
            // 필요 시 한 프레임만 충돌 무시(스치듯 지나가게)
            var myCol = GetComponent<Collider2D>();
            if (myCol != null) Physics2D.IgnoreCollision(myCol, other, true);
            return;
        }

        // 데미지 대상 검색
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
            // 아군/자기 자신은 무시
            if (targetNO.InputAuthority == owner) return;
            if (damagedPlayers.Contains(targetNO.InputAuthority)) return;

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
            ApplyKnockback(other);
            RPC_OnImpact();
            return;
        }

        // === Barrier 레이어 ===
        if (layer == LayerMask.NameToLayer("Barrier"))
        {
            // 자기 방벽이면 통과
            if (targetNO.InputAuthority == owner)
            {
                var myCol = GetComponent<Collider2D>();
                if (myCol != null) Physics2D.IgnoreCollision(myCol, other, true);
                return;
            }

            var tm = TeamAssigner.Instance;
            if (tm != null &&
                tm.Teams.TryGet(owner, out var ownerTeamB) &&
                tm.Teams.TryGet(targetNO.InputAuthority, out var barrierTeam) &&
                ownerTeamB == barrierTeam)
            {
                // 아군 방벽이면 소멸(혹은 통과로 바꾸고 싶으면 return 처리)
                DestroySelf();
                return;
            }

            // 적 방벽: 피해 주고 소멸
            health.RPC_ApplyDamage(damage, owner);
            DestroySelf();
            return;
        }

        // 기타 Health 오브젝트: 피해 주고 소멸
        health.RPC_ApplyDamage(damage, owner);
        DestroySelf();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OnImpact()
    {
        var myCol = GetComponent<Collider2D>();
        if (hasImpacted) return;
        hasImpacted = true;

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

        if (fxHitPlayer != null)
        {
            fxHitPlayer.SetActive(true);
            StartCoroutine(Co_WaitEffectThenDespawn(fxHitPlayer));
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

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!Object.HasStateAuthority) return;

        int layer = collision.gameObject.layer;
        if (layer == LayerMask.NameToLayer("Ground") || layer == LayerMask.NameToLayer("Wall"))
            return;
    }

    private void ApplyKnockback(Collider2D other)
    {
        var controller = other.GetComponentInParent<PlayerNetworkController>();
        if (controller == null) return;

        // 총알 진행 방향 그대로
        Vector2 bulletDir = rb != null && rb.velocity.sqrMagnitude > 0.0001f
            ? rb.velocity.normalized
            : ((Vector2)other.bounds.center - (Vector2)transform.position).normalized;

        controller.RPC_ApplyKnockback(bulletDir, knockbackForce, knockbackUpBias, 0.2f, /*opposite=*/false);
    }

    private void DestroySelf()
    {
        if (Object != null && Object.IsValid)
            Runner.Despawn(Object);
    }
}
