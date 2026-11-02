using BackEnd;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// 튜토리얼 완료 버튼 : 뒤끝에 "튜토리얼 완료"를 기록하고 로비로 이동한다.
// (예전의 Photon NetworkRunner 종료 코드는 제거. 대신 뒤끝 게임 서버에서 나간다.)
public class TutorialCompleteButton : MonoBehaviour
{
    [Header("튜토리얼 완료 버튼")]
    public Button completeButton;
    [SerializeField] private string lobbySceneName = "LobbyPage";

    private void Start()
    {
        if (completeButton != null)
            completeButton.onClick.AddListener(OnClick_CompleteTutorial);
        else
            Debug.LogError("TutorialCompleteButton: completeButton 참조가 없습니다!");
    }

    public void OnClick_CompleteTutorial()
    {
        if (UserInfoManager.Instance == null)
        {
            Debug.LogError("UserInfoManager.Instance 가 존재하지 않습니다!");
            return;
        }

        // 뒤끝 데이터에서 Tutorial 값을 true 로 변경
        UserInfoManager.Instance.SetTutorialCompleted();
        Debug.Log("튜토리얼 완료 상태로 변경됨 (Backend 데이터 업데이트 요청됨)");

        try { if (Backend.IsInitialized) Backend.Match.LeaveGameServer(); }
        catch (System.Exception exception) { Debug.LogWarning("TutorialCompleteButton: LeaveGameServer 예외 - " + exception.Message); }

        SceneManager.LoadScene(lobbySceneName);
    }
}
