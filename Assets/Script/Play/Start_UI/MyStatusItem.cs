using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MyStatusItem : MonoBehaviour
{
    [Header("UI 요소")]
    [SerializeField] private TMP_Text nicknameText;         // 닉네임 표시
    [SerializeField] private Image healthBarFillImage;    // 체력바(Fill Amount)
    [SerializeField] private Image characterIconImage;    // 캐릭터 아이콘
    [SerializeField] private GameObject deadOverlay;        // 사망 시 활성화

    private Health healthComponent;

    /// <summary>
    /// 외부에서 호출하는 초기화 메서드
    /// </summary>
    public void Initialize(string nickname, Sprite characterIcon, Health health)
    {
        nicknameText.text = nickname;
        characterIconImage.sprite = characterIcon;

        healthComponent = health;
        healthComponent.OnHealthChanged += HandleHealthChanged;
        healthComponent.OnDeath += HandleDeath;

        // 현재 HP/사망 상태로 즉시 UI 동기화
        HandleHealthChanged(healthComponent.currentHP);
        deadOverlay.SetActive(healthComponent.IsDead);
    }

    /// <summary>
    /// 체력 변화 콜백 (Action<int> 에 맞춤)
    /// </summary>
    private void HandleHealthChanged(int currentHp)
    {
        float fill = Mathf.Clamp01(currentHp / (float)healthComponent.statData.maxHP);
        healthBarFillImage.fillAmount = fill;

        if (currentHp > 0 && deadOverlay.activeSelf)
            deadOverlay.SetActive(false);
    }

    /// <summary>
    /// 사망 콜백
    /// </summary>
    private void HandleDeath()
    {
        deadOverlay.SetActive(true);
    }

    private void OnDestroy()
    {
        if (healthComponent != null)
        {
            healthComponent.OnHealthChanged -= HandleHealthChanged;
            healthComponent.OnDeath -= HandleDeath;
        }
    }
}
