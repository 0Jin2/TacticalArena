#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 테스트 모드 게임 중 디버그 창 (F1 또는 우측 상단 TEST 버튼으로 열고 닫기)
public class TestModeDebugWindow : MonoBehaviour
{
    private const float ReferenceHeight = 720f;

    private bool open;
    private Vector2 scroll;
    private List<string> mapPaths;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1)) open = !open;
    }

    private void OnGUI()
    {
        var manager = TestModeManager.Instance;
        if (manager == null || manager.MapPath == null) return; // 테스트 로비에서는 TestLobbyUI가 담당

        float scale = Mathf.Max(1f, Screen.height / ReferenceHeight);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;

        if (GUI.Button(new Rect(width - 90, 8, 82, 28), open ? "TEST ▲" : "TEST ▼"))
            open = !open;
        if (!open) return;

        var area = new Rect(width - 330, 42, 322, Mathf.Min(560, height - 50));
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 8, area.y + 8, area.width - 16, area.height - 16));
        scroll = GUILayout.BeginScrollView(scroll);

        DrawInfo(manager);
        GUILayout.Space(8);
        DrawMapChange(manager);
        GUILayout.Space(8);
        DrawCharacterChange(manager);
        GUILayout.Space(8);
        if (GUILayout.Button("테스트 로비로", GUILayout.Height(30)))
            manager.BackToTestLobby();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawInfo(TestModeManager manager)
    {
        var runner = manager.Runner;
        int players = runner != null && runner.IsRunning ? runner.ActivePlayers.Count() : 0;

        GUILayout.Label("<b>테스트 모드</b> (F1)", RichLabel());
        GUILayout.Label($"방 코드: {manager.RoomCode}   인원: {players}명");
        GUILayout.Label($"맵: {TestModeManager.MapName(manager.MapPath)}   {(manager.IsMaster ? "방장" : "참가자")}");
        if (!string.IsNullOrEmpty(manager.Status))
            GUILayout.Label(manager.Status);
    }

    private void DrawMapChange(TestModeManager manager)
    {
        GUILayout.Label(manager.IsMaster ? "맵 변경 (방 전체 이동)" : "맵 변경 (방장만 가능)");
        mapPaths ??= TestModeManager.FindMapPaths();

        GUI.enabled = manager.IsMaster && !manager.IsBusy;
        foreach (var path in mapPaths)
        {
            bool current = path == manager.MapPath;
            if (GUILayout.Button(current ? $"▶ {TestModeManager.MapName(path)}" : TestModeManager.MapName(path)) && !current)
                manager.RequestMapChange(path);
        }
        GUI.enabled = true;
    }

    private void DrawCharacterChange(TestModeManager manager)
    {
        GUILayout.Label("내 캐릭터 변경 (이 맵에서 고를 수 있는 캐릭터)");
        var ids = TestModeManager.AvailableCharacterIds();
        if (ids.Count == 0)
        {
            GUILayout.Label("(캐릭터 목록 준비 중)");
            return;
        }

        GUI.enabled = !manager.IsBusy && CharacterSelectionNetworkData.Instance != null;
        foreach (var id in ids)
        {
            bool current = id == manager.CharacterId;
            if (GUILayout.Button(current ? $"▶ {id}" : id) && !current)
                manager.RequestCharacterChange(id);
        }
        GUI.enabled = true;
    }

    private static GUIStyle RichLabel()
    {
        return new GUIStyle(GUI.skin.label) { richText = true };
    }
}
#endif
