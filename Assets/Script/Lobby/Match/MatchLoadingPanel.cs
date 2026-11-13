using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// MatchLoadingPanel : 로비 → 플레이 씬으로 넘어가는 동안 켜 두는 로딩 패널
//
//  BACKEND_MATCH 가 매칭이 잡히면 이 패널을 켜고, 모든 유저의 씬 로딩이 끝나면 같이 씬을 이동한다.
//
//  사용법 A (그냥 쓰기): 아무것도 안 해도 된다. 비워 두면 검은 화면 + LOADING 글자 + 진행 막대를 자동으로 만든다.
//  사용법 B (내가 만든 패널): 로비 씬 Canvas 에 패널(UI 오브젝트)을 만들고 이 스크립트를 붙인 뒤
//          BACKEND_MATCH 의 Loading Panel 칸에 끌어다 넣는다. 패널은 처음에 꺼 두면 된다.
//          (글자/진행 막대는 아래 칸에 연결하면 갱신된다. 연결 안 하면 첫 번째 TMP 글자만 쓴다)
// ─────────────────────────────────────────────────────────────────────────────

public class MatchLoadingPanel : MonoBehaviour
{
    [Header("직접 만든 패널을 쓸 때 (모두 선택)")]
    [Tooltip("켜고 끌 패널 오브젝트. 비워 두면 이 스크립트가 붙은 오브젝트(UI 라면) 또는 자동 생성 화면을 씁니다.")]
    public GameObject panelRoot;
    [Tooltip("\"불러오는 중...\" / \"다른 플레이어를 기다리는 중...\" 문구")]
    public TMP_Text messageText;
    [Tooltip("\"2 / 4\" 같은 로딩 완료 인원 표시 (선택)")]
    public TMP_Text countText;
    [Tooltip("진행 막대 (선택). Slider 를 연결하세요.")]
    public Slider progressSlider;

    [Header("문구")]
    public string loadingMessage = "게임을 불러오는 중...";
    public string waitingMessage = "다른 플레이어를 기다리는 중...";

    private bool built;
    private RectTransform fallbackFill;

    private void EnsureBuilt()
    {
        if (built) return;
        built = true;

        if (panelRoot == null && GetComponent<RectTransform>() != null) panelRoot = gameObject;   // UI 패널에 직접 붙인 경우
        if (panelRoot == null) BuildFallback();

        if (messageText == null && panelRoot != null) messageText = panelRoot.GetComponentInChildren<TMP_Text>(true);
        if (countText == messageText) countText = null;
    }

    public void Show()
    {
        EnsureBuilt();
        if (panelRoot == null) return;

        panelRoot.SetActive(true);
        panelRoot.transform.SetAsLastSibling();      // 다른 UI 위에 보이게
        SetMessage(loadingMessage);
        SetCount(string.Empty);
        SetProgress(0f);
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    /// <summary>씬 불러오는 진행도 0~1</summary>
    public void SetProgress(float value)
    {
        value = Mathf.Clamp01(value);
        if (progressSlider != null) progressSlider.value = value;
        if (fallbackFill != null) fallbackFill.anchorMax = new Vector2(value, 1f);
    }

    /// <summary>내 로딩은 끝났고 다른 유저를 기다리는 중</summary>
    public void SetWaiting(int loaded, int total)
    {
        SetMessage(waitingMessage);
        SetCount(loaded + " / " + total);
    }

    private void SetMessage(string text)
    {
        if (messageText != null) messageText.text = text;
    }

    private void SetCount(string text)
    {
        if (countText != null) countText.text = text;
        else if (messageText != null && !string.IsNullOrEmpty(text) && countText == null && fallbackFill == null)
        {
            // 인원 글자가 따로 없으면 문구 뒤에 붙인다.
            messageText.text = waitingMessage + "  (" + text + ")";
        }
    }

    // ── 자동 생성 화면 (기본 폰트에는 한글이 없으므로 영어 문구를 쓴다) ─────────────
    private void BuildFallback()
    {
        loadingMessage = "LOADING...";
        waitingMessage = "WAITING FOR OTHER PLAYERS...";

        var canvasObject = new GameObject("MatchLoadingCanvas");
        canvasObject.transform.SetParent(transform, false);
        panelRoot = canvasObject;

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();         // 뒤에 있는 버튼이 눌리지 않게 막는다

        var background = NewRect("Background", canvasObject.transform);
        Stretch(background);
        var bg = background.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.95f);

        messageText = NewText("Message", canvasObject.transform, 56f, new Vector2(0f, 60f));
        countText = NewText("Count", canvasObject.transform, 40f, new Vector2(0f, -10f));

        var barBack = NewRect("BarBack", canvasObject.transform);
        barBack.anchorMin = barBack.anchorMax = new Vector2(0.5f, 0.5f);
        barBack.sizeDelta = new Vector2(700f, 16f);
        barBack.anchoredPosition = new Vector2(0f, -90f);
        barBack.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.2f);

        fallbackFill = NewRect("BarFill", barBack);
        fallbackFill.anchorMin = Vector2.zero;
        fallbackFill.anchorMax = new Vector2(0f, 1f);
        fallbackFill.offsetMin = fallbackFill.offsetMax = Vector2.zero;
        fallbackFill.gameObject.AddComponent<Image>().color = Color.white;

        panelRoot.SetActive(false);
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static TMP_Text NewText(string name, Transform parent, float size, Vector2 position)
    {
        var rect = NewRect(name, parent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(1400f, 100f);
        rect.anchoredPosition = position;

        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }
}
