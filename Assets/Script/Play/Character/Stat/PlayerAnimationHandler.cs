using UnityEngine;
using Fusion;

[RequireComponent(typeof(NetworkMecanimAnimator))]
public class PlayerAnimationHandler : NetworkBehaviour
{
    private NetworkMecanimAnimator netAnimator;
    private Animator animator;
    private Rigidbody2D rb;

    [Networked] public float moveX { get; private set; }
    [Networked] public bool isGrounded { get; private set; }

    public override void Spawned()
    {
        netAnimator = GetComponent<NetworkMecanimAnimator>();
        animator = netAnimator.Animator;
        rb = GetComponent<Rigidbody2D>();
    }

    public void SetMoveState(float moveXValue, bool grounded)
    {
        if (!Object.HasStateAuthority)
            return; // 🔑 서버에서만 실행

        moveX = moveXValue;
        isGrounded = grounded;

        if (netAnimator != null)
        {
            netAnimator.Animator.SetFloat("moveX", moveXValue);
            netAnimator.Animator.SetBool("grounded", grounded);

            // Rigidbody2D의 y속도를 파라미터로 넘긴다
            float verticalVel = rb != null ? rb.velocity.y : 0f;
            netAnimator.Animator.SetFloat("verticalVelocity", verticalVel);
        }
    }

    public void PlayJumpTrigger()
    {
        if (Object.HasStateAuthority)
        {
            netAnimator.SetTrigger("jump");
        }
    }

    public void SetShootState(bool isShooting)
    {
        if (Object.HasStateAuthority)
        {
            netAnimator.Animator.SetBool("shoot", isShooting);
        }
    }

    public void PauseJumpAnim()
    {
        // animator.speed는 점프 등 특정 시점에만 제한적으로 건드림
        if (Object.HasStateAuthority && animator != null)
            animator.speed = 0f;
    }

    private bool IsGrounded()
    {
        Vector2 pos = transform.position;
        Vector2 size = new Vector2(0.5f, 0.1f);
        RaycastHit2D hit = Physics2D.BoxCast(pos, size, 0f, Vector2.down, 0.1f, LayerMask.GetMask("Ground"));
        Debug.DrawRay(pos, Vector2.down * 0.1f, hit.collider ? Color.green : Color.red);
        return hit.collider != null;
    }

    public void PlayDieTrigger()
    {
        if (Object.HasStateAuthority && netAnimator != null)
        {
            netAnimator.SetTrigger("die");
            // 🔒 사망 트리거와 동시에 사격 상태/속도 등을 즉시 해제
            var a = netAnimator.Animator;
            if (a != null)
            {
                a.SetBool("shoot", false);
                a.SetFloat("moveX", 0f);
            }
        }
    }

    public void ResetDieState()
    {
        // ✅ StateAuthority에서 네트워크 파라미터로 복구해야 모든 클라가 동일하게 깨어남
        if (!Object.HasStateAuthority || netAnimator == null) return;

        // 1) 'respawn' 트리거를 네트워크로 발행 → Die 상태에서 빠져나오는 전이를 Animator에 구성
        netAnimator.SetTrigger("respawn");

        // 2) 파라미터 정상화(이 값들은 NetworkMecanimAnimator가 복제함)
        var a = netAnimator.Animator;
        a.ResetTrigger("die");           // 로컬 트리거 정리
        a.SetBool("shoot", false);
        a.SetBool("grounded", true);
        a.SetFloat("moveX", 0f);
        a.SetFloat("verticalVelocity", 0f);
    }
}