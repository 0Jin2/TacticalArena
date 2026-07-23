using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class Login_Lobby : MonoBehaviour
{
    public GameObject loadingScreen;  // 로딩 화면 UI 오브젝트
    public TextMeshProUGUI loadingMessage;  // TextMeshPro UI 컴포넌트
    public string[] randomMessages;  // 로딩 중 출력할 메시지 리스트

    private bool isLoading = false;

    void Update()
    {
        // 마우스 왼쪽 클릭 감지
        if (Input.GetMouseButtonDown(0) && !isLoading)
        {
            isLoading = true;
            StartLoadingScene("LobbyPage"); // 로드할 씬 이름
        }
    }

    public void StartLoadingScene(string sceneName)
    {
        loadingScreen.SetActive(true); // 로딩 화면 활성화
        StartCoroutine(ShowRandomLoadingMessages()); // 랜덤 메시지 출력
        StartCoroutine(LoadSceneAsync(sceneName)); // 씬 로드 시작
    }

    private IEnumerator LoadSceneAsync(string sceneName)
    {
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        operation.allowSceneActivation = false; // 로딩 완료 후 수동 전환

        float minDisplayTime = 3f; // 최소 유지 시간
        float elapsed = 0f;

        while (!operation.isDone)
        {
            elapsed += Time.deltaTime;

            if (operation.progress >= 0.9f && elapsed >= minDisplayTime)
            {
                Debug.Log("로딩 완료 및 최소 표시 시간 충족");
                operation.allowSceneActivation = true; // 씬 전환 허용
            }

            yield return null; // 다음 프레임까지 대기
        }

        loadingScreen.SetActive(false); // 로딩 화면 비활성화 (필요시)
    }

    private IEnumerator ShowRandomLoadingMessages()
    {
        while (true)
        {
            // 랜덤 메시지 선택
            string randomMessage = randomMessages[Random.Range(0, randomMessages.Length)];
            loadingMessage.text = randomMessage; // 메시지 출력

            yield return new WaitForSeconds(2f); // 메시지 변경 간격 (2초)
        }
    }
}
