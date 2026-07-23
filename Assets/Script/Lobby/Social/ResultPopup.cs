using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ResultPopup : MonoBehaviour
{
    [SerializeField] private TMP_Text messageText;      // 메시지를 표시할 텍스트 컴포넌트
    [SerializeField] private float fadeDuration = 2f; // 페이드 아웃에 걸리는 시간(초)

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void SetMessage(string message)
    {
        if (messageText != null)
            messageText.text = message;
    }

    private void OnEnable()
    {
        StartCoroutine(FadeOutAndDestroy());
    }

    private IEnumerator FadeOutAndDestroy()
    {
        float timer = 0f;
        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            yield return null;
        }
        Destroy(gameObject);
    }
}
