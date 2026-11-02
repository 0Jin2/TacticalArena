using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TutorialObjectActivator : MonoBehaviour
{
    [Header("튜토리얼 완료 여부에 따라 On/Off 할 오브젝트들")]
    [SerializeField] private GameObject[] targetObjects;

    private bool subscribed = false;

    private void OnEnable()
    {
        StartCoroutine(BindAndApply());
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private IEnumerator BindAndApply()
    {
        // UserInfoManager 인스턴스가 생성될 때까지 대기 (DontDestroyOnLoad 구조 대응)
        while (UserInfoManager.Instance == null)
            yield return null;

        Subscribe();

        // 현재 값으로 즉시 1회 적용
        Apply(UserInfoManager.Instance.IsTutorialCompleted);
    }

    private void Subscribe()
    {
        if (subscribed) return;

        UserInfoManager.Instance.OnTutorialDataLoaded += HandleTutorialDataLoaded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        if (UserInfoManager.Instance != null)
            UserInfoManager.Instance.OnTutorialDataLoaded -= HandleTutorialDataLoaded;

        subscribed = false;
    }

    private void HandleTutorialDataLoaded()
    {
        if (UserInfoManager.Instance == null) return;
        Apply(UserInfoManager.Instance.IsTutorialCompleted);
    }

    private void Apply(bool isTutorialCompleted)
    {
        // 요구사항:
        // - 튜토리얼 값 false면 오브젝트 활성화
        // - 튜토리얼 값 true면 오브젝트 비활성화 유지
        bool shouldActive = (isTutorialCompleted == false);

        if (targetObjects == null) return;

        for (int i = 0; i < targetObjects.Length; i++)
        {
            if (targetObjects[i] == null) continue;
            if (targetObjects[i].activeSelf != shouldActive)
                targetObjects[i].SetActive(shouldActive);
        }
    }
}