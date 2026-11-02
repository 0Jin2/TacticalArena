using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// ResultMemberItem : 결과 화면의 한 줄 (아이콘 / 닉네임 / 킬 / 데스)
//   기존과 같은 필드·함수 구성입니다. (Fusion 의존 없음)
// ─────────────────────────────────────────────────────────────────────────────

public class ResultMemberItem : MonoBehaviour
{
    [Header("UI")]
    public Image characterIconImage;
    public TMP_Text nicknameText;
    public TMP_Text killText;
    public TMP_Text deathText;

    [Tooltip("(선택) 내 줄이면 이 색으로 닉네임 표시")]
    public bool highlightLocal = true;
    public Color localColor = Color.yellow;

    public void Setup(Sprite icon, string nickname, int kills, int deaths)
    {
        if (characterIconImage != null) characterIconImage.sprite = icon;
        if (nicknameText != null) nicknameText.text = nickname;
        if (killText != null) killText.text = kills.ToString();
        if (deathText != null) deathText.text = deaths.ToString();
    }

    /// <summary>내 줄 강조 (GameHud 가 필요하면 호출)</summary>
    public void SetLocal(bool isLocal)
    {
        if (highlightLocal && isLocal && nicknameText != null) nicknameText.color = localColor;
    }
}
