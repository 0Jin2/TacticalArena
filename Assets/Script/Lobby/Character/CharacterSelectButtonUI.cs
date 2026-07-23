using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectButtonUI : MonoBehaviour
{
    [Header("연결된 캐릭터 데이터")]
    public CharacterInfo characterInfo;

    [Header("UI 프리팹이 생성될 부모 위치")]
    public Transform uiPrefabRoot; // Inspector에서 지정

    [Header("씬 시작 시 자동 선택 여부")]
    public bool autoSelectOnStart = false;

    public CharacterDisplayData displayData;
    public Button button;
    public GameObject lockIconObj;

    private GameObject currentUIPrefab;
    private CharacterDetailPanelUI detailPanel;

    private void Awake()
    {
        detailPanel = FindObjectOfType<CharacterDetailPanelUI>();
        button.onClick.AddListener(OnClickButton);
    }

    void Start()
    {
        if (displayData != null)
        {
            SetLockState(UserCharacterDataManager.Instance.IsCharacterLocked(displayData.characterDataKey));
        }

        if (autoSelectOnStart)
        {
            // 프레임 한 번 쉰 뒤 실행 (UI 초기화 완료 보장)
            Invoke(nameof(SelectDefault), 0.1f);
        }
    }

    private void SelectDefault()
    {
        OnClickButton();
    }

    public void SetLockState(bool isLocked)
    {
        if (lockIconObj != null)
            lockIconObj.SetActive(isLocked);
    }

    public void OnClickButton()
    {
        if (detailPanel != null && displayData != null)
        {
            detailPanel.ApplyDisplayData(displayData);
            detailPanel.ApplyLockState(UserCharacterDataManager.Instance.IsCharacterLocked(displayData.characterDataKey));
        }

        // 기존 자식 모두 삭제
        if (uiPrefabRoot != null)
        {
            for (int i = uiPrefabRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(uiPrefabRoot.GetChild(i).gameObject);
            }
        }

        // 새 UI 프리팹 생성
        if (characterInfo != null && characterInfo.uiPrefab != null && uiPrefabRoot != null)
        {
            var newUI = Instantiate(characterInfo.uiPrefab, uiPrefabRoot);
            newUI.transform.localPosition = Vector3.zero;
            newUI.transform.localRotation = Quaternion.identity;

            Debug.Log($"✅ 캐릭터 UI 프리팹 생성: {characterInfo.characterName}");
        }
    }

    public void UnlockAndHideLockIcon()
    {
        SetLockState(false);
    }
}