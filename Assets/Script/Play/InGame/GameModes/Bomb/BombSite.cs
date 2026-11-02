using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────────────────────
// BombSite : 폭탄 설치 지점  (기존 BombSite 대체)
//
//  - 이제 "네트워크로 생성되는 오브젝트"가 아니라 씬에 미리 놓아 두는 평범한 오브젝트입니다.
//    (BombMode 의 sites 배열에 넣어 주세요. 비워 두면 씬의 BombSite 를 이름순으로 자동 수집합니다)
//  - 설치/해제의 "판정"은 BombMode(호스트)가 하고, 이 스크립트는 화면 표시와
//    "내 캐릭터가 이 지점 안에 있는가" 감지만 합니다.
//  - Collider2D(Trigger) 필요.
// ─────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(Collider2D))]
public class BombSite : MonoBehaviour
{
    [Header("기준점 / 상호작용")]
    [Tooltip("거리 판정의 기준 위치. 비워 두면 이 오브젝트의 위치")]
    public Transform worldAnchor;
    [Tooltip("이 반경 안에서만 설치/해제할 수 있습니다.")]
    public float interactRange = 2.0f;

    [Header("폭탄 비주얼")]
    [Tooltip("설치되면 켜지는 폭탄 오브젝트")]
    public GameObject bombVisual;
    [Range(0f, 1f)] public float eligibleAlpha = 0.5f;       // 설치 가능 위치에 서 있을 때(미리보기) 반투명
    [Range(0f, 1f)] public float defaultAlpha = 1.0f;

    [Header("진행도 표시 (월드 캔버스, 선택)")]
    [SerializeField] private Canvas progressCanvas;
    [SerializeField] private Image progressFill;
    [SerializeField] private TMP_Text progressLabel;
    public string plantLabelFormat = "설치 중... ({0}%)";
    public string defuseLabelFormat = "해제 중... ({0}%)";

    [Header("인덱스 (BombMode 가 지정)")]
    public int siteIndex = -1;

    public bool IsActive { get; private set; }
    public bool IsPlantedHere { get; private set; }
    /// <summary>내 캐릭터가 이 지점 트리거 안에 있는가</summary>
    public bool LocalInside { get { return localOverlap > 0; } }
    public Vector3 AnchorPosition { get { return worldAnchor != null ? worldAnchor.position : transform.position; } }
    public Transform AnchorTransform { get { return worldAnchor != null ? worldAnchor : transform; } }

    private CanvasGroup visualGroup;
    private SpriteRenderer[] spriteRenderers;
    private int localOverlap;
    private bool previewShown;
    private bool progressIsPlant = true;

    private void Awake()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;

        if (bombVisual != null)
        {
            visualGroup = bombVisual.GetComponent<CanvasGroup>();
            if (visualGroup == null) visualGroup = bombVisual.AddComponent<CanvasGroup>();
            spriteRenderers = bombVisual.GetComponentsInChildren<SpriteRenderer>(true);
        }
        SetVisualAlpha(defaultAlpha);
        HideProgress();
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region BombMode 가 호출

    /// <summary>라운드 시작: 초기화 후 활성화</summary>
    public void ResetForRound()
    {
        IsActive = true;
        IsPlantedHere = false;
        previewShown = false;
        if (bombVisual != null) bombVisual.SetActive(false);
        SetVisualAlpha(defaultAlpha);
        HideProgress();
    }

    /// <summary>라운드 종료 후: 상호작용 불가</summary>
    public void Deactivate()
    {
        IsActive = false;
        previewShown = false;
        SetVisualAlpha(defaultAlpha);
        HideProgress();
    }

    public void SetPlanted()
    {
        IsPlantedHere = true;
        previewShown = false;
        if (bombVisual != null) bombVisual.SetActive(true);
        SetVisualAlpha(defaultAlpha);
        HideProgress();
    }

    /// <summary>설치/해제 진행도 표시 (0~1). 값이 음수면 숨김</summary>
    public void ShowProgress(bool isPlant, float normalized)
    {
        progressIsPlant = isPlant;
        if (progressCanvas == null) return;
        progressCanvas.enabled = true;
        float p = Mathf.Clamp01(normalized);
        if (progressFill != null) progressFill.fillAmount = p;
        if (progressLabel != null)
            progressLabel.text = string.Format(isPlant ? plantLabelFormat : defuseLabelFormat, Mathf.RoundToInt(p * 100f));
    }

    public void HideProgress()
    {
        if (progressCanvas != null) progressCanvas.enabled = false;
    }

    /// <summary>내가 설치/해제 가능한 위치에 서 있을 때 폭탄 미리보기를 반투명으로 보여 준다.</summary>
    public void SetLocalPreview(bool eligible)
    {
        if (eligible)
        {
            if (!IsPlantedHere && bombVisual != null && !bombVisual.activeSelf) bombVisual.SetActive(true);
            SetVisualAlpha(eligibleAlpha);
            previewShown = true;
        }
        else if (previewShown)
        {
            SetVisualAlpha(defaultAlpha);
            if (!IsPlantedHere && bombVisual != null) bombVisual.SetActive(false);
            previewShown = false;
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 내 캐릭터 감지

    private void OnTriggerEnter2D(Collider2D other)
    {
        var entity = other.GetComponentInParent<PlayerEntity>();
        if (entity == null || !entity.IsMine) return;
        localOverlap++;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var entity = other.GetComponentInParent<PlayerEntity>();
        if (entity == null || !entity.IsMine) return;
        localOverlap = Mathf.Max(0, localOverlap - 1);
    }

    private void OnDisable()
    {
        localOverlap = 0;
    }

    #endregion

    private void SetVisualAlpha(float a)
    {
        if (visualGroup != null) visualGroup.alpha = a;
        if (spriteRenderers == null) return;
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            var sr = spriteRenderers[i];
            if (sr == null) continue;
            var c = sr.color;
            c.a = a;
            sr.color = c;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.8f);
        Gizmos.DrawWireSphere(AnchorPosition, interactRange);
    }
}
