using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FadeInAnim : MonoBehaviour
{
    public CanvasGroup uiElement; // 투명도를 제어할 UI의 CanvasGroup
    public Button toggleButton; // UI를 활성화할 버튼
    public float fadeDuration = 2.0f; // 투명도가 변화하는 시간 (초 단위)

    void Start()
    {
        // 버튼 클릭 이벤트에 메서드 연결
        toggleButton.onClick.AddListener(() => StartCoroutine(FadeInUI()));

        // UI를 처음엔 보이지 않게 설정
        uiElement.alpha = 0;
        uiElement.interactable = false;
        uiElement.blocksRaycasts = false;
    }

    // 투명도가 점점 증가하는 코루틴
    IEnumerator FadeInUI()
    {
        float elapsedTime = 0f;

        // UI 활성화 (알파값 0에서 1로 증가)
        uiElement.interactable = true;
        uiElement.blocksRaycasts = true;

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            uiElement.alpha = Mathf.Clamp01(elapsedTime / fadeDuration); // 투명도 0 -> 1
            yield return null;
        }

        // 최종적으로 알파값을 1로 설정
        uiElement.alpha = 1;
    }
}