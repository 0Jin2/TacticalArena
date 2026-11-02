using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
// ReturnToLobbyButton : "로비로 돌아가기" 버튼에 연결  (기존 ReturnToLobbyButton 대체)
//   OnClickReturnToLobby() 를 버튼의 OnClick 에 연결한다.
//   인게임 서버에서 나가고 로비 씬으로 이동한다. (ResultReporter 가 있으면 그쪽 처리를 사용 → 호스트면 결과 보고도 마무리)
// ─────────────────────────────────────────────────────────────────────────────

public class ReturnToLobbyButton : MonoBehaviour
{
    [SerializeField] private string lobbySceneName = "LobbyPage";

    public void OnClickReturnToLobby()
    {
        var reporter = ResultReporter.Instance;
        if (reporter != null)
        {
            reporter.ReturnToLobby();
            return;
        }

        // ResultReporter 가 없는 씬(튜토리얼 등): 서버에서 나가고 이동
        try { if (BackEnd.Backend.IsInitialized) BackEnd.Backend.Match.LeaveGameServer(); }
        catch (System.Exception exception) { NetLog.Warn("LeaveGameServer 예외: " + exception.Message); }

        var net = InGameNet.Instance;
        if (net != null && net.Connection != null) net.Connection.NotifyMatchEnded();

        SceneManager.LoadScene(lobbySceneName);
    }
}
