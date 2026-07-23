using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Fusion;

public class MatchStatusItem : MonoBehaviour
{
    public TMP_Text nicknameText;
    public Image characterIconImage;
    public TMP_Text killText;
    public TMP_Text deathText;
    public GameObject deadOverlay;

    private PlayerRef owner;

    public void Initialize(PlayerRef player, string nickname, Sprite icon)
    {
        owner = player;

        nicknameText.text = nickname;
        nicknameText.color = player == FindObjectOfType<NetworkRunner>().LocalPlayer
            ? Color.yellow : Color.white;

        characterIconImage.sprite = icon;

        killText.text = "0";
        deathText.text = "0";
        deadOverlay.SetActive(false);
    }

    public void UpdateKillDeath(int kills, int deaths)
    {
        killText.text = kills.ToString();
        deathText.text = deaths.ToString();
    }

    public void SetDead(bool isDead)
    {
        deadOverlay.SetActive(isDead);
    }
}
