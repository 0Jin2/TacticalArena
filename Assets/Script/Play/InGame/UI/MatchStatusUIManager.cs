using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MatchStatusUIManager : 팀 현황판(전원의 킬/데스/사망) 관리  (기존 MatchStatusUIManager 대체)
//
//  - GameHud 가 줌인이 끝나면 CreateAllPlayerStatusItems() 를 호출한다. (기존과 같은 시점)
//  - 값 갱신은 MatchState 의 이벤트(킬/데스/사망/부활)로 필요할 때만 한다. (기존: 매 프레임 전체 갱신)
//  - 팀 점수 갱신은 GameHud 가 담당한다.
// ─────────────────────────────────────────────────────────────────────────────

public class MatchStatusUIManager : MonoBehaviour
{
    public static MatchStatusUIManager Instance { get; private set; }

    public Transform redTeamParent;
    public Transform blueTeamParent;
    public GameObject matchStatusItemPrefab;
    [Tooltip("캐릭터 아이콘을 찾을 목록. 비워 두면 PlayerSpawner 의 목록을 사용")]
    public CharacterCatalog catalogOverride;

    private readonly Dictionary<uint, MatchStatusItem> items = new Dictionary<uint, MatchStatusItem>();
    private MatchState state;
    private InGameNet net;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (Instance == this) Instance = null;
    }

    public void CreateAllPlayerStatusItems()
    {
        net = InGameNet.Ensure();
        state = MatchState.Instance;
        Unsubscribe();

        // 다시 만들 때 기존 칸 제거 (재접속 후 재생성 등)
        foreach (var kv in items) if (kv.Value != null) Destroy(kv.Value.gameObject);
        items.Clear();

        if (matchStatusItemPrefab == null) { NetLog.Warn("MatchStatusUIManager: matchStatusItemPrefab 이 비어 있습니다."); return; }

        var catalog = catalogOverride != null ? catalogOverride
            : (PlayerSpawner.Instance != null ? PlayerSpawner.Instance.catalog : null);

        foreach (var player in net.Players)
        {
            if (player.IsForfeited) continue;
            var parent = player.Team == NetTeam.Red ? redTeamParent : blueTeamParent;
            if (parent == null) continue;

            var go = Instantiate(matchStatusItemPrefab, parent);
            var item = go.GetComponent<MatchStatusItem>();
            if (item == null) { Destroy(go); continue; }

            Sprite icon = catalog != null ? catalog.GetIcon(player.CharacterId) : null;
            item.Initialize(player, icon, player.IsLocal);
            items[player.SessionId] = item;
            Refresh(player.SessionId);
        }

        if (state != null)
        {
            state.OnStatChanged += Refresh;
            state.OnPlayerDied += HandleDiedOrRevived;
            state.OnPlayerRevived += Refresh;
        }
        net.PlayersChanged += RefreshAll;
    }

    private void Unsubscribe()
    {
        if (state != null)
        {
            state.OnStatChanged -= Refresh;
            state.OnPlayerDied -= HandleDiedOrRevived;
            state.OnPlayerRevived -= Refresh;
        }
        if (net != null) net.PlayersChanged -= RefreshAll;
    }

    private void HandleDiedOrRevived(uint victim, uint killer)
    {
        Refresh(victim);
    }

    private void RefreshAll()
    {
        foreach (var kv in items) Refresh(kv.Key);
    }

    private void Refresh(uint session)
    {
        MatchStatusItem item;
        if (state == null || !items.TryGetValue(session, out item) || item == null) return;

        item.UpdateKillDeath(state.GetKills(session), state.GetDeaths(session));
        item.SetDead(state.IsDead(session));

        var info = net != null ? net.GetPlayer(session) : null;
        item.SetOffline(info != null && !info.IsOnline && !info.IsForfeited);
    }
}
