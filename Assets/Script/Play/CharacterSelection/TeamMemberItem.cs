using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Fusion;

public class TeamMemberItem : MonoBehaviour
{
    [Header("UI 요소")]
    public TMP_Text nicknameText;
    public GameObject readyIndicator;
    public GameObject disconnectIndicator; // 연결끊김 표시 UI

    [Header("비주얼 표시")]
    public Transform characterVisualRoot;
    private GameObject _currentCharacterVisual;
    public GameObject currentCharacterVisualImage;

    public void SetNickname(string nickname)
    {
        if (nicknameText != null)
            nicknameText.text = nickname;
    }

    public void InitializeDefault()
    {
        SetReady(false);
        SetDisconnected(false);
        // 기존 미리보기 정리
        if (_currentCharacterVisual != null)
        {
            Destroy(_currentCharacterVisual);
            _currentCharacterVisual = null;
        }
    }

    public void SetDisconnected(bool disconnected)
    {
        if (disconnectIndicator != null)
            disconnectIndicator.SetActive(disconnected);
    }

    // ✅ 준비 상태: 텍스트 변경 X, 오브젝트 온/오프
    public void SetReady(bool isReady)
    {
        if (readyIndicator != null)
            readyIndicator.SetActive(isReady);
    }

    public void SetCharacterVisual(GameObject prefab)
    {
        // 이미 같은 프리팹이 표시 중이면 아무 것도 하지 않음 (깜빡임 방지)
        if (_currentCharacterVisual != null && prefab != null)
        {
            // 인스턴스는 (Clone)이 붙으므로 이름 비교로 빠르게 체크
            // ex) "TitanUIPrefab" vs "TitanUIPrefab(Clone)"
            if (_currentCharacterVisual.name.StartsWith(prefab.name))
                return;
        }

        ClearCharacterVisual();

        if (prefab != null && characterVisualRoot != null)
        {
            _currentCharacterVisual = Instantiate(prefab, characterVisualRoot);
            _currentCharacterVisual.transform.localPosition = Vector3.zero;
            _currentCharacterVisual.transform.localRotation = Quaternion.identity;
        }
    }

    public void ClearCharacterVisual()
    {
        if (currentCharacterVisualImage != null)
        {
            var image = currentCharacterVisualImage.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = false;
            }
        }

        if (_currentCharacterVisual != null)
        {
            Destroy(_currentCharacterVisual);
            _currentCharacterVisual = null;
        }
    }
}
