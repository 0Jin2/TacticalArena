using UnityEngine;
using UnityEngine.UI;
using Fusion;

public class HealthBar : MonoBehaviour
{
    public Image fillImage;
    public float baseBarWidth = 1f;
    public int baseHP = 100;

    private Health targetHealth;
    private RectTransform canvasRect;
    private float initialLocalScaleX;

    private void Awake()
    {
        canvasRect = GetComponent<RectTransform>();
        initialLocalScaleX = transform.localScale.x;
    }

    public void Init(Health health, float baseWidth = 0.6f, int baseHp = 120)
    {
        Debug.Log($"[HealthBar] Init 호출! Health: {health.gameObject.name}, HP={health.currentHP}");
        if (targetHealth != null)
        {
            targetHealth.OnHealthChanged -= UpdateHealthBar;
        }

        targetHealth = health;
        baseBarWidth = baseWidth;
        baseHP = baseHp;

        if (targetHealth != null)
        {
            targetHealth.OnHealthChanged += UpdateHealthBar;
            UpdateHealthBar(targetHealth.currentHP);
            UpdateCanvasWidth();
        }
    }

    public void UpdateHealthBar(int currentHP)
    {
        if (fillImage == null || targetHealth?.statData == null) return;
        float percent = Mathf.Clamp01((float)currentHP / targetHealth.statData.maxHP);
        fillImage.fillAmount = percent;
    }

    private void UpdateCanvasWidth()
    {
        if (canvasRect == null || targetHealth?.statData == null) return;
        float newWidth = baseBarWidth * (targetHealth.statData.maxHP / (float)baseHP);
        canvasRect.sizeDelta = new Vector2(newWidth, canvasRect.sizeDelta.y);
    }

    private void LateUpdate()
    {
        // 부모가 뒤집혔으면, localScale.x를 initialLocalScaleX * 부모 방향(sign)으로 유지
        if (transform.parent != null)
        {
            float parentSign = Mathf.Sign(transform.parent.localScale.x);
            Vector3 ls = transform.localScale;
            ls.x = initialLocalScaleX * parentSign;
            transform.localScale = ls;
        }
    }

    private void OnDestroy()
    {
        if (targetHealth != null)
            targetHealth.OnHealthChanged -= UpdateHealthBar;
    }
}
