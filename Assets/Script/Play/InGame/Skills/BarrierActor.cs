using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

// ─────────────────────────────────────────────────────────────────────────────
// BarrierActor : 미레이 방벽 프리팹에 붙는 스크립트 (기존 BarrierBehaviour + Health 대체)
//
//  - 맞을 수 있는 설치물(IDamageTarget). 슬롯 번호로 구분한다. (주인, 슬롯)
//  - 체력 계산은 방벽 주인의 기기만 한다. 0이 되면 ShieldSummonSkill 이 모든 기기에 "부서짐"을 알린다.
//  - duration 이 지나면 각 기기가 스스로 부순다.
//  - 프리팹 요구사항: Collider2D(PolygonCollider2D), Animator ("idle"/"die" bool 파라미터, "Spawn" 상태)
//    레이어는 Barrier. 기존 Health / BarrierBehaviour / NetworkObject 컴포넌트는 제거한다.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider2D))]
public class BarrierActor : MonoBehaviour, IDamageTarget
{
    [Header("방벽 수치")]
    [Tooltip("방벽 체력. (기존 방벽 프리팹의 Health → CharacterStatData.maxHP 값과 같게 맞추세요)")]
    public int maxHp = 100;

    [Header("피격 효과음")]
    public AudioClip hitClip;
    public AudioMixerGroup hitMixer;

    [Header("애니메이터")]
    public Animator animator;
    public string spawnStateName = "Spawn";
    public string idleParam = "idle";
    public string dieParam = "die";
    [Tooltip("파괴 애니메이션 길이를 알 수 없을 때 기다리는 시간")]
    public float fallbackDieSeconds = 0.5f;

    public int CurrentHp { get; private set; }

    // ── IDamageTarget ───────────────────────────────────────────────────────
    public uint OwnerSession { get; private set; }
    public byte Slot { get; private set; }
    public NetTeam Team
    {
        get
        {
            var net = InGameNet.Instance;
            var p = net != null ? net.GetPlayer(OwnerSession) : null;
            return p != null ? p.Team : NetTeam.None;
        }
    }
    public DamageTargetKind Kind { get { return DamageTargetKind.Deployable; } }
    public bool IsAlive { get { return !isDying; } }
    public bool IsOwnerLocal { get { var net = InGameNet.Instance; return net != null && net.LocalSessionId == OwnerSession; } }
    public Vector3 Position { get { return transform.position; } }

    private ShieldSummonSkill skill;
    private Collider2D col;
    private bool isDying;
    private bool initialized;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        if (animator == null) animator = GetComponent<Animator>();
    }

    public void Initialize(ShieldSummonSkill origin, uint owner, byte slot, float duration, float facing)
    {
        skill = origin;
        OwnerSession = owner;
        Slot = slot;
        CurrentHp = maxHp;
        initialized = true;

        // 플레이어와 같은 규칙: scale.x = |scale.x| * -방향
        var scale = transform.localScale;
        scale.x = Mathf.Abs(scale.x) * -facing;
        transform.localScale = scale;

        DamageRegistry.Register(this);
        StartCoroutine(Co_Lifetime(duration));
    }

    private IEnumerator Co_Lifetime(float duration)
    {
        yield return new WaitForSeconds(duration);
        Break();
    }

    private void LateUpdate()
    {
        // Spawn 애니메이션이 끝나면 idle 로 전환
        if (animator == null || isDying || string.IsNullOrEmpty(idleParam)) return;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName(spawnStateName) && state.normalizedTime >= 1f && !animator.GetBool(idleParam))
            animator.SetBool(idleParam, true);
    }

    public void ReceiveDamage(DamageInfo info)
    {
        if (!initialized || isDying || info.IsHeal) return;

        if (!info.IsSilent && info.Amount > 0)
        {
            var owner = PlayerEntity.Get(OwnerSession);
            if (owner != null && owner.Audio != null) owner.Audio.PlayOneShot(hitClip, hitMixer);
        }

        if (!IsOwnerLocal) return;      // 체력 계산은 방벽 주인의 기기만

        CurrentHp = Mathf.Max(0, CurrentHp - Mathf.Max(0, info.Amount));
        if (CurrentHp == 0 && skill != null) skill.RequestBreak(Slot);
    }

    /// <summary>방벽을 부순다. (모든 기기에서 호출, 여러 번 불려도 안전)</summary>
    public void Break()
    {
        if (isDying) return;
        isDying = true;

        DamageRegistry.Unregister(this);
        if (skill != null) skill.ForgetBarrier(Slot);
        if (col != null) col.enabled = false;

        if (animator != null)
        {
            if (!string.IsNullOrEmpty(dieParam)) animator.SetBool(dieParam, true);
            if (!string.IsNullOrEmpty(idleParam)) animator.SetBool(idleParam, false);
        }
        StartCoroutine(Co_DestroyAfterDie());
    }

    private IEnumerator Co_DestroyAfterDie()
    {
        float wait = fallbackDieSeconds;
        if (animator != null)
        {
            yield return null;      // die 전이가 반영될 한 프레임
            float length = animator.GetCurrentAnimatorStateInfo(0).length;
            if (length > 0f) wait = length;
        }
        yield return new WaitForSeconds(wait);
        Destroy(gameObject);
    }

    private void OnDestroy()
    {
        DamageRegistry.Unregister(this);
    }
}
