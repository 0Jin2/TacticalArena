using UnityEngine;
using Fusion;
using UnityEngine.EventSystems;

public class PlayerLocalInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("입력 설정")]
    public string horizontalAxis = "Horizontal";

    [Header("입력 캐시")]
    public Vector2 moveInput;
    public bool jumpPressed;
    public bool attackPressed;
    public bool skillPressed;

    private FixedJoystick joystick;
    private bool jumpQueued = false;
    public bool isAttackButtonHeld = false; // Hold 상태 변수 추가

    private void Start()
    {
        joystick = GamePlayUIController.Instance?.GetJoystickComponent();

        var runner = GetComponent<NetworkObject>()?.Runner;
        if (runner != null && !runner.ProvideInput)
        {
            runner.ProvideInput = true;
            Debug.Log("✅ ProvideInput 설정 완료");
        }

        // Jump 버튼 클릭 이벤트 연결
        if (GamePlayUIController.Instance != null && GamePlayUIController.Instance.jumpButton != null)
        {
            GamePlayUIController.Instance.jumpButton.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(OnJumpButtonDown);
        }

        // **공격 버튼 자동 EventTrigger 연결**
        if (GamePlayUIController.Instance != null && GamePlayUIController.Instance.attackButton != null)
        {
            var btnObj = GamePlayUIController.Instance.attackButton;
            var trigger = btnObj.GetComponent<EventTrigger>();
            if (trigger == null) trigger = btnObj.AddComponent<EventTrigger>();

            AddEventTrigger(trigger, EventTriggerType.PointerDown, () => isAttackButtonHeld = true);
            AddEventTrigger(trigger, EventTriggerType.PointerUp, () => isAttackButtonHeld = false);
        }
    }

    private void AddEventTrigger(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener((_) => action());
        trigger.triggers.Add(entry);
    }

    private void Update()
    {
        var netObj = GetComponent<NetworkObject>();
        if (netObj == null || !netObj.HasInputAuthority)
            return;

        if (joystick != null)
        {
            float x = joystick.Horizontal;
            float y = joystick.Vertical;
            if (Mathf.Abs(x) < 0.2f)
                x = 0f;
            if (Mathf.Abs(y) < 0.2f)
                y = 0f;
            moveInput = new Vector2(x, y);
        }
        else
        {
            float x = Input.GetAxisRaw(horizontalAxis);
            float y = Input.GetAxisRaw("Vertical");
            if (Mathf.Abs(x) < 0.2f)
                x = 0f;
            if (Mathf.Abs(y) < 0.2f)
                y = 0f;
            moveInput = new Vector2(x, y);
        }

        // 점프 입력 처리
        if (jumpQueued)
        {
            jumpPressed = true;
            jumpQueued = false;
        }
        else
        {
            jumpPressed = false;
        }

        // ★ 공격/스킬 입력 분리!
        // (예시) 공격과 스킬 버튼이 동시에 눌릴 일 없도록
        attackPressed = isAttackButtonHeld && !skillPressed;
    }

    private void OnJumpButtonDown()
    {
        jumpQueued = true;
    }

    // IPointerDownHandler/IPointerUpHandler 실제 구현 (컴파일 오류 해결)
    public void OnPointerDown(PointerEventData eventData)
    {
        isAttackButtonHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isAttackButtonHeld = false;
    }

    public PlayerInputData GetInputData()
    {
        return new PlayerInputData
        {
            moveX = moveInput.x,
            jump = jumpPressed,
            attack = attackPressed,
            skill = skillPressed
        };
    }
}

public struct PlayerInputData : INetworkInput
{
    public float moveX;
    public bool jump;
    public bool attack;
    public bool skill;
}