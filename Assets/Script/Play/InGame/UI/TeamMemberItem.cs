using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// TeamMemberItem : 캐릭터 선택 화면의 팀원 한 칸 (닉네임 / 준비 표시 / 연결 끊김 / 선택한 캐릭터 미리보기)
//   기존 TeamMemberItem 과 같은 함수 구성입니다. (Fusion 의존 제거만)
//
//   ※ 프리팹(팀원 칸) 마다 아래 칸이 비어 있지 않은지 꼭 확인하세요.
//      nicknameText        : 닉네임 글자 (TMP)
//      readyIndicator      : 준비 완료일 때만 켜지는 오브젝트
//      characterVisualRoot : 선택한 캐릭터 UI 프리팹이 생성될 부모 (Image 가 붙어 있어도 됨)
//      currentCharacterVisualImage : (선택) 처음에 보이는 기본 이미지 오브젝트
//   비어 있으면 Console 에 [TeamMemberItem] 경고가 한 번 출력됩니다.
//
//   기본 이미지 규칙
//     - 칸이 만들어졌을 때: 프리팹에 넣어 둔 이미지(비주얼 루트의 Image)가 그대로 보인다.
//     - 처음 캐릭터가 선택돼 캐릭터 UI 가 생기는 순간: 그 이미지의 Source Image 를 비우고 숨긴 뒤
//       자식으로 캐릭터 UI 를 만든다. (이후 캐릭터가 바뀌면 자식만 교체)
//     - 캐릭터 UI 가 위치는 항상 (0, 0, 0)
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
    private string shownVisualKey;                  // 지금 보여 주는 캐릭터 (같으면 다시 만들지 않음)
    private string shownNickname;
    private bool validated;

    // 칸이 만들어졌을 때의 기본 이미지 상태 (되돌릴 때 사용)
    private Image rootImage, placeholderImage;
    private Sprite rootSprite, placeholderSprite;
    private bool rootEnabled, placeholderEnabled;

    public uint SessionId { get; set; }

    private void Awake()
    {
        Validate();
    }

    /// <summary>연결이 빠진 칸이 있으면 자동으로 찾아보고, 그래도 없으면 경고. 기본 이미지 상태도 기억해 둔다.</summary>
    private void Validate()
    {
        if (validated) return;
        validated = true;

        if (nicknameText == null)
        {
            nicknameText = GetComponentInChildren<TMP_Text>(true);
            if (nicknameText == null)
                Debug.LogWarning("[TeamMemberItem] '" + name + "' : nicknameText 가 비어 있습니다. 닉네임이 표시되지 않습니다.", this);
        }
        if (readyIndicator == null)
            Debug.LogWarning("[TeamMemberItem] '" + name + "' : readyIndicator 가 비어 있습니다. 준비 표시가 나오지 않습니다.", this);
        if (characterVisualRoot == null)
            Debug.LogWarning("[TeamMemberItem] '" + name + "' : characterVisualRoot 가 비어 있습니다. 캐릭터 미리보기를 이 칸 자신 아래에 만듭니다.", this);

        // 기본 이미지 기억
        rootImage = characterVisualRoot != null ? characterVisualRoot.GetComponent<Image>() : null;
        placeholderImage = currentCharacterVisualImage != null ? currentCharacterVisualImage.GetComponent<Image>() : null;
        if (rootImage != null) { rootSprite = rootImage.sprite; rootEnabled = rootImage.enabled; }
        if (placeholderImage != null) { placeholderSprite = placeholderImage.sprite; placeholderEnabled = placeholderImage.enabled; }
    }

    public void SetNickname(string nickname)
    {
        Validate();
        if (nickname == null) nickname = string.Empty;
        if (nicknameText != null && shownNickname != nickname)
        {
            nicknameText.text = nickname;
            shownNickname = nickname;
        }
    }

    /// <summary>칸을 처음 상태로. 기본 이미지는 건드리지 않는다 (만들어졌을 때 그대로 보이게)</summary>
    public void InitializeDefault()
    {
        Validate();
        SetReady(false);
        SetDisconnected(false);
        DestroyVisualInstance();
        shownVisualKey = null;
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

    /// <summary>
    /// 선택한 캐릭터 표시.
    ///   uiPrefab 이 있으면: 기본 이미지의 Source Image 를 비우고 숨긴 뒤, 비주얼 루트 자식으로 캐릭터 UI 생성 (위치 0,0,0).
    ///   uiPrefab 이 없고 icon 만 있으면: 기본 이미지 자리에 아이콘을 보여 준다.
    ///   key 가 지금 보여 주는 것과 같으면 아무 것도 하지 않는다 (깜빡임 방지).
    /// </summary>
    public void SetCharacterVisual(string key, GameObject uiPrefab, Sprite icon)
    {
        Validate();
        if (!string.IsNullOrEmpty(key) && key == shownVisualKey) return;

        DestroyVisualInstance();
        shownVisualKey = key;

        if (uiPrefab != null)
        {
            HidePlaceholders();

            Transform root = characterVisualRoot != null ? characterVisualRoot : transform;
            currentCharacterVisual = Instantiate(uiPrefab, root);
            ResetPositionToZero(currentCharacterVisual.transform);
        }
        else if (icon != null)
        {
            Image target = placeholderImage != null ? placeholderImage : rootImage;
            if (target != null)
            {
                target.sprite = icon;
                target.enabled = true;
                if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
            }
        }
    }

    /// <summary>(호환용) 프리팹만 넘기는 기존 방식</summary>
    public void SetCharacterVisual(GameObject prefab)
    {
        SetCharacterVisual(prefab != null ? prefab.name : null, prefab, null);
    }

    /// <summary>선택이 취소돼 캐릭터를 지울 때: 캐릭터 UI 를 없애고 기본 이미지를 되돌린다</summary>
    public void ClearCharacterVisual()
    {
        Validate();
        DestroyVisualInstance();
        shownVisualKey = null;
        RestorePlaceholders();
    }

    // ── 내부 ────────────────────────────────────────────────────────────────
    private void DestroyVisualInstance()
    {
        if (currentCharacterVisual != null)
        {
            Destroy(currentCharacterVisual);
            currentCharacterVisual = null;
        }
    }

    /// <summary>기본 이미지: Source Image 를 비우고 숨긴다 (자식 캐릭터 UI 는 그대로 보인다)</summary>
    private void HidePlaceholders()
    {
        if (rootImage != null)
        {
            rootImage.sprite = null;
            rootImage.enabled = false;
        }
        if (placeholderImage != null && placeholderImage != rootImage)
            placeholderImage.enabled = false;
    }

    private void RestorePlaceholders()
    {
        if (rootImage != null) { rootImage.sprite = rootSprite; rootImage.enabled = rootEnabled; }
        if (placeholderImage != null && placeholderImage != rootImage)
        {
            placeholderImage.sprite = placeholderSprite;
            placeholderImage.enabled = placeholderEnabled;
        }
    }

    /// <summary>자식으로 들어간 캐릭터 UI 의 위치를 항상 (0, 0, 0) 으로 맞춘다</summary>
    private static void ResetPositionToZero(Transform t)
    {
        var rect = t as RectTransform;
        if (rect != null) rect.anchoredPosition3D = Vector3.zero;   // UI: 앵커 기준 위치 0,0,0
        else t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
    }
}
