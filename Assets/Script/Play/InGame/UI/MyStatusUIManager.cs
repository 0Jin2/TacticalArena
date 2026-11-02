using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// MyStatusUIManager : "내 상태" 칸 생성  (기존 MyStatusUIManager 대체)
//   GameHud.myStatusParent 아래에 GameHud.myStatusItemPrefab 을 만들어 내 캐릭터의 체력과 연결한다.
// ─────────────────────────────────────────────────────────────────────────────

public class MyStatusUIManager : MonoBehaviour
{
    public static MyStatusUIManager Instance { get; private set; }

    [Tooltip("캐릭터 아이콘을 찾을 목록. 비워 두면 PlayerSpawner 의 목록을 사용")]
    public CharacterCatalog catalogOverride;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void CreateMyStatusUI()
    {
        var hud = GameHud.Instance;
        if (hud == null || hud.myStatusParent == null || hud.myStatusItemPrefab == null)
        {
            NetLog.Warn("MyStatusUIManager: GameHud 또는 myStatusParent/myStatusItemPrefab 이 없습니다.");
            return;
        }

        var me = PlayerEntity.Local;
        if (me == null || me.Health == null || me.Info == null)
        {
            NetLog.Warn("MyStatusUIManager: 내 캐릭터를 찾지 못했습니다.");
            return;
        }

        var catalog = catalogOverride != null ? catalogOverride
            : (PlayerSpawner.Instance != null ? PlayerSpawner.Instance.catalog : null);
        Sprite icon = me.Def != null ? me.Def.characterIcon : (catalog != null ? catalog.GetIcon(me.CharacterId) : null);

        // 기존 칸 제거
        var parent = hud.myStatusParent.transform;
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);

        var go = Instantiate(hud.myStatusItemPrefab, parent);
        var item = go.GetComponent<MyStatusItem>();
        if (item == null) { NetLog.Warn("MyStatusUIManager: myStatusItemPrefab 에 MyStatusItem 이 없습니다."); return; }

        item.Initialize(me.Nickname, icon, me.Health);
    }
}
