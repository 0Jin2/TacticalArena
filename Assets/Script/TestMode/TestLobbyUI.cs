#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// 테스트 로비 화면 (Assets/Scenes/Test/TestLobby.unity)
// - 혼자 플레이: 나만 들어가는 비공개 방
// - 같이 플레이: 같은 방 코드를 입력한 사람끼리 입장 (같은 PC의 창들은 기본 코드가 같아서 바로 합류)
public class TestLobbyUI : MonoBehaviour
{
    private const float ReferenceHeight = 720f;
    private const int MultiMaxPlayers = 10;

    private List<string> mapPaths;
    private List<string> characterIds;
    private int mapIndex;
    private int characterIndex;
    private string nickname;
    private string roomCode;
    private Vector2 scroll;

    private void Start()
    {
        mapPaths = TestModeManager.FindMapPaths();
        characterIds = TestModeManager.FindCharacterIds();
        // MPPM 추가 에디터는 닉네임이 겹치지 않게 따로 생성
        nickname = GPGS_BACKEND.AdditionalEditorId != null || string.IsNullOrEmpty(GPGS_BACKEND.MyNickname)
            ? $"Tester{Random.Range(100, 1000)}"
            : GPGS_BACKEND.MyNickname;
        roomCode = DefaultRoomCode();
        TestModeManager.Ensure();
    }

    // 같은 PC(에디터 창 + MPPM 가상 플레이어)는 같은 코드 → 그냥 둘 다 "같이 플레이"만 누르면 합류
    private static string DefaultRoomCode()
    {
        uint hash = 2166136261;
        foreach (char c in System.Environment.MachineName)
            hash = (hash ^ c) * 16777619;
        return $"PC{hash % 10000:0000}";
    }

    private void OnGUI()
    {
        var manager = TestModeManager.Instance;
        if (manager == null) return;

        float scale = Mathf.Max(1f, Screen.height / ReferenceHeight);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;

        var area = new Rect(Mathf.Max(8, (width - 520) / 2), 16, Mathf.Min(520, width - 16), height - 32);
        GUI.Box(area, GUIContent.none);
        GUILayout.BeginArea(new Rect(area.x + 12, area.y + 12, area.width - 24, area.height - 24));
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("<size=20><b>테스트 모드</b></size>  (에디터 전용 · 배포 빌드에 없음)", new GUIStyle(GUI.skin.label) { richText = true });
        GUILayout.Space(8);

        GUI.enabled = !manager.IsBusy;

        GUILayout.BeginHorizontal();
        GUILayout.Label("닉네임", GUILayout.Width(70));
        nickname = GUILayout.TextField(nickname, 8);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("맵");
        if (mapPaths.Count == 0) GUILayout.Label("(Assets/Scenes/Play 에 맵 씬이 없음)");
        else mapIndex = GUILayout.SelectionGrid(mapIndex, mapPaths.ConvertAll(TestModeManager.MapName).ToArray(), 2);

        GUILayout.Space(6);
        GUILayout.Label("시작 캐릭터 (게임 중 디버그 창에서 언제든 변경 가능)");
        if (characterIds.Count > 0)
            characterIndex = GUILayout.SelectionGrid(characterIndex, characterIds.ToArray(), 3);

        GUILayout.Space(12);
        if (GUILayout.Button("혼자 플레이", GUILayout.Height(36)))
            Play($"SOLO{Random.Range(100000, 1000000)}", 1);

        GUILayout.Space(10);
        GUILayout.Label("같이 플레이 — 같은 방 코드끼리 입장 (먼저 들어온 사람이 방장, 맵은 방장 기준)");
        GUILayout.BeginHorizontal();
        GUILayout.Label("방 코드", GUILayout.Width(70));
        roomCode = GUILayout.TextField(roomCode, 12);
        GUILayout.EndHorizontal();
        if (GUILayout.Button("같이 플레이 (방 만들기 / 참가)", GUILayout.Height(36)))
            Play(roomCode.Trim().ToUpperInvariant(), MultiMaxPlayers);

        GUI.enabled = true;

        if (!string.IsNullOrEmpty(manager.Status))
        {
            GUILayout.Space(8);
            GUILayout.Label(manager.Status);
        }

        GUILayout.Space(12);
        GUILayout.Label("· 창 두 개: Window > Play Mode > Scenarios 에서 추가 에디터 1개 넣은 시나리오로 Play → 두 창 모두 '같이 플레이'\n" +
                        "· 다른 테스터: 방장의 방 코드를 알려주고 같은 코드로 '같이 플레이'\n" +
                        "· 게임 중 F1 또는 우측 상단 TEST 버튼: 맵 변경 / 캐릭터 변경");

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void Play(string code, int maxPlayers)
    {
        if (mapPaths.Count == 0 || string.IsNullOrEmpty(code)) return;

        GPGS_BACKEND.SetTestNickname(string.IsNullOrWhiteSpace(nickname) ? "Tester" : nickname.Trim());
        var manager = TestModeManager.Instance;
        if (characterIds.Count > 0) manager.CharacterId = characterIds[characterIndex];
        manager.StartSession(code, mapPaths[mapIndex], maxPlayers);
    }
}
#endif
