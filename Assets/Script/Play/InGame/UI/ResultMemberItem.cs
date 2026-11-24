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
    public Image characterIconImage;  // 캐릭터 아이콘
    public TMP_Text nicknameText;     // 닉네임 글자
    public TMP_Text killText;         // 킬 수 글자
    public TMP_Text deathText;        // 데스 수 글자

    public void Setup(Sprite icon, string nickname, int kills, int deaths)
    {
        if (characterIconImage != null) characterIconImage.sprite = icon;
        if (nicknameText != null) nicknameText.text = nickname;
        if (killText != null) killText.text = kills.ToString();
        if (deathText != null) deathText.text = deaths.ToString();
    }

    /// <summary>닉네임 색 (GameHud 가 호출: 나 노랑 / 같은 팀 파랑 / 상대 팀 빨강. 색은 Resources/TeamColorSettings)</summary>
    public void SetNicknameColor(Color color)
    {
        if (nicknameText != null) nicknameText.color = color;
    }
}
