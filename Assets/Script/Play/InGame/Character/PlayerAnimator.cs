using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerAnimator : Animator 파라미터를 한곳에서 다루는 래퍼 (기존 PlayerAnimationHandler 대체)
//
//  Animator Controller 에 있어야 하는 파라미터 (없는 것은 조용히 건너뜀):
//    moveX(float) grounded(bool) verticalVelocity(float) shoot(bool)
//    jump(trigger) die(trigger) respawn(trigger)
//
//  내 캐릭터는 Motor/Combat 이 값을 넣고, 남의 캐릭터는 PlayerNetSync 가 받은 값을 넣는다.
//  → 포톤의 NetworkMecanimAnimator 없이도 모든 기기에서 같은 동작을 보여 준다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerAnimator : MonoBehaviour
{
    [Header("대상 Animator (비우면 자신/자식에서 자동 검색)")]
    public Animator animator;

    [Header("파라미터 이름 (Animator Controller 와 같아야 함)")]
    public string moveXParam = "moveX";
    public string groundedParam = "grounded";
    public string verticalVelocityParam = "verticalVelocity";
    public string shootParam = "shoot";
    public string jumpTrigger = "jump";
    public string dieTrigger = "die";
    public string respawnTrigger = "respawn";

    private readonly HashSet<string> existing = new HashSet<string>();
    private float baseSpeed = 1f;

    public void Setup(PlayerEntity owner)
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator == null) { NetLog.Warn("PlayerAnimator: Animator 를 찾지 못했습니다. " + gameObject.name); return; }

        baseSpeed = animator.speed;
        existing.Clear();
        foreach (var p in animator.parameters) existing.Add(p.name);
    }

    public float Speed
    {
        get { return animator != null ? animator.speed : 1f; }
        set { if (animator != null) animator.speed = value; }
    }

    public float BaseSpeed { get { return baseSpeed; } }

    private bool Has(string name) { return animator != null && !string.IsNullOrEmpty(name) && existing.Contains(name); }

    public void SetLocomotion(float moveX, bool grounded, float verticalVelocity)
    {
        if (Has(moveXParam)) animator.SetFloat(moveXParam, moveX);
        if (Has(groundedParam)) animator.SetBool(groundedParam, grounded);
        if (Has(verticalVelocityParam)) animator.SetFloat(verticalVelocityParam, verticalVelocity);
    }

    public void SetShoot(bool shooting)
    {
        if (Has(shootParam)) animator.SetBool(shootParam, shooting);
    }

    public void PlayJump()
    {
        if (Has(jumpTrigger)) animator.SetTrigger(jumpTrigger);
    }

    public void PlayDie()
    {
        if (animator == null) return;
        if (Has(dieTrigger)) animator.SetTrigger(dieTrigger);
        // 사망과 동시에 사격/이동 상태 해제
        if (Has(shootParam)) animator.SetBool(shootParam, false);
        if (Has(moveXParam)) animator.SetFloat(moveXParam, 0f);
    }

    public void ResetDie()
    {
        if (animator == null) return;
        if (Has(dieTrigger)) animator.ResetTrigger(dieTrigger);
        if (Has(respawnTrigger)) animator.SetTrigger(respawnTrigger);
        if (Has(shootParam)) animator.SetBool(shootParam, false);
        if (Has(groundedParam)) animator.SetBool(groundedParam, true);
        if (Has(moveXParam)) animator.SetFloat(moveXParam, 0f);
        if (Has(verticalVelocityParam)) animator.SetFloat(verticalVelocityParam, 0f);
    }
}
