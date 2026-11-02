using System;
using UnityEngine;
using UnityEngine.EventSystems;

// ─────────────────────────────────────────────────────────────────────────────
// HoldButton : "누르고 있는 동안" 을 감지하는 화면 버튼 (공격 버튼용)
//   기존에는 EventTrigger(PointerDown/Up)를 코드로 붙였던 부분을 이 컴포넌트로 대체.
//   GameHud 가 attackButton 에 자동으로 붙여 주므로 직접 붙일 필요는 없다.
//   - Image 등 Raycast Target 이 켜진 UI 위에 있어야 터치를 받는다.
// ─────────────────────────────────────────────────────────────────────────────

public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("손가락이 버튼 밖으로 나가면 누름을 해제할지")]
    public bool releaseOnExit = false;

    public bool IsHeld { get; private set; }
    public event Action Pressed;
    public event Action Released;

    public static HoldButton GetOrAdd(GameObject target)
    {
        var hold = target.GetComponent<HoldButton>();
        return hold != null ? hold : target.AddComponent<HoldButton>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsHeld) return;
        IsHeld = true;
        if (Pressed != null) Pressed();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        Release();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (releaseOnExit) Release();
    }

    private void OnDisable()
    {
        Release();              // 버튼이 숨겨질 때 눌린 채로 남지 않게
    }

    private void Release()
    {
        if (!IsHeld) return;
        IsHeld = false;
        if (Released != null) Released();
    }
}
