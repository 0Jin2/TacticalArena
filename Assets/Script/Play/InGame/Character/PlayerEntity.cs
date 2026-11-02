using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerEntity : 캐릭터 프리팹의 "신분증" + 부품 연결 허브
//
//  - 누구의 캐릭터인가(세션/닉네임/팀), 내 것인가(IsMine) 를 알려준다.
//  - 같은 프리팹 안의 다른 부품(Motor/Health/Combat/...)을 한곳에서 찾게 해 준다.
//  - 모든 캐릭터 프리팹 루트에 1개 붙인다. 캐릭터 수치는 stat 에 CharacterStatData 를 끼운다.
//
//  내 캐릭터(IsMine)  : 입력을 받고 이동/체력/탄약을 "내 기기가" 책임진다. 결과를 PlayerNetSync 로 전송.
//  남의 캐릭터        : 받은 패킷대로만 움직이는 "인형". 판정은 하지 않는다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerEntity : MonoBehaviour
{
    [Header("캐릭터 수치 (CharacterStatData 에셋)")]
    public CharacterStatData stat;

    // ── 전체 목록 ───────────────────────────────────────────────────────────
    public static readonly List<PlayerEntity> All = new List<PlayerEntity>();
    public static PlayerEntity Local { get; private set; }
    public static event Action<PlayerEntity> OnSpawned;
    public static event Action<PlayerEntity> OnDespawned;
    public static event Action<PlayerEntity, uint> OnAnyDeath;     // (죽은 캐릭터, 킬러 세션) 모든 기기
    public static event Action<PlayerEntity> OnAnyRevived;         // 부활/스폰된 캐릭터

    public static PlayerEntity Get(uint sessionId)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].SessionId == sessionId) return All[i];
        return null;
    }

    // ── 신분 ────────────────────────────────────────────────────────────────
    public NetPlayer Info { get; private set; }
    public CharacterDef Def { get; private set; }
    public bool IsInitialized { get; private set; }
    public bool IsMine { get; private set; }
    public bool IsPresent { get; private set; }

    public uint SessionId { get { return Info != null ? Info.SessionId : 0u; } }
    public NetTeam Team { get { return Info != null ? Info.Team : NetTeam.None; } }
    public string Nickname { get { return Info != null ? Info.Nickname : string.Empty; } }
    public string CharacterId { get { return Info != null ? Info.CharacterId : string.Empty; } }

    // ── 같은 프리팹의 부품들 ────────────────────────────────────────────────
    public PlayerMotor Motor { get; private set; }
    public PlayerHealth Health { get; private set; }
    public PlayerCombat Combat { get; private set; }
    public PlayerInput Input { get; private set; }
    public PlayerAnimator Anim { get; private set; }
    public PlayerNetSync Sync { get; private set; }
    public SkillBase Skill { get; private set; }
    public PlayerAudio Audio { get; private set; }

    public event Action<PlayerEntity> Initialized;

    private readonly List<Renderer> shownRenderers = new List<Renderer>();
    private Collider2D[] colliders;
    private Rigidbody2D body;

    private void Awake()
    {
        Motor = GetComponent<PlayerMotor>();
        Health = GetComponent<PlayerHealth>();
        Combat = GetComponent<PlayerCombat>();
        Input = GetComponent<PlayerInput>();
        Anim = GetComponent<PlayerAnimator>();
        Sync = GetComponent<PlayerNetSync>();
        Skill = GetComponent<SkillBase>();
        Audio = GetComponent<PlayerAudio>();
        if (Audio == null) Audio = gameObject.AddComponent<PlayerAudio>();
        body = GetComponent<Rigidbody2D>();
        colliders = GetComponents<Collider2D>();
        IsPresent = true;
    }

    /// <summary>PlayerSpawner 가 프리팹을 만든 직후 호출한다.</summary>
    public void Init(NetPlayer info, CharacterDef def)
    {
        Info = info;
        Def = def;
        var net = InGameNet.Instance;
        IsMine = net != null && info.SessionId == net.LocalSessionId;
        gameObject.name = "Player_" + info.Nickname + (IsMine ? " (ME)" : "");

        shownRenderers.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.enabled) shownRenderers.Add(r);

        if (stat == null) NetLog.Warn("PlayerEntity: stat(CharacterStatData)이 비어 있습니다. 프리팹 확인: " + gameObject.name);

        All.Add(this);
        if (IsMine) Local = this;

        // 부품 초기화 (순서 중요: Motor/Health 가 먼저)
        if (Motor != null) Motor.Setup(this);
        if (Health != null) Health.Setup(this);
        if (Combat != null) Combat.Setup(this);
        if (Anim != null) Anim.Setup(this);
        if (Input != null) Input.Setup(this);
        if (Skill != null) Skill.Setup(this);
        if (Sync != null) Sync.Setup(this);

        IsInitialized = true;
        if (Initialized != null) Initialized(this);
        if (OnSpawned != null) OnSpawned(this);
    }

    /// <summary>초기화가 끝난 뒤(이미 끝났으면 즉시) 콜백을 실행한다. 체력바/이름표 같은 부속품이 사용.</summary>
    public void WhenReady(Action<PlayerEntity> callback)
    {
        if (IsInitialized) callback(this);
        else Initialized += callback;
    }

    /// <summary>입력/조작 가능 여부: 내 캐릭터 + 살아있음 + 게임 진행 중 + 서버 연결 정상</summary>
    public bool ControlEnabled
    {
        get
        {
            if (!IsMine || !IsPresent) return false;
            if (Health != null && Health.IsDead) return false;

            var flow = MatchFlow.Instance;
            if (flow != null && flow.Phase != MatchPhase.Playing) return false;

            var net = InGameNet.Instance;
            if (net != null && net.Connection != null && !net.Connection.IsLocalPlayable) return false;
            return true;
        }
    }

    /// <summary>캐릭터를 화면/물리에서 숨기거나 되살린다. (BOMB 공격팀 늦은 스폰 등)</summary>
    public void SetPresent(bool present)
    {
        if (IsPresent == present) return;
        IsPresent = present;

        for (int i = 0; i < shownRenderers.Count; i++)
            if (shownRenderers[i] != null) shownRenderers[i].enabled = present;
        if (colliders != null)
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) colliders[i].enabled = present;
        if (body != null) body.simulated = present;
    }

    // ═════════════════════════════════════════════════════════════════════════
    #region 사망 / 부활

    /// <summary>PlayerHealth 가 사망 처리를 끝낸 뒤 호출한다. (모든 기기)</summary>
    public void NotifyDeath(uint killer)
    {
        if (OnAnyDeath != null) OnAnyDeath(this, killer);
    }

    /// <summary>
    /// [내 캐릭터] 지정한 위치에서 되살린다. 체력/탄창을 채우고, 무적 시간을 주고, PlayerSpawn 을 알린다.
    /// 최초 스폰(게임 시작)에도 쓸 수 있다.
    /// </summary>
    public void RespawnLocal(Vector2 position, float facing, float invincibleSeconds)
    {
        if (!IsMine) return;

        SetPresent(true);
        if (Motor != null) { Motor.ResetState(); Motor.Teleport(position, facing); }
        if (Health != null) Health.Revive(invincibleSeconds);
        if (Combat != null) Combat.RefillOnRespawn();
        if (Skill != null) Skill.OnRespawn();
        if (Input != null) Input.ClearHeld();

        var net = InGameNet.Instance;
        if (net != null)
        {
            net.Send(NetPacketType.PlayerSpawn, w =>
            {
                w.WriteVector2(position);
                w.WriteByte(facing < 0f ? (byte)0 : (byte)1);
                w.WriteFloat(invincibleSeconds);
            }, true);
        }
        if (Sync != null) Sync.SendNow();
        if (OnAnyRevived != null) OnAnyRevived(this);
    }

    /// <summary>[남의 캐릭터] PlayerSpawn 패킷을 받아 되살린다.</summary>
    public void ApplyRemoteSpawn(NetReader r)
    {
        if (IsMine) return;

        Vector2 position = r.ReadVector2();
        float facing = r.ReadByte() == 0 ? -1f : 1f;
        float invincible = r.ReadFloat();

        SetPresent(true);
        if (Motor != null) Motor.Teleport(position, facing);
        if (Sync != null) Sync.SnapTo(position);
        if (Health != null) Health.Revive(invincible);
        if (Skill != null) Skill.OnRespawn();
        if (OnAnyRevived != null) OnAnyRevived(this);
    }

    #endregion

    private void OnDestroy()
    {
        All.Remove(this);
        if (Local == this) Local = null;
        if (OnDespawned != null) OnDespawned(this);
    }
}
