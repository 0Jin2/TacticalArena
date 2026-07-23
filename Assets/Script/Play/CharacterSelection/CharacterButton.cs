using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Fusion;

public class CharacterButton : MonoBehaviour
{
    public CharacterInfo characterInfo;
    public Image iconImage;
    public Button button;
    public GameObject lockIcon;

    private CharacterSelectionManager selectionManager;

    private void Start()
    {
        selectionManager = FindObjectOfType<CharacterSelectionManager>();

        if (characterInfo != null && iconImage != null)
        {
            iconImage.sprite = characterInfo.characterIcon;
        }

        //UserCharaxterDataManager.cs의 뒤끝 콘솔의 캐릭터 데이터 조회 후에 버튼 활성화 여부 결정
        ApplyLockState();
        button.onClick.AddListener(OnClick);
    }

    private void ApplyLockState()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Tutorial"))
        {
            // 튜토리얼에서는 항상 모든 캐릭터 버튼을 잠금 해제
            if (button != null) button.interactable = true;
            if (lockIcon != null) lockIcon.SetActive(false);
            return;
        }

        if (characterInfo == null || UserCharacterDataManager.Instance == null || button == null)
        {
            if (button != null) button.interactable = false;
            return;
        }

        bool isLocked = UserCharacterDataManager.Instance.IsCharacterLocked(characterInfo.characterId);

        button.interactable = !isLocked;

        if (lockIcon != null)
        {
            lockIcon.SetActive(isLocked);
        }
    }

    public void OnClick()
    {
        var myNetworkData = CharacterSelectionNetworkData.Local;
        if (myNetworkData == null || myNetworkData.Object == null) return;

        var player = CharacterSelectionNetworkData.Instance.Runner.LocalPlayer;
        if (!button.interactable)
        {
            Debug.LogWarning("⛔ 버튼 비활성화 상태. 클릭 무시됨.");
            return;
        }

        if (CharacterSelectionNetworkData.Instance.ReadyStates.TryGet(player, out bool isReady) && isReady)
        {
            Debug.LogWarning("❌ 준비 완료 상태에서는 캐릭터 선택 불가");
            return;
        }

        Debug.Log($"🎯 캐릭터 선택 요청: {characterInfo.characterId}");

        // ✅ 내가 누른 버튼만 즉시 잠금
        button.interactable = false;

        // 🛰 서버에 선택 저장 요청 (실제 반영은 서버에 따라 다음 틱에 반영됨)
        myNetworkData.RequestSelectCharacter(characterInfo.characterId);
    }
}