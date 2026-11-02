using BackEnd;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// 로비로 돌아가는 버튼 (튜토리얼/결과 화면 등).
// 예전에는 Photon NetworkRunner 를 종료했지만, 이제는 뒤끝 게임 서버에서 나가고 씬을 이동한다.
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
            Debug.LogError("GoLobby: GoLobbyButton 참조가 없습니다!");
    }

    public void OnClick_GoLobbyButton()
    {
        try { if (Backend.IsInitialized) Backend.Match.LeaveGameServer(); }
        catch (System.Exception exception) { Debug.LogWarning("GoLobby: LeaveGameServer 예외 - " + exception.Message); }

        SceneManager.LoadScene(lobbySceneName);
    }
}
