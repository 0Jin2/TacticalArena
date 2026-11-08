using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// CharacterButton : 캐릭터 선택 화면의 캐릭터 버튼 하나  (기존 CharacterButton 대체)
//   - 기존 CharacterInfo 대신 CharacterDef 를 연결한다.
//   - 눌림 처리와 활성/비활성 판단은 CharSelectUI 가 한다. (이 버튼은 표시 + 클릭 전달만)
//
//   버튼 상태 규칙 (CharSelectUI 가 SetInteractable 로 알려 줌)
//     - 보유하지 않은 캐릭터 : 잠금 아이콘 + 항상 interactable 꺼짐 (고정)
//     - 캐릭터 선택 후 "준비" 상태 : 모든 버튼 interactable 꺼짐
//     - 준비 취소 : 선택한 캐릭터만 빼고 다시 켜짐 (다른 캐릭터로 바꿀 수 있게)
//     - 선택 시간이 끝났거나 시작 전 카운트다운 : 모든 버튼 꺼짐
//
//   ※ interactable 이 꺼질 때 버튼이 투명/회색으로 변하는 문제 방지:
//      "보유 캐릭터"를 일시적으로 끌 때는 Disabled 색을 Normal 색과 똑같이 맞춰서 모양이 변하지 않게 한다.
//      "잠긴 캐릭터"일 때만 lockedTint 색으로 어둡게 보인다.  (Button 의 Transition 이 Color Tint 일 때)
// ─────────────────────────────────────────────────────────────────────────────

public class CharacterButton : MonoBehaviour
{
    public CharacterDef characterDef;
    public Image iconImage;
    public Button button;
    public GameObject lockIcon;
    [Tooltip("(선택) 지금 내가 고른 캐릭터일 때만 켜지는 표시(테두리 등)")]
    public GameObject selectedMark;
    [Tooltip("보유하지 않은 캐릭터를 어둡게 보이게 할 색")]
    public Color lockedTint = new Color(0.45f, 0.45f, 0.45f, 1f);

    private CharSelectUI owner;
    private bool clickable = true;          // 지금 클릭을 받아도 되는가
    private ColorBlock baseColors;
    private bool hasBaseColors;
    private int appliedState = -1;          // 마지막으로 적용한 상태 (같으면 다시 적용하지 않음)

    public string CharacterId { get { return characterDef != null ? characterDef.characterId : string.Empty; } }
    /// <summary>이 캐릭터를 쓸 수 없는가 (보유하지 않음). 튜토리얼에서는 항상 false</summary>
    public bool IsLocked { get; private set; }
    /// <summary>지금 클릭이 가능한 상태인가 (잠금 아님 + 비활성 사유 없음)</summary>
    public bool IsInteractable { get { return !IsLocked && clickable; } }

    public void Setup(CharSelectUI ui)
    {
        owner = ui;
        if (button == null) button = GetComponent<Button>();
        if (characterDef != null && iconImage != null) iconImage.sprite = characterDef.characterIcon;

        if (button != null)
        {
            baseColors = button.colors;
            hasBaseColors = true;
            appliedState = -1;
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
        }
        RefreshLock(ui != null && ui.IsTutorial);
    }

    /// <summary>잠금 상태 다시 확인 (보유 캐릭터 데이터가 늦게 도착할 수 있어 주기적으로 호출됨)</summary>
    public void RefreshLock(bool ignoreLock)
    {
        if (ignoreLock)
        {
            IsLocked = false;
        }
        else if (characterDef == null || UserCharacterDataManager.Instance == null)
        {
            IsLocked = true;                // 데이터가 없으면 안전하게 잠금
        }
        else
        {
            IsLocked = UserCharacterDataManager.Instance.IsCharacterLocked(characterDef.characterId);
        }

        if (lockIcon != null && lockIcon.activeSelf != IsLocked) lockIcon.SetActive(IsLocked);
        ApplyButtonVisual();
    }

    /// <summary>선택 가능 여부를 정한다. 잠긴 캐릭터는 어떤 값을 줘도 항상 꺼진 상태.</summary>
    public void SetInteractable(bool interactable)
    {
        clickable = interactable;
        ApplyButtonVisual();
    }

    /// <summary>내가 고른 캐릭터 표시</summary>
    public void SetSelectedMark(bool selected)
    {
        if (selectedMark != null && selectedMark.activeSelf != selected) selectedMark.SetActive(selected);
    }

    private void ApplyButtonVisual()
    {
        if (button == null) return;

        int state = (IsLocked ? 1 : 0) | (clickable ? 2 : 0);
        if (state == appliedState) return;
        appliedState = state;

        // 모양: 잠긴 캐릭터만 어둡게. 그 외에는 꺼져도 Normal 색 그대로 (투명해지지 않게)
        if (hasBaseColors)
        {
            var colors = baseColors;
            colors.disabledColor = IsLocked ? lockedTint : baseColors.normalColor;
            button.colors = colors;
        }

        bool interactable = IsInteractable;
        if (button.interactable != interactable) button.interactable = interactable;
    }

    private void OnClick()
    {
        if (owner == null || characterDef == null) return;
        if (!clickable || IsLocked) return;
        owner.Select(characterDef.characterId);
    }
}
