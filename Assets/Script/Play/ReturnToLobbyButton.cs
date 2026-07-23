using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;

public class ReturnToLobbyButton : MonoBehaviour
{
    [SerializeField] private string lobbySceneName = "LobbyPage";

    public void OnClickReturnToLobby()
    {
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
