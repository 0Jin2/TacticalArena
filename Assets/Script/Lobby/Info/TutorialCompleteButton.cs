using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Fusion;

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
            Debug.LogError("❌ TutorialCompleteButton: completeButton 참조가 없습니다!");
    }

    public void OnClick_CompleteTutorial()
    {
        if (UserInfoManager.Instance == null)
        {
            Debug.LogError("❌ UserInfoManager.Instance 가 존재하지 않습니다!");
            return;
        }

        // ✅ 뒤끝 데이터에서 Tutorial 값을 true로 변경
        UserInfoManager.Instance.SetTutorialCompleted();

        Debug.Log("✅ 튜토리얼 완료 상태로 변경됨 (Backend 데이터 업데이트 요청됨)");

        Debug.Log("🏠 로비로 돌아가기 버튼 클릭됨");

        // 모든 NetworkRunner 종료 + 파괴 (DontDestroyOnLoad 잔존 방지)
        var runners = FindObjectsOfType<NetworkRunner>(true);
        foreach (var r in runners)
        {
            try { if (r.IsRunning) r.Shutdown(); } catch { /* 무시 */ }
            Destroy(r.gameObject);
        }

        SceneManager.LoadScene(lobbySceneName);
    }
}
