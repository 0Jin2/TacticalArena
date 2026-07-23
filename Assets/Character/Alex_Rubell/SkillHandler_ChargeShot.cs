using UnityEngine;
using System.Collections;
using Fusion;

public class SkillHandler_ChargeShot : SkillHandler
{
    [Header("차지샷 이펙트 오브젝트")]
    public GameObject chargeShotEffectObject;   // Inspector에 할당, 초기에는 비활성화

    [Header("프로젝트타일 설정")]
    public GameObject chargeShotProjectilePrefab;
    public Transform projectileSpawnPoint;

    private bool waitingForShoot = false;
    private const float skillKeepTime = 3f;

    public override void TryUseSkill()
    {
        if (!Object.HasInputAuthority) return;
        if (controller == null) return;

        if (isSkillActive)
        {
            CancelSkill();
            return;
        }

        if (!skillCooldownTimer.ExpiredOrNotRunning(Runner)) return;
        if (controller.isUsingSkill) return;

        RPC_StartSkill();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    protected override void RPC_StartSkill()
    {
        if (isSkillActive) return;
        isSkillActive = true;
        skillCooldownTimer = TickTimer.CreateFromSeconds(Runner, statData.skillCooldown);
        StartCoroutine(DoSkill());
    }

    protected override IEnumerator DoSkill()
    {
        waitingForShoot = true;
        controller.SetMovementLock(true);

        // ─── StateAuthority에서만 이펙트 활성화 RPC 호출 ───
        if (Object.HasStateAuthority)
            RPC_ToggleChargeEffect(true);

        float timer = 0f;
        bool fired = false;

        // 공격 버튼 이벤트 임시 변경
        GamePlayUIController.Instance.OverrideAttackButton(() =>
        {
            if (waitingForShoot && !fired)
            {
                fired = true;
                waitingForShoot = false;
                StartCoroutine(DelayedShootCoroutine());
                EndSkill();
            }
        });

        while (waitingForShoot && timer < skillKeepTime)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (waitingForShoot)
        {
            waitingForShoot = false;
            EndSkill();
        }

        // 버튼 이벤트 복원
        GamePlayUIController.Instance.RestoreAttackButton();
    }

    private IEnumerator DelayedShootCoroutine()
    {
        yield return new WaitForSeconds(0.01f);
        ShootChargeShot();
    }

    private void ShootChargeShot()
    {
        var spawnPoint = controller.projectileSpawnPoint;
        if (chargeShotProjectilePrefab != null && spawnPoint != null)
        {
            float baseDir = Mathf.Sign(controller.NetFlipXValue);
            if (Mathf.Abs(baseDir) < 0.01f) baseDir = 1f;

            Vector2 shootDir = Vector2.right * baseDir;
            Quaternion shootRot = Quaternion.FromToRotation(Vector2.right, shootDir);

            Runner.Spawn(
                chargeShotProjectilePrefab,
                spawnPoint.position,
                shootRot,
                inputAuthority: null,
                onBeforeSpawned: (runner, obj) =>
                {
                    var proj = obj.GetComponent<ChargeShotProjectile>();
                    if (proj != null)
                    {
                        proj.owner = controller.Object.InputAuthority;
                        proj.direction = baseDir;
                    }
                }
            );
        }

        if (controller.localInput != null)
            controller.localInput.isAttackButtonHeld = false;
    }

    private void CancelSkill()
    {
        waitingForShoot = false;
        EndSkill();
    }

    protected override void EndSkill()
    {
        isSkillActive = false;
        controller.SetMovementLock(false);

        // ─── StateAuthority에서만 이펙트 비활성화 RPC 호출 ───
        if (Object.HasStateAuthority)
            RPC_ToggleChargeEffect(false);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ToggleChargeEffect(bool active)
    {
        if (chargeShotEffectObject != null)
            chargeShotEffectObject.SetActive(active);
    }

    private void Update()
    {
        if (!Object.HasInputAuthority) return;

        if (!isSkillActive && skillCooldownTimer.IsRunning)
        {
            float timeLeft = skillCooldownTimer.RemainingTime(Runner) ?? 0f;
            float total = statData.skillCooldown;
            float percent = 1f - Mathf.Clamp01(timeLeft / total);
            GamePlayUIController.Instance?.UpdateSkillButtonFill(percent);
        }
        else if (!skillCooldownTimer.IsRunning)
        {
            GamePlayUIController.Instance?.UpdateSkillButtonFill(1f);
        }
    }
}