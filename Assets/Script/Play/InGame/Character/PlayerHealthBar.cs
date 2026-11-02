using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerHealthBar : 캐릭터 머리 위 체력바 (기존 HealthBar 대체)
//   캐릭터 프리팹 안의 월드 캔버스 오브젝트에 붙인다.
//   캐릭터가 좌우로 뒤집혀도(scale.x 부호) 체력바는 항상 똑바로 보이도록 되돌린다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerHealthBar : MonoBehaviour
{
    [Header("연결")]
    public Image fillImage;

    [Header("폭 조절 (최대 체력이 클수록 바가 길어진다)")]
    public bool scaleWidthByMaxHp = true;
    public float baseBarWidth = 0.6f;
    public int baseHp = 120;

    private PlayerEntity entity;
    private RectTransform rect;
    private float initialScaleX = 1f;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        initialScaleX = transform.localScale.x;
        entity = GetComponentInParent<PlayerEntity>();
    }

    private void Start()
    {
        if (entity == null) { enabled = false; return; }
        entity.WhenReady(OnEntityReady);
    }

    private void OnEntityReady(PlayerEntity e)
    {
        if (e.Health == null) return;
        e.Health.OnHealthChanged += Refresh;
        Refresh(e.Health.CurrentHP);

        if (scaleWidthByMaxHp && rect != null)
        {
            float width = baseBarWidth * (e.Health.MaxHP / (float)Mathf.Max(1, baseHp));
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }
    }

    private void Refresh(int hp)
    {
        if (fillImage == null || entity == null || entity.Health == null) return;
        fillImage.fillAmount = Mathf.Clamp01(hp / (float)Mathf.Max(1, entity.Health.MaxHP));
    }

    private void LateUpdate()
    {
        if (transform.parent == null) return;
        float parentSign = Mathf.Sign(transform.parent.lossyScale.x == 0f ? 1f : transform.parent.lossyScale.x);
        var ls = transform.localScale;
        ls.x = Mathf.Abs(initialScaleX) * (initialScaleX < 0f ? -1f : 1f) * parentSign;
        transform.localScale = ls;
    }

    private void OnDestroy()
    {
        if (entity != null && entity.Health != null) entity.Health.OnHealthChanged -= Refresh;
    }
}
