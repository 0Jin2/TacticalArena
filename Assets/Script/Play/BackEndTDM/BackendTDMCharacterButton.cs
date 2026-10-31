using UnityEngine;
using UnityEngine.UI;

// 뒤끝 팀데스매치 캐릭터 선택 버튼
public class BackendTDMCharacterButton : MonoBehaviour
{
    [SerializeField] private CharacterInfo characterInfo;
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private GameObject lockIcon;
    [SerializeField] private GameObject selectedFrame;

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
