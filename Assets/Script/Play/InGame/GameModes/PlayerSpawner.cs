using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerSpawner : 캐릭터 생성 / 스폰 위치 / 부활 / 재접속 복구  (기존 PlaySceneNetworkSpawner 대체)
//
//  포톤은 "방장이 모든 캐릭터를 서버에 만들어" 주었지만, 릴레이 방식에서는
//  모든 기기가 "같은 명단"을 보고 각자 모든 캐릭터를 자기 화면에 만든다.
//   - 누가 어떤 캐릭터인지 : MatchFlow 가 Countdown 에서 확정해 모두에게 알려 준 CharacterId
//   - 어디에 서는지        : 팀 스폰 포인트[팀 안 순번]  → 모든 기기가 같은 위치를 계산
//   - 내 캐릭터만 입력/물리를 책임지고, 남의 캐릭터는 받은 위치대로만 움직인다. (PlayerEntity.IsMine)
//
//  배치: GameRoot 오브젝트 (MatchFlow 와 같은 곳)
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerSpawner : MonoBehaviour, INetStateProvider
{
    public static PlayerSpawner Instance { get; private set; }

    [Header("캐릭터 목록")]
    public CharacterCatalog catalog;

    [Header("스폰 포인트 (팀별)")]
    public Transform[] redSpawnPoints;
    public Transform[] blueSpawnPoints;
    [Tooltip("스폰했을 때 바라보는 방향 (+1 오른쪽, -1 왼쪽)")]
    public float redFacing = 1f;
    public float blueFacing = -1f;

    [Header("생성 위치 부모 (선택)")]
    public Transform avatarRoot;

    [Header("카메라")]
    public bool followLocalCamera = true;

    public bool AvatarsSpawned { get; private set; }
    public PlayerEntity LocalEntity { get { return PlayerEntity.Local; } }
    /// <summary>내 캐릭터가 부활하기까지 남은 시간 (살아 있으면 0)</summary>
    public float RespawnRemaining { get { return respawnEndTime > Time.time ? respawnEndTime - Time.time : 0f; } }

    public event System.Action AvatarsReady;

    private InGameNet net;
    private MatchFlow flow;
    private Coroutine respawnRoutine;
    private float respawnEndTime;
    private byte[] pendingPlayerState;

    public ushort StateId { get { return NetStateIds.Players; } }

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    private void Awake()
    {
        Instance = this;
        if (GetComponent<CombatRouter>() == null) gameObject.AddComponent<CombatRouter>();
    }

    private void OnEnable()
    {
        net = InGameNet.Ensure();
        net.RegisterStateProvider(this);
        net.PlayersChanged += HandlePlayersChanged;
        PlayerEntity.OnAnyDeath += HandleAnyDeath;
    }

    private void Start()
    {
        flow = MatchFlow.Instance;
        if (flow == null) { NetLog.Error("PlayerSpawner: MatchFlow 가 씬에 없습니다."); return; }
        flow.OnPhaseChanged += HandlePhaseChanged;
        flow.OnSpawnStage += HandleSpawnStage;

        // 이미 진행 중인 단계에서 늦게 시작된 경우(재접속 등) 대비
        if (flow.Phase == MatchPhase.Playing || (flow.Phase == MatchPhase.Countdown && flow.SpawnStageStarted)) SpawnAll(true);
    }

    private void OnDisable()
    {
        if (net != null)
        {
            net.UnregisterStateProvider(this);
            net.PlayersChanged -= HandlePlayersChanged;
        }
        PlayerEntity.OnAnyDeath -= HandleAnyDeath;
        if (flow != null)
        {
            flow.OnPhaseChanged -= HandlePhaseChanged;
            flow.OnSpawnStage -= HandleSpawnStage;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void HandlePhaseChanged(MatchPhase phase, bool restored)
    {
        // 보통은 카운트다운이 끝난 직후(OnSpawnStage)에 이미 만들어져 있다. 못 만들었다면 Playing 에서라도 만든다.
        if (phase == MatchPhase.Playing) SpawnAll(restored);
        else if (phase == MatchPhase.Result) CancelRespawn();
    }

    /// <summary>게임 시작 대기 카운트다운이 끝나고 패널이 닫혔다 → 캐릭터를 만들고 카메라 줌인</summary>
    private void HandleSpawnStage()
    {
        SpawnAll(false);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 스폰

    public Transform[] PointsOf(NetTeam team)
    {
        return team == NetTeam.Red ? redSpawnPoints : team == NetTeam.Blue ? blueSpawnPoints : null;
    }

    public float FacingOf(NetTeam team) { return team == NetTeam.Blue ? blueFacing : redFacing; }

    /// <summary>게임 시작 위치: 팀 안 순번으로 고정 (모든 기기가 같은 값)</summary>
    public Vector2 InitialSpawnPosition(NetPlayer player)
    {
        var points = PointsOf(player.Team);
        if (points == null || points.Length == 0) return Vector2.zero;
        var point = points[Mathf.Abs(player.TeamIndex) % points.Length];
        return point != null ? (Vector2)point.position : Vector2.zero;
    }

    /// <summary>부활 위치: 팀 스폰 포인트 중 무작위 (내 기기에서만 호출)</summary>
    public Vector2 RandomSpawnPosition(NetTeam team)
    {
        var points = PointsOf(team);
        if (points == null || points.Length == 0) return Vector2.zero;
        var point = points[Random.Range(0, points.Length)];
        return point != null ? (Vector2)point.position : Vector2.zero;
    }

    private void SpawnAll(bool restored)
    {
        if (AvatarsSpawned) return;
        if (catalog == null) { NetLog.Error("PlayerSpawner: CharacterCatalog 가 비어 있습니다."); return; }

        net.RefreshLocalSessionId();                     // 내 세션 번호가 0 이면 한 번 더 읽어 온다 (내 캐릭터 판별용)

        var modeType = flow != null && flow.Config != null ? flow.Config.modeType : GameModeType.TDM;
        var players = new List<NetPlayer>(net.Players);
        Debug.Log("[Camera] PlayerSpawner: 캐릭터 생성 시작. 인원=" + players.Count + ", 내 세션=" + net.LocalSessionId + ", 스폰포인트 Red=" + CountPoints(redSpawnPoints) + " Blue=" + CountPoints(blueSpawnPoints));

        foreach (var player in players)
        {
            if (PlayerEntity.Get(player.SessionId) != null) continue;

            string id = string.IsNullOrEmpty(player.CharacterId) && flow != null ? flow.Config.defaultCharacterId : player.CharacterId;
            var def = catalog.Get(id);
            if (def == null)
            {
                NetLog.Warn("PlayerSpawner: 캐릭터 정의를 찾지 못했습니다. id='" + id + "' 유저=" + player.Nickname);
                continue;
            }
            var prefab = def.GetPrefab(modeType);
            if (prefab == null)
            {
                NetLog.Warn("PlayerSpawner: 캐릭터 프리팹이 비어 있습니다. id=" + id);
                continue;
            }
            player.CharacterId = def.characterId;

            Vector2 pos = InitialSpawnPosition(player);
            float facing = FacingOf(player.Team);
            if (CountPoints(PointsOf(player.Team)) == 0)
                NetLog.Warn("PlayerSpawner: " + player.Team + " 팀 스폰 포인트가 비어 있어 (0,0) 에 생성합니다. 인스펙터에서 지정하세요.");

            var go = Instantiate(prefab, pos, Quaternion.identity, avatarRoot);
            var entity = go.GetComponent<PlayerEntity>();
            if (entity == null)
            {
                NetLog.Error("PlayerSpawner: 프리팹에 PlayerEntity 가 없습니다. " + prefab.name);
                Destroy(go);
                continue;
            }

            entity.Init(player, def);
            entity.Motor.Teleport(pos, facing);
            if (entity.Sync != null) entity.Sync.SnapTo(pos);
            if (player.IsForfeited) entity.SetPresent(false);
        }

        AvatarsSpawned = true;

        // 재접속이면 스냅샷으로 받아 둔 위치/체력을 적용
        if (pendingPlayerState != null)
        {
            ApplyPlayerState(pendingPlayerState);
            pendingPlayerState = null;
        }

        FollowLocal(restored);
        if (AvatarsReady != null) AvatarsReady();
    }

    private static int CountPoints(Transform[] points)
    {
        if (points == null) return 0;
        int n = 0;
        for (int i = 0; i < points.Length; i++) if (points[i] != null) n++;
        return n;
    }

    /// <summary>화면에 만들어진 캐릭터 중 "내 캐릭터"를 찾는다. (PlayerEntity.Local 이 비어 있어도 세션 번호로 한 번 더 찾는다)</summary>
    private PlayerEntity FindLocalEntity()
    {
        if (PlayerEntity.Local != null) return PlayerEntity.Local;

        uint mySession = net != null ? net.LocalSessionId : 0u;
        var all = PlayerEntity.All;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (e == null) continue;
            if (e.IsMine) return e;
            if (mySession != 0u && e.SessionId == mySession) return e;
            if (e.Info != null && e.Info.IsLocal) return e;
        }
        return null;
    }

    /// <summary>내 캐릭터를 카메라가 따라가게 하고, 줌인이 끝나면 MatchFlow 에 "준비 완료"를 알린다.</summary>
    private void FollowLocal(bool snap)
    {
        var cam = CameraZoomController.Instance;
        var local = FindLocalEntity();

        if (!followLocalCamera)
        {
            NetLog.Warn("PlayerSpawner: followLocalCamera 가 꺼져 있어 카메라가 캐릭터를 따라가지 않습니다.");
            ReportZoomReady();
            return;
        }
        if (cam == null)
        {
            NetLog.Error("PlayerSpawner: CameraZoomController 가 씬에 없습니다. CM PlayerCamera 오브젝트에 붙어 있는지 확인하세요.");
            ReportZoomReady();                       // 줌인할 카메라가 없으면 기다릴 필요가 없다
            return;
        }
        if (local == null)
        {
            NetLog.Error("PlayerSpawner: 내 캐릭터를 찾지 못했습니다. 내 세션=" + (net != null ? net.LocalSessionId : 0u)
                + ", 생성된 캐릭터 수=" + PlayerEntity.All.Count + " (캐릭터 생성 실패 경고가 위에 있는지 확인)");
            ReportZoomReady();
            return;
        }

        Debug.Log("[Camera] PlayerSpawner: 카메라 대상 = " + local.name + " 위치=" + local.transform.position + (snap ? " (즉시)" : " (줌인)"));
        cam.OnZoomInCompleted -= HandleZoomInDone;
        cam.OnZoomInCompleted += HandleZoomInDone;
        if (snap) cam.SnapToTarget(local.transform);
        else cam.ZoomInToTarget(local.transform);
    }

    private void HandleZoomInDone()
    {
        var cam = CameraZoomController.Instance;
        if (cam != null) cam.OnZoomInCompleted -= HandleZoomInDone;
        ReportZoomReady();
    }

    private void ReportZoomReady()
    {
        if (flow != null) flow.ReportZoomReady();
    }

    private void HandlePlayersChanged()
    {
        foreach (var p in net.Players)
        {
            var entity = PlayerEntity.Get(p.SessionId);
            if (entity != null && p.IsForfeited && entity.IsPresent) entity.SetPresent(false);
        }
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 부활

    private void HandleAnyDeath(PlayerEntity victim, uint killer)
    {
        if (!victim.IsMine) return;
        if (flow == null || flow.Phase != MatchPhase.Playing) return;

        var mode = GameModeBase.Current;
        if (mode != null && !mode.AllowRespawn) return;                 // BOMB: 라운드 중 부활 없음

        float delay = flow.Config.respawnSeconds;
        if (respawnRoutine != null) StopCoroutine(respawnRoutine);
        respawnEndTime = Time.time + delay;
        respawnRoutine = StartCoroutine(Co_Respawn(victim, delay));
    }

    private IEnumerator Co_Respawn(PlayerEntity entity, float delay)
    {
        // Time.time 대신 실제 경과로 기다려 앱이 잠깐 백그라운드에 갔다 와도 즉시 부활할 수 있게 한다.
        double end = NetClock.Now + delay;
        while (NetClock.Now < end) yield return null;

        respawnRoutine = null;
        respawnEndTime = 0f;
        if (entity == null || entity.Health == null || !entity.Health.IsDead) yield break;
        if (flow == null || flow.Phase != MatchPhase.Playing) yield break;

        RespawnLocal(entity);
    }

    /// <summary>내 캐릭터를 팀 스폰 포인트 중 하나에 되살린다. (부활 / BOMB 라운드 시작)</summary>
    public void RespawnLocal(PlayerEntity entity, bool useInitialPoint = false)
    {
        if (entity == null || !entity.IsMine) return;
        var info = entity.Info;
        Vector2 pos = useInitialPoint ? InitialSpawnPosition(info) : RandomSpawnPosition(info.Team);
        float invincible = flow != null && flow.Config != null ? flow.Config.invincibleSeconds : 2f;
        entity.RespawnLocal(pos, FacingOf(info.Team), invincible);
    }

    private void CancelRespawn()
    {
        if (respawnRoutine != null) { StopCoroutine(respawnRoutine); respawnRoutine = null; }
        respawnEndTime = 0f;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재접속 복구 (INetStateProvider)

    // v1: [버전][인원]{[세션][x][y][방향 byte][체력 ushort][사망 bool]}
    public void WriteState(NetWriter writer)
    {
        writer.WriteByte(1);
        var list = PlayerEntity.All;
        writer.WriteByte((byte)Mathf.Min(list.Count, 255));
        for (int i = 0; i < list.Count && i < 255; i++)
        {
            var e = list[i];
            Vector2 pos = e.transform.position;
            writer.WriteUInt(e.SessionId);
            writer.WriteVector2(pos);
            writer.WriteByte(e.Motor != null && e.Motor.Facing < 0f ? (byte)0 : (byte)1);
            writer.WriteUShort((ushort)Mathf.Clamp(e.Health != null ? e.Health.CurrentHP : 0, 0, ushort.MaxValue));
            writer.WriteBool(e.Health != null && e.Health.IsDead);
        }
    }

    public void ReadState(NetReader reader)
    {
        // 아직 캐릭터가 만들어지지 않았다면(단계 복구 순서) 보관했다가 SpawnAll 직후 적용
        var copy = new NetWriter(64);
        byte version = reader.ReadByte();
        if (version != 1) { NetLog.Warn("PlayerSpawner: 알 수 없는 스냅샷 버전 " + version); return; }

        int count = reader.ReadByte();
        copy.WriteByte(version);
        copy.WriteByte((byte)count);
        for (int i = 0; i < count; i++)
        {
            copy.WriteUInt(reader.ReadUInt());
            copy.WriteVector2(reader.ReadVector2());
            copy.WriteByte(reader.ReadByte());
            copy.WriteUShort(reader.ReadUShort());
            copy.WriteBool(reader.ReadBool());
        }

        byte[] data = copy.ToArray();
        if (AvatarsSpawned) ApplyPlayerState(data);
        else pendingPlayerState = data;
    }

    private void ApplyPlayerState(byte[] data)
    {
        var r = new NetReader(data);
        r.ReadByte();                      // 버전
        int count = r.ReadByte();
        for (int i = 0; i < count; i++)
        {
            uint session = r.ReadUInt();
            Vector2 pos = r.ReadVector2();
            float facing = r.ReadByte() == 0 ? -1f : 1f;
            int hp = r.ReadUShort();
            bool dead = r.ReadBool();

            var e = PlayerEntity.Get(session);
            if (e == null) continue;

            if (e.Motor != null) e.Motor.Teleport(pos, facing);
            if (e.Sync != null) e.Sync.SnapTo(pos);
            if (e.Health != null) e.Health.RestoreState(hp, dead, 0f);

            // 내 캐릭터가 사망 상태로 복구되면 부활 타이머를 다시 건다
            if (e.IsMine && dead && flow != null && flow.Phase == MatchPhase.Playing)
                HandleAnyDeath(e, 0);
        }
        FollowLocal(true);
    }

    #endregion
}
