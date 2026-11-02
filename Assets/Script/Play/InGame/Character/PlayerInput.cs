using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerInput : "내" 캐릭터의 입력 읽기 (조이스틱/버튼 → 값)
//  - 화면 UI(GameHud)의 버튼 이벤트와 조이스틱을 읽어 Motor/Combat 이 가져다 쓴다.
//  - PC 에디터에서는 키보드로도 테스트: 이동 A/D·방향키, 점프 W/Space, 공격 J, 스킬 K, 재장전 R
//  - 남의 캐릭터에서는 아무것도 하지 않는다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerInput : MonoBehaviour
{
    [Header("입력 설정")]
    [Range(0f, 0.9f)] public float deadZone = 0.2f;
    public bool useKeyboardInEditor = true;

    public Vector2 MoveInput { get; private set; }
    public bool AttackHeld { get; private set; }
    public bool AttackDown { get; private set; }          // 이번 프레임에 막 눌림

    private PlayerEntity entity;
    private GameHud hud;
    private FixedJoystick joystick;
    private bool jumpLatched;
    private bool prevAttackHeld;
    private bool attackSuppressed;

    public void Setup(PlayerEntity owner)
    {
        entity = owner;
        if (!entity.IsMine) { enabled = false; return; }

        hud = GameHud.Instance;
        if (hud == null) { NetLog.Warn("PlayerInput: GameHud 가 씬에 없습니다. (조이스틱/버튼 사용 불가)"); return; }

        joystick = hud.GetJoystick();
        hud.OnJumpClicked += HandleJump;
        hud.OnSkillClicked += HandleSkill;
        hud.OnReloadClicked += HandleReload;
    }

    private void OnDestroy()
    {
        if (hud == null) return;
        hud.OnJumpClicked -= HandleJump;
        hud.OnSkillClicked -= HandleSkill;
        hud.OnReloadClicked -= HandleReload;
    }

    private void Update()
    {
        if (entity == null || !entity.IsMine) return;

        if (!entity.ControlEnabled)
        {
            ClearHeld();
            return;
        }

        // 1) 이동: 조이스틱 → (없으면) 키보드
        Vector2 move = Vector2.zero;
        if (joystick != null && joystick.gameObject.activeInHierarchy)
            move = new Vector2(joystick.Horizontal, joystick.Vertical);

#if UNITY_EDITOR || UNITY_STANDALONE
        if (useKeyboardInEditor && move == Vector2.zero)
        {
            move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W)) jumpLatched = true;
            if (Input.GetKeyDown(KeyCode.K)) HandleSkill();
            if (Input.GetKeyDown(KeyCode.R)) HandleReload();
        }
#endif
        if (Mathf.Abs(move.x) < deadZone) move.x = 0f;
        if (Mathf.Abs(move.y) < deadZone) move.y = 0f;
        MoveInput = move;

        // 2) 공격 (버튼 누르고 있는 동안)
        bool held = hud != null && hud.AttackHeld;
#if UNITY_EDITOR || UNITY_STANDALONE
        if (useKeyboardInEditor && Input.GetKey(KeyCode.J)) held = true;
#endif
        // 차지샷 등으로 공격 입력을 소비한 뒤에는, 버튼을 한 번 뗄 때까지 기본 공격이 나가지 않게 한다.
        if (attackSuppressed)
        {
            if (!held) attackSuppressed = false;
            else held = false;
        }
        AttackHeld = held;
        AttackDown = held && !prevAttackHeld;
        prevAttackHeld = held;
    }

    /// <summary>점프 입력이 있었는지 확인하고 소비한다. (물리 프레임에서 놓치지 않도록 래치)</summary>
    public bool ConsumeJump()
    {
        bool value = jumpLatched;
        jumpLatched = false;
        return value;
    }

    /// <summary>지금 눌려 있는 공격 버튼을 뗄 때까지 공격 입력을 무시한다.</summary>
    public void SuppressAttackUntilRelease()
    {
        attackSuppressed = true;
        AttackHeld = false;
        AttackDown = false;
    }

    /// <summary>눌려 있던 입력을 모두 해제 (사망/스킬 전환/연결 끊김)</summary>
    public void ClearHeld()
    {
        MoveInput = Vector2.zero;
        AttackHeld = false;
        AttackDown = false;
        prevAttackHeld = false;
        jumpLatched = false;
    }

    private void HandleJump() { if (entity != null && entity.ControlEnabled) jumpLatched = true; }
    private void HandleSkill() { if (entity != null && entity.Skill != null) entity.Skill.TryUse(); }
    private void HandleReload() { if (entity != null && entity.Combat != null) entity.Combat.TryReload(); }
}
