using System.Collections;
using UnityEngine;
using Fusion;

[RequireComponent(typeof(Health), typeof(PolygonCollider2D), typeof(Animator))]
public class BarrierBehaviour : NetworkBehaviour
{
    [Networked] public PlayerRef owner { get; private set; }
    [Networked] private bool isFlipped { get; set; }
    public float duration = 3f;

    private Animator animator;
    private PolygonCollider2D PolygonCollider;
    private Health health;
    private bool isDying = false;
    private float originalScaleX;

    public void Initialize(PlayerRef ownerRef, float lifeTime, bool flip)
    {
        owner = ownerRef;
        duration = lifeTime;
        isFlipped = flip;              // ← 초기화 시 flip 값 설정
    }

    public override void Spawned()
    {
        animator = GetComponent<Animator>();
        PolygonCollider = GetComponent<PolygonCollider2D> ();

        health = GetComponent<Health>();
        health.OnDeath += OnHealthDeath;

        originalScaleX = transform.localScale.x;
        transform.localScale = new Vector3(
            Mathf.Abs(originalScaleX) * (isFlipped ? +1f : -1f),
            transform.localScale.y,
            transform.localScale.z
        );

        if (Object.HasStateAuthority)
            StartCoroutine(DestroyAfterDuration());
    }

    private void OnHealthDeath()
    {
        if (isDying) return;
        if (Object.HasStateAuthority)
            StartDie();
    }

    private IEnumerator DestroyAfterDuration()
    {
        yield return new WaitForSeconds(duration);
        if (Object.HasStateAuthority && !isDying)
        {
            // ▶ 남은 HP만큼 데미지 입혀서 OnDeath 강제 발생
            health.ForceKillWithoutFeedback(owner);
        }
    }

    private void LateUpdate()
    {
        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName("Spawn")
            && state.normalizedTime >= 1f
            && !animator.GetBool("idle")
            && !isDying)
        {
            animator.SetBool("idle", true);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isDying) return;

        var proj = other.GetComponent<Projectile>();
        if (proj == null) return;

        // 같은 팀 총알 무시
        var teams = TeamAssigner.Instance;
        if (teams != null
            && teams.Teams.TryGet(owner, out var oTeam)
            && teams.Teams.TryGet(proj.owner, out var pTeam)
            && oTeam == pTeam)
            return;

        if (Object.HasStateAuthority)
        {
            health.TakeDamage(proj.damage, proj.owner);
            Runner.Despawn(proj.Object);
        }
    }

    private void StartDie()
    {
        isDying = true;
        animator.SetBool("die", true);
        animator.SetBool("idle", false);
        PolygonCollider.enabled = false;   // ▶ die 시에만 비활성화
        StartCoroutine(WaitDieAndDestroy());
    }

    private IEnumerator WaitDieAndDestroy()
    {
        yield return new WaitForSeconds(
            animator.GetCurrentAnimatorStateInfo(0).length
        );
        if (Object != null && Object.IsValid)
            Runner.Despawn(Object);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        health.OnDeath -= OnHealthDeath;
    }
}