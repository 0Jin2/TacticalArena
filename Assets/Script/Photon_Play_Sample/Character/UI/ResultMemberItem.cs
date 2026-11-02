// [DISABLED] Photon removed (BACKND migration). Kept for reference only.
// To re-enable, delete the "#if false" line and the final "#endif" line.
#if false
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResultMemberItem : MonoBehaviour
{
    [Header("UI")]
    public Image characterIconImage;
    public TMP_Text nicknameText;
    public TMP_Text killText;
    public TMP_Text deathText;

    public void Setup(Sprite icon, string nickname, int kills, int deaths)
    {
        if (characterIconImage) characterIconImage.sprite = icon;
        if (nicknameText) nicknameText.text = nickname;
        if (killText) killText.text = kills.ToString();
        if (deathText) deathText.text = deaths.ToString();
    }
}
#endif
