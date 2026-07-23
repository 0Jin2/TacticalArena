using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Fusion;

public class GoLobby : MonoBehaviour
{
    [Header("로비 이동 버튼")]
    public Button GoLobbyButton;
    [SerializeField] private string lobbySceneName = "LobbyPage";

    private void Start()
    {
        if (GoLobbyButton != null)
            GoLobbyButton.onClick.AddListener(OnClick_GoLobbyButton);
        else
            Debug.LogError("❌ TutorialCompleteButton: completeButton 참조가 없습니다!");
    }

    public void OnClick_GoLobbyButton()
    {
        var runners = FindObjectsOfType<NetworkRunner>(true);
        foreach (var r in runners)
        {
            try { if (r.IsRunning) r.Shutdown(); } catch { /* 무시 */ }
            Destroy(r.gameObject);
        }

        SceneManager.LoadScene(lobbySceneName);
    }
}
