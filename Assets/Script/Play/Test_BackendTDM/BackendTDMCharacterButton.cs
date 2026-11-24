using UnityEngine;
using UnityEngine.UI;

// 뒤끝 팀데스매치 캐릭터 선택 버튼
public class BackendTDMCharacterButton : MonoBehaviour
{
    [Header("UI 요소")]
    [Header("캐릭터 데이터")]
    [SerializeField] private CharacterInfo characterInfo;  // 이 버튼이 고르는 캐릭터 (CharacterInfo 에셋)
    [SerializeField] private Button button;                // 클릭할 Button
    [SerializeField] private Image iconImage;              // 캐릭터 아이콘 Image
    [SerializeField] private GameObject lockIcon;          // 미보유 캐릭터일 때 켜지는 자물쇠
    [SerializeField] private GameObject selectedFrame;     // 선택됐을 때 켜지는 테두리

    private BackendTDMManager manager;

    public CharacterInfo CharacterInfo => characterInfo;

    public void Bind(BackendTDMManager owner)
    {
        manager = owner;
        if (button == null) button = GetComponent<Button>();
        if (iconImage != null && characterInfo != null) iconImage.sprite = characterInfo.characterIcon;
        if (button != null)
        {
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
        }
        SetSelected(false);
    }

    public void SetLocked(bool locked)
    {
        if (lockIcon != null) lockIcon.SetActive(locked);
        if (iconImage != null) iconImage.color = locked ? new Color(0.35f, 0.35f, 0.35f) : Color.white;
    }

    public void SetInteractable(bool interactable)
    {
        if (button != null) button.interactable = interactable;
    }

    public void SetSelected(bool selected)
    {
        if (selectedFrame != null) selectedFrame.SetActive(selected);
    }

    private void OnClick()
    {
        if (manager != null && characterInfo != null) manager.OnCharacterButtonClicked(characterInfo.characterId);
    }
}
