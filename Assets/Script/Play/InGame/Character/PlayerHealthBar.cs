using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerHealthBar : 캐릭터 머리 위 체력바 (기존 HealthBar 대체)
//   캐릭터 프리팹 안의 월드 캔버스 오브젝트에 붙인다.
//   캐릭터가 좌우로 뒤집혀도(scale.x 부호) 체력바는 항상 똑바로 보이도록 되돌린다.
//   체력바 색: 나 = 노랑 / 같은 팀 = 파랑 / 상대 팀 = 빨강  (색은 Resources/TeamColorSettings 에서 바꿈)
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerHealthBar : MonoBehaviour
{
    [Header("연결")]
    public Image fillImage;  // 체력 바 채움 이미지 (Image Type = Filled)

    [Header("색")]
    [Tooltip("켜면 체력바를 '나 노랑 / 같은 팀 파랑 / 상대 팀 빨강'으로 칠합니다. 끄면 프리팹에 정한 색을 그대로 씁니다.")]
    public bool useTeamColor = true;  // 켜면 체력바를 관계 색으로 칠함

    [Header("폭 조절 (최대 체력이 클수록 바가 길어진다)")]
    public bool scaleWidthByMaxHp = true;  // 켜면 최대 체력이 클수록 바가 길어짐
    public float baseBarWidth = 0.6f;      // 기준 체력(baseHp)일 때 바 길이
    public int baseHp = 120;               // 바 길이 계산의 기준 체력

    private PlayerEntity entity;
    private RectTransform rect;
    private float initialScaleX = 1f;
    private bool colorSubscribed;

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
        ApplyTeamColor();
        var net = InGameNet.Instance;
        if (net != null && !colorSubscribed) { net.PlayersChanged += ApplyTeamColor; colorSubscribed = true; }   // 내 팀이 늦게 정해져도 다시 칠함

        if (e.Health == null) return;
        e.Health.OnHealthChanged += Refresh;
        Refresh(e.Health.CurrentHP);

        if (scaleWidthByMaxHp && rect != null)
        {
            float width = baseBarWidth * (e.Health.MaxHP / (float)Mathf.Max(1, baseHp));
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        }
    }

    /// <summary>체력바 색을 관계 색으로 칠한다. (관계를 아직 모르면 프리팹 색 유지)</summary>
    private void ApplyTeamColor()
    {
        if (!useTeamColor || fillImage == null || entity == null) return;
        var relation = TeamView.RelationOf(entity.IsMine, entity.Team);
        if (relation == TeamRelation.None) return;
        fillImage.color = TeamView.ColorOf(relation);
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
        if (colorSubscribed && InGameNet.Instance != null) InGameNet.Instance.PlayersChanged -= ApplyTeamColor;
    }
}
