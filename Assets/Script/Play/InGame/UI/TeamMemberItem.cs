using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// TeamMemberItem : 캐릭터 선택 화면의 팀원 한 칸 (닉네임 / 준비 표시 / 연결 끊김 / 선택한 캐릭터 미리보기)
//   기존 TeamMemberItem 과 같은 함수 구성입니다. (Fusion 의존 제거만)
// ─────────────────────────────────────────────────────────────────────────────

public class TeamMemberItem : MonoBehaviour
{
    [Header("UI 요소")]
    public TMP_Text nicknameText;
    public GameObject readyIndicator;
    public GameObject disconnectIndicator;          // 연결끊김 표시 UI

    [Header("비주얼 표시")]
    public Transform characterVisualRoot;
    public GameObject currentCharacterVisualImage;

    private GameObject currentCharacterVisual;

    public uint SessionId { get; set; }

    public void SetNickname(string nickname)
    {
        if (nicknameText != null) nicknameText.text = nickname;
    }

    public void InitializeDefault()
    {
        SetReady(false);
        SetDisconnected(false);
        if (currentCharacterVisual != null)
        {
            Destroy(currentCharacterVisual);
            currentCharacterVisual = null;
        }
    }

    public void SetDisconnected(bool disconnected)
    {
        if (disconnectIndicator != null && disconnectIndicator.activeSelf != disconnected)
            disconnectIndicator.SetActive(disconnected);
    }

    /// <summary>준비 상태: 텍스트 변경 없이 오브젝트 온/오프</summary>
    public void SetReady(bool isReady)
    {
        if (readyIndicator != null && readyIndicator.activeSelf != isReady)
            readyIndicator.SetActive(isReady);
    }

    public void SetCharacterVisual(GameObject prefab)
    {
        // 같은 프리팹이 이미 표시 중이면 아무 것도 하지 않음 (깜빡임 방지)
        if (currentCharacterVisual != null && prefab != null && currentCharacterVisual.name.StartsWith(prefab.name))
            return;

        ClearCharacterVisual();

        if (prefab != null && characterVisualRoot != null)
        {
            currentCharacterVisual = Instantiate(prefab, characterVisualRoot);
            currentCharacterVisual.transform.localPosition = Vector3.zero;
            currentCharacterVisual.transform.localRotation = Quaternion.identity;
        }
    }

    public void ClearCharacterVisual()
    {
        if (currentCharacterVisualImage != null)
        {
            var image = currentCharacterVisualImage.GetComponent<Image>();
            if (image != null) image.enabled = false;
        }

        if (currentCharacterVisual != null)
        {
            Destroy(currentCharacterVisual);
            currentCharacterVisual = null;
        }
    }
}
