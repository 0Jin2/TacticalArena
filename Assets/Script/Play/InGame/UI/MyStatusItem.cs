using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// MyStatusItem : 화면 왼쪽 위의 "내 상태" 칸 (닉네임 / 체력바 / 아이콘 / 사망 표시)  (기존 MyStatusItem 대체)
// ─────────────────────────────────────────────────────────────────────────────

public class MyStatusItem : MonoBehaviour
{
    [Header("UI 요소")]
    [SerializeField] private TMP_Text nicknameText;
    [SerializeField] private Image healthBarFillImage;         // Fill Amount
    [SerializeField] private Image characterIconImage;
    [SerializeField] private GameObject deadOverlay;           // 사망 시 활성화
    [Tooltip("(선택) '85 / 100' 같은 체력 숫자")]
    [SerializeField] private TMP_Text hpText;

    private PlayerHealth health;

    public void Initialize(string nickname, Sprite characterIcon, PlayerHealth target)
    {
        Unhook();

        if (nicknameText != null) nicknameText.text = nickname;
        if (characterIconImage != null) characterIconImage.sprite = characterIcon;

        health = target;
        if (health == null) return;

        health.OnHealthChanged += HandleHealthChanged;
        health.OnDeath += HandleDeath;
        health.OnRevived += HandleRevived;

        // 현재 상태로 즉시 맞춤
        HandleHealthChanged(health.CurrentHP);
        if (deadOverlay != null) deadOverlay.SetActive(health.IsDead);
    }

    private void HandleHealthChanged(int currentHp)
    {
        if (health == null) return;
        float max = Mathf.Max(1, health.MaxHP);
        if (healthBarFillImage != null) healthBarFillImage.fillAmount = Mathf.Clamp01(currentHp / max);
        if (hpText != null) hpText.text = currentHp + " / " + health.MaxHP;
        if (currentHp > 0 && deadOverlay != null && deadOverlay.activeSelf) deadOverlay.SetActive(false);
    }

    private void HandleDeath(uint killer)
    {
        if (deadOverlay != null) deadOverlay.SetActive(true);
    }

    private void HandleRevived()
    {
        if (deadOverlay != null) deadOverlay.SetActive(false);
        if (health != null) HandleHealthChanged(health.CurrentHP);
    }

    private void Unhook()
    {
        if (health == null) return;
        health.OnHealthChanged -= HandleHealthChanged;
        health.OnDeath -= HandleDeath;
        health.OnRevived -= HandleRevived;
        health = null;
    }

    private void OnDestroy()
    {
        Unhook();
    }
}
