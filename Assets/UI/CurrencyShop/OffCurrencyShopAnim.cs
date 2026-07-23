using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class OffCurrencyShopAnim : MonoBehaviour
{
    [Header("UI오브젝트 참조")]
    public GameObject uiObject; // UI 오브젝트 지정
    public List<Button> triggerButtons; // 버튼 리스트

    private Animator uiAnimator;
    private Vector3 initialPosition; // 초기 위치 저장 변수

    void Start()
    {
        // UI 오브젝트의 초기 위치 저장
        if (uiObject != null)
        {
            initialPosition = uiObject.transform.localPosition;

            uiAnimator = uiObject.GetComponent<Animator>();
            if (uiAnimator == null)
            {
                Debug.LogWarning("지정된 UI 오브젝트에 Animator가 없습니다.");
            }
        }
        else
        {
            Debug.LogError("UI 오브젝트가 설정되지 않았습니다!");
        }

        // 버튼 리스트의 클릭 이벤트 연결
        foreach (var button in triggerButtons)
        {
            if (button != null)
            {
                button.onClick.AddListener(DeactivateUIWithAnimation);
            }
            else
            {
                Debug.LogError("버튼 리스트에 Null이 포함되어 있습니다!");
            }
        }
    }

    public void DeactivateUIWithAnimation()
    {
        if (uiObject == null || uiAnimator == null)
        {
            Debug.LogError("UI 오브젝트 또는 Animator가 설정되지 않았습니다!");
            return;
        }

        // 초기 위치로 리셋
        uiObject.transform.localPosition = initialPosition;

        // Animator의 Bool 값 OffOption 설정
        uiAnimator.SetBool("CurrencyShopOff", true);

        // 애니메이션 종료 시 UI 오브젝트 비활성화
        StartCoroutine(WaitForAnimationEnd());
    }

    private System.Collections.IEnumerator WaitForAnimationEnd()
    {
        // 현재 실행 중인 애니메이션의 길이를 계산
        float animationLength = uiAnimator.GetCurrentAnimatorStateInfo(0).length;

        // 애니메이션이 끝날 때까지 대기
        yield return new WaitForSeconds(animationLength);

        // UI 오브젝트 비활성화
        uiObject.SetActive(false);

        // Animator Bool 값 초기화
        uiAnimator.SetBool("CurrencyShopOff", false);

        Debug.Log("UI 오브젝트 비활성화 및 Bool 값 초기화 완료!");
    }
}