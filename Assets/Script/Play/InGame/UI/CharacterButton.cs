using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// CharacterButton : 캐릭터 선택 화면의 캐릭터 버튼 하나  (기존 CharacterButton 대체)
//   - 기존 CharacterInfo 대신 CharacterDef 를 연결한다.
//   - 눌림 처리와 활성/비활성 판단은 CharSelectUI 가 한다. (이 버튼은 표시 + 클릭 전달만)
//   - 잠금: UserCharacterDataManager 로 확인. 튜토리얼에서는 항상 해제.
// ─────────────────────────────────────────────────────────────────────────────

public class CharacterButton : MonoBehaviour
{
    public CharacterDef characterDef;
    public Image iconImage;
    public Button button;
    public GameObject lockIcon;

    private CharSelectUI owner;

    public string CharacterId { get { return characterDef != null ? characterDef.characterId : string.Empty; } }
    /// <summary>이 캐릭터를 쓸 수 없는가 (보유하지 않음). 튜토리얼에서는 항상 false</summary>
    public bool IsLocked { get; private set; }

    public void Setup(CharSelectUI ui)
    {
        owner = ui;
        if (button == null) button = GetComponent<Button>();
        if (characterDef != null && iconImage != null) iconImage.sprite = characterDef.characterIcon;

        if (button != null)
        {
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
    }

    public void SetInteractable(bool interactable)
    {
        if (button != null && button.interactable != interactable) button.interactable = interactable;
    }

    private void OnClick()
    {
        if (owner == null || characterDef == null) return;
        if (button != null && !button.interactable) return;
        owner.Select(characterDef.characterId);
    }
}
