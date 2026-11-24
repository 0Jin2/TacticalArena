using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// CharacterButton : 캐릭터 선택 화면의 캐릭터 버튼 하나  (기존 CharacterButton 대체)
//   - 기존 CharacterInfo 대신 CharacterDef 를 연결한다.
//   - 고를 수 있는지 판단은 CharSelectUI 가 한다. (이 버튼은 표시 + 클릭 전달만)
//
//   버튼 상태 규칙
//     - 버튼은 항상 터치를 받는다. (interactable 을 끄지 않는다)
//     - 고를 수 없는 캐릭터는 버튼 전체(아이콘/테두리/글자)를 lockedTint 색으로 어둡게 칠한다.
//       누르면 선택은 안 되고, CharSelectUI 가 이유를 알림으로 띄운다.
//         보유하지 않음       → "캐릭터를 보유하고 있지 않습니다."   (잠금 아이콘도 켜짐)
//         팀원이 사용 중(랭크) → "팀원이 캐릭터를 이미 사용중입니다."
//         준비 완료 상태      → "준비를 취소하면 바꿀 수 있습니다"
//         선택 시간 종료      → "캐릭터 선택 시간이 끝났습니다."
//     - 내가 고른 캐릭터는 selectedMark 를 켠다. (준비 완료 전에는 밝게, 준비 완료 후에는 모두와 같이 어둡게)
//     - 잠금 아이콘(lockIcon)과 선택 표시(selectedMark)는 어둡게 칠하지 않는다. (잘 보이도록)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>캐릭터를 고를 수 없는 이유</summary>
public enum CharacterBlockReason
{
    None = 0,           // 고를 수 있음
    NotOwned = 1,       // 보유하지 않은 캐릭터
    TeammateUsing = 2,  // 랭크전: 같은 팀원이 이미 사용 중
    ReadyLocked = 3,    // 준비 완료 상태라 변경 불가
    TimeOver = 4,       // 선택 시간이 끝남 (마지막 1초 / 시작 카운트다운)
}

public class CharacterButton : MonoBehaviour
{
    [Header("캐릭터 데이터")]
    public CharacterDef characterDef;  // 이 버튼이 고르는 캐릭터 (CharacterDef 에셋)

    [Header("UI 요소")]
    public Image iconImage;          // 캐릭터 아이콘을 띄울 Image
    public Button button;            // 클릭할 Button
    public GameObject lockIcon;      // 미보유 캐릭터일 때 켜지는 자물쇠
    [Tooltip("(선택) 지금 내가 고른 캐릭터일 때만 켜지는 표시(테두리 등)")]
    public GameObject selectedMark;  // (선택) 내가 고른 캐릭터일 때 켜지는 표시

    [Header("색상")]
    [Tooltip("고를 수 없는 캐릭터(미보유 / 팀원 사용 중 / 준비 완료 등)를 어둡게 보이게 할 색. 버튼 안 모든 이미지와 글자에 곱해집니다.")]
    public Color lockedTint = new Color(0.45f, 0.45f, 0.45f, 1f);  // 고를 수 없는 캐릭터를 어둡게 칠할 색

    private CharSelectUI owner;
    private CharacterBlockReason externalReason = CharacterBlockReason.None;   // CharSelectUI 가 알려 준 이유
    private readonly List<Graphic> dimTargets = new List<Graphic>();           // 어둡게 칠할 이미지/글자
    private readonly List<Color> dimBaseColors = new List<Color>();            // 원래 색
    private bool dimTargetsReady;
    private bool dimApplied;

    public string CharacterId { get { return characterDef != null ? characterDef.characterId : string.Empty; } }
    /// <summary>이 캐릭터를 쓸 수 없는가 (보유하지 않음). 튜토리얼에서는 항상 false</summary>
    public bool IsLocked { get; private set; }
    /// <summary>지금 고를 수 없는 이유. 보유하지 않음이 가장 우선</summary>
    public CharacterBlockReason BlockReason { get { return IsLocked ? CharacterBlockReason.NotOwned : externalReason; } }
    /// <summary>지금 고를 수 있는가</summary>
    public bool CanSelect { get { return BlockReason == CharacterBlockReason.None; } }

    public void Setup(CharSelectUI ui)
    {
        owner = ui;
        if (button == null) button = GetComponent<Button>();
        if (characterDef != null && iconImage != null) iconImage.sprite = characterDef.characterIcon;

        if (button != null)
        {
            button.interactable = true;                 // 터치는 항상 받는다 (고를 수 없으면 알림)
            button.onClick.RemoveListener(OnClick);
            button.onClick.AddListener(OnClick);
        }
        CollectDimTargets();
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

    /// <summary>[CharSelectUI] 보유 여부 외에 고를 수 없는 이유를 알려 준다. (None = 고를 수 있음)</summary>
    public void SetBlock(CharacterBlockReason reason)
    {
        if (reason == CharacterBlockReason.NotOwned) reason = CharacterBlockReason.None;   // 보유 여부는 버튼이 직접 판단
        if (externalReason == reason) return;
        externalReason = reason;
        ApplyButtonVisual();
    }

    /// <summary>내가 고른 캐릭터 표시</summary>
    public void SetSelectedMark(bool selected)
    {
        if (selectedMark != null && selectedMark.activeSelf != selected) selectedMark.SetActive(selected);
    }

    /// <summary>버튼 안의 이미지/글자를 모아 원래 색을 기억한다. (잠금 아이콘, 선택 표시는 제외)</summary>
    private void CollectDimTargets()
    {
        if (dimTargetsReady) return;
        dimTargetsReady = true;

        foreach (var graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null) continue;
            if (lockIcon != null && graphic.transform.IsChildOf(lockIcon.transform)) continue;
            if (selectedMark != null && graphic.transform.IsChildOf(selectedMark.transform)) continue;
            dimTargets.Add(graphic);
            dimBaseColors.Add(graphic.color);
        }
    }

    private void ApplyButtonVisual()
    {
        if (button != null && !button.interactable) button.interactable = true;    // 다른 곳에서 꺼도 다시 켠다
        if (!dimTargetsReady) return;

        bool dim = !CanSelect;
        if (dim == dimApplied) return;
        dimApplied = dim;

        for (int i = 0; i < dimTargets.Count; i++)
        {
            var graphic = dimTargets[i];
            if (graphic == null) continue;
            Color baseColor = dimBaseColors[i];
            graphic.color = dim
                ? new Color(baseColor.r * lockedTint.r, baseColor.g * lockedTint.g, baseColor.b * lockedTint.b, baseColor.a * lockedTint.a)
                : baseColor;
        }
    }

    private void OnClick()
    {
        if (owner == null || characterDef == null) return;

        var reason = BlockReason;
        if (reason != CharacterBlockReason.None)
        {
            owner.ShowBlockedNotice(reason);       // 선택은 하지 않고 이유만 알려 준다
            return;
        }
        owner.Select(characterDef.characterId);
    }
}
