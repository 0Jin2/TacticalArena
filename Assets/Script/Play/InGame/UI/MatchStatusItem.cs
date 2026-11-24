using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// MatchStatusItem : 화면 위쪽 팀 현황판의 한 칸 (닉네임 / 캐릭터 아이콘 / 킬 / 데스 / 사망 표시)
//   (기존 MatchStatusItem 대체. PlayerRef 대신 세션 번호(uint)로 식별)
// ─────────────────────────────────────────────────────────────────────────────

public class MatchStatusItem : MonoBehaviour
{
    [Header("UI 요소")]
    public TMP_Text nicknameText;      // 닉네임 글자
    public Image characterIconImage;   // 캐릭터 아이콘
    public TMP_Text killText;          // 킬 수 글자
    public TMP_Text deathText;         // 데스 수 글자
    public GameObject deadOverlay;     // 죽었을 때 덮이는 표시
    [Tooltip("(선택) 연결이 끊긴 유저일 때 켜지는 표시")]
    public GameObject offlineOverlay;  // (선택) 연결이 끊긴 유저 표시

    public uint SessionId { get; private set; }

    /// <summary>칸 초기화. 닉네임 색: 나 노랑 / 같은 팀 파랑 / 상대 팀 빨강 (색은 Resources/TeamColorSettings)</summary>
    public void Initialize(NetPlayer player, Sprite icon, bool isLocal)
    {
        SessionId = player.SessionId;

        if (nicknameText != null)
        {
            nicknameText.text = player.Nickname;
            nicknameText.color = TeamView.ColorOf(isLocal, player.Team);
        }
        if (characterIconImage != null) characterIconImage.sprite = icon;
        if (killText != null) killText.text = "0";
        if (deathText != null) deathText.text = "0";
        if (deadOverlay != null) deadOverlay.SetActive(false);
        if (offlineOverlay != null) offlineOverlay.SetActive(false);
    }

    public void UpdateKillDeath(int kills, int deaths)
    {
        if (killText != null) killText.text = kills.ToString();
        if (deathText != null) deathText.text = deaths.ToString();
    }

    public void SetDead(bool isDead)
    {
        if (deadOverlay != null && deadOverlay.activeSelf != isDead) deadOverlay.SetActive(isDead);
    }

    public void SetOffline(bool offline)
    {
        if (offlineOverlay != null && offlineOverlay.activeSelf != offline) offlineOverlay.SetActive(offline);
    }
}
