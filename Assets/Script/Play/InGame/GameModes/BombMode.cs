using System;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// BombMode : 폭탄 설치/해제 모드  (기존 BombModeHandler 대체)
//
//  규칙 (기존과 동일)
//   - 총 5라운드, 먼저 3승(= 총 라운드/2 + 1)하면 매치 종료
//   - 1라운드: 레드 = 설치팀, 블루 = 해제팀. 라운드마다 역할 교대
//   - 라운드 시간 300초. 폭탄이 설치되면 남은 시간이 60초(폭발 카운트다운)로 바뀜
//   - 라운드 중 부활 없음
//   - 설치 전 한 팀이 전멸 → 생존 팀 승리
//   - 설치 후: 해제팀 전멸 → 설치팀 즉시 승 / 해제 성공 → 해제팀 승 / 60초 경과(폭발) → 설치팀 승
//   - 설치 못 하고 300초 경과 → 해제팀 승
//   - 설치 6초, 해제 6초 (진행 중 거리 벗어나면 취소). 라운드 사이 2초 대기
//
//  기존에 없던(또는 고친) 부분
//   - 라운드가 시작되면 모든 캐릭터가 시작 위치에서 체력을 채우고 되살아남 (기존은 죽은 채로 다음 라운드 → 사실상 버그)
//   - 설치/해제 버튼을 다시 눌러 취소하면 호스트에도 취소가 전달됨 (기존은 화면만 취소되고 진행이 계속됨)
//   - 한쪽 팀이 비어 있는 1인 테스트에서는 '전멸 판정'을 건너뜀
//
//  네트워크: 호스트가 모든 판정(진행 시간, 거리, 전멸, 승패)을 하고 ModeEvent 로 결과만 알린다.
//   라운드 승수는 이 모드가 직접 관리한다. (MatchState 의 팀 점수 대신 mode.ScoreOf() 를 읽을 것)
// ─────────────────────────────────────────────────────────────────────────────

public class BombMode : GameModeBase
{
    public static BombMode Instance { get; private set; }

    public enum RoundEndReason : byte { None = 0, Eliminated = 1, TimeOut = 2, Exploded = 3, Defused = 4 }

    [Header("라운드 규칙")]
    public int totalRounds = 5;
    [Tooltip("1라운드에 폭탄을 설치하는 팀")]
    public NetTeam firstInstallerTeam = NetTeam.Red;
    public float roundSeconds = 300f;
    [Tooltip("폭탄 설치 후 폭발까지의 시간")]
    public float bombCountdownSeconds = 60f;
    public float plantSeconds = 6f;
    public float defuseSeconds = 6f;
    [Tooltip("라운드가 끝나고 다음 라운드가 시작되기까지의 대기 시간")]
    public float betweenRoundsSeconds = 2f;
    [Tooltip("라운드 시작 시 모든 캐릭터를 시작 위치로 되살린다. (1라운드는 이미 시작 위치이므로 제외)")]
    public bool respawnAtRoundStart = true;

    [Header("판정 보정")]
    [Tooltip("호스트가 보는 상대 위치는 약간 늦습니다. interactRange 에 이 배율을 곱한 거리까지는 허용")]
    public float hostRangeTolerance = 1.4f;
    [Tooltip("한쪽 팀에 아무도 없으면 전멸 판정을 건너뜁니다. (혼자 테스트용)")]
    public bool skipEliminationForEmptyTeam = true;

    [Header("HUD")]
    [Tooltip("라운드 승수 점수판을 보여 줄지. (기존에도 줌인 후 점수판에 라운드 승수가 표시됨)")]
    public bool showScoreBoard = true;
    public string roundFormat = "{0} / {1}";

    [Header("씬 오브젝트")]
    [Tooltip("비워 두면 씬의 BombSite 를 이름순으로 자동 수집")]
    public BombSite[] sites;

    [Header("안내 문구 (비우면 표시 안 함)")]
    public string msgBombPlanted = "폭탄이 설치되었습니다.";     // 해제팀에게만 + 방향 화살표
    public string msgRoundWin = "라운드 승리!";
    public string msgRoundLose = "라운드 패배";
    public string msgPlantLabel = "설치 중.";
    public string msgDefuseLabel = "해제 중.";

    // ── 패킷 종류 ───────────────────────────────────────────────────────────
    private const byte ReqInteract = 1;          // [site byte][isPlant bool]
    private const byte ReqCancel = 2;            // [site byte]
    private const byte EvRoundStart = 1;         // [round byte][installer byte][seconds float]
    private const byte EvInteractStart = 2;      // [site byte][isPlant bool][session uint][seconds float]
    private const byte EvInteractCancel = 3;     // [site byte]
    private const byte EvPlanted = 4;            // [site byte][bombSeconds float]
    private const byte EvRoundEnd = 5;           // [winner byte][reason byte][redWins byte][blueWins byte]

    // ── 모든 기기가 이벤트로 맞추는 상태 ─────────────────────────────────────
    public int CurrentRound { get; private set; }
    public NetTeam InstallerTeam { get; private set; } = NetTeam.Red;
    public NetTeam DefenderTeam { get { return Opposite(InstallerTeam); } }
    public bool RoundActive { get; private set; }
    public bool BombPlanted { get; private set; }
    public int PlantedSite { get; private set; } = -1;
    public int RedWins { get; private set; }
    public int BlueWins { get; private set; }
    public RoundEndReason LastEndReason { get; private set; }
    public NetTeam LastRoundWinner { get; private set; }

    private double roundEndAt;
    private double nextRoundAt;

    // 진행 중인 설치/해제 (한 번에 하나만)
    private bool interActive;
    private int interSite;
    private bool interIsPlant;
    private uint interSession;
    private double interStartAt;
    private float interDuration;

    // 호스트 전용 판정 상태
    private bool hostClosed;                       // 이번 라운드 승패를 이미 보냄 (중복 방지)
    private bool hostPlanted;                      // 설치를 확정해 보냄 (에코 도착 전 중복 설치 방지)
    private bool hostInterActive;
    private int hostInterSite;
    private bool hostInterIsPlant;
    private uint hostInterSession;
    private float hostInterElapsed, hostInterDuration;
    private float lastRoundStartSentAt = -100f;

    // 내 기기 UI 상태
    private BombSite localSite;
    private bool localInteracting;
    private float lastRequestAt = -100f;

    // ── HUD 가 읽는 값 ──────────────────────────────────────────────────────
    public bool InteractAvailable { get; private set; }
    public bool InteractIsPlant { get; private set; }
    public bool IsInteracting { get { return localInteracting; } }
    /// <summary>내가 진행 중인 설치/해제의 진행도 (0~1)</summary>
    public float InteractProgress { get; private set; }
    public string InteractLabel { get { return interIsPlant ? msgPlantLabel : msgDefuseLabel; } }
    public int WinsNeeded { get { return totalRounds / 2 + 1; } }

    /// <summary>해제팀에게 "폭탄이 설치되었습니다" + 방향 안내를 띄울 때 (대상 위치, 문구)</summary>
    public event Action<Transform, string> OnBombAlert;
    /// <summary>라운드가 끝났을 때 (승리 팀, 이유)</summary>
    public event Action<NetTeam, RoundEndReason> OnRoundEnded;
    /// <summary>새 라운드가 시작됐을 때 (라운드 번호)</summary>
    public event Action<int> OnRoundStarted;

    // ── GameModeBase 설정 ───────────────────────────────────────────────────
    public override bool AllowRespawn { get { return false; } }
    public override bool ShowScoreUI { get { return showScoreBoard; } }
    public override bool ShowGoalUI { get { return false; } }
    public override bool ShowRoundUI { get { return true; } }
    public override string RoundText { get { return string.Format(roundFormat, Mathf.Max(1, CurrentRound), totalRounds); } }

    public override float DisplaySeconds
    {
        get
        {
            if (CurrentRound == 0) return roundSeconds;
            if (!RoundActive) return 0f;
            return Mathf.Max(0f, (float)(roundEndAt - NetClock.Now));
        }
    }

    public override bool ShowTimer { get { return true; } }

    public override int ScoreOf(NetTeam team)
    {
        return team == NetTeam.Red ? RedWins : team == NetTeam.Blue ? BlueWins : 0;
    }

    protected override MatchState.ScoreRule ScoreRule { get { return MatchState.ScoreRule.Manual; } }

    // ═════════════════════════════════════════════════════════════════════════
    #region 수명

    protected override void Awake()
    {
        base.Awake();
        if (Current == this) Instance = this;
    }

    protected override void Start()
    {
        base.Start();
        CollectSites();
    }

    protected override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    private void CollectSites()
    {
        if (sites == null || sites.Length == 0)
        {
            var found = FindObjectsByType<BombSite>(FindObjectsSortMode.None);
            Array.Sort(found, (a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            sites = found;
        }
        for (int i = 0; i < sites.Length; i++)
        {
            if (sites[i] == null) continue;
            sites[i].siteIndex = i;
            sites[i].Deactivate();
        }
        if (sites.Length == 0) NetLog.Warn("BombMode: 씬에 BombSite 가 없습니다.");
    }

    public BombSite GetSite(int index)
    {
        return sites != null && index >= 0 && index < sites.Length ? sites[index] : null;
    }

    protected override void OnPlayingStarted(bool restored)
    {
        if (!restored)
        {
            ResetAllState();
        }
        RaiseModeChanged();
    }

    protected override void OnResultStarted()
    {
        CancelLocalInteractionUi();
        RoundActive = false;
        if (sites != null) foreach (var s in sites) if (s != null) s.Deactivate();
        InteractAvailable = false;
        RaiseModeChanged();
    }

    private void ResetAllState()
    {
        CurrentRound = 0;
        RedWins = BlueWins = 0;
        RoundActive = false;
        BombPlanted = false;
        PlantedSite = -1;
        interActive = false;
        hostClosed = false;
        hostPlanted = false;
        hostInterActive = false;
        localInteracting = false;
        InstallerTeam = firstInstallerTeam;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 모든 기기: 매 프레임 (표시 갱신)

    protected override void OnPlayingUpdate()
    {
        UpdateLocalPrompt();
        UpdateProgressDisplay();
    }

    private void UpdateLocalPrompt()
    {
        InteractAvailable = false;

        var me = PlayerEntity.Local;
        BombSite inside = FindLocalInsideSite();
        bool alive = me != null && me.Health != null && !me.Health.IsDead && me.Info != null;

        // 지점을 벗어났거나 죽었으면 진행 중이던 설치/해제를 취소
        if (localInteracting && (!alive || inside == null || inside.siteIndex != interSite || !RoundActive))
        {
            RequestCancelInteract();
        }

        if (localSite != null && localSite != inside) localSite.SetLocalPreview(false);
        localSite = inside;

        if (!RoundActive || !alive || inside == null || !inside.IsActive)
        {
            if (inside != null) inside.SetLocalPreview(false);
            return;
        }

        bool canPlant = !BombPlanted && me.Team == InstallerTeam && !inside.IsPlantedHere;
        bool canDefuse = BombPlanted && me.Team == DefenderTeam && inside.IsPlantedHere;

        // 다른 사람이 이미 진행 중이면 버튼을 숨김 (내가 하는 중이면 취소 버튼으로 유지)
        if (interActive && interSession != me.SessionId) { canPlant = false; canDefuse = false; }

        if (canPlant || canDefuse || localInteracting)
        {
            InteractAvailable = true;
            InteractIsPlant = localInteracting ? interIsPlant : canPlant;
            inside.SetLocalPreview(canPlant && !localInteracting);
        }
        else inside.SetLocalPreview(false);
    }

    private BombSite FindLocalInsideSite()
    {
        if (sites == null) return null;
        for (int i = 0; i < sites.Length; i++)
            if (sites[i] != null && sites[i].LocalInside) return sites[i];
        return null;
    }

    private void UpdateProgressDisplay()
    {
        if (!interActive)
        {
            InteractProgress = 0f;
            return;
        }
        float p = interDuration > 0f ? Mathf.Clamp01((float)((NetClock.Now - interStartAt) / interDuration)) : 1f;
        var site = GetSite(interSite);
        if (site != null) site.ShowProgress(interIsPlant, p);
        if (localInteracting) InteractProgress = p;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 내 기기: 버튼 입력 (HUD 가 호출)

    /// <summary>설치/해제 버튼. 진행 중이 아니면 시작 요청, 진행 중이면 취소.</summary>
    public void ToggleInteract()
    {
        if (!RoundActive) return;
        if (localInteracting) { RequestCancelInteract(); return; }
        if (!InteractAvailable || localSite == null) return;
        if (Time.unscaledTime - lastRequestAt < 0.3f) return;      // 연타 방지
        lastRequestAt = Time.unscaledTime;

        int site = localSite.siteIndex;
        bool plant = InteractIsPlant;
        SendRequest(ReqInteract, w => { w.WriteByte((byte)site); w.WriteBool(plant); });
    }

    private void RequestCancelInteract()
    {
        if (!localInteracting) return;
        int site = interSite;
        SendRequest(ReqCancel, w => w.WriteByte((byte)site));
        // 화면은 바로 풀어 준다 (호스트의 취소 이벤트가 오면 한 번 더 정리됨)
        UnlockLocalMovement();
        localInteracting = false;
        InteractProgress = 0f;
    }

    private void UnlockLocalMovement()
    {
        var me = PlayerEntity.Local;
        if (me != null && me.Motor != null) me.Motor.SetMovementLock(false);
    }

    private void CancelLocalInteractionUi()
    {
        if (localInteracting) UnlockLocalMovement();
        localInteracting = false;
        InteractProgress = 0f;
        interActive = false;
        if (sites != null) foreach (var s in sites) if (s != null) s.HideProgress();
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 호스트: 요청 판정

    protected override void OnModeRequest(byte kind, NetReader reader, uint sender)
    {
        if (!IsPlaying || !RoundActive || hostClosed) return;

        switch (kind)
        {
            case ReqInteract:
            {
                int site = reader.ReadByte();
                bool isPlant = reader.ReadBool();
                HostStartInteract(sender, site, isPlant);
                break;
            }
            case ReqCancel:
            {
                int site = reader.ReadByte();
                if (hostInterActive && hostInterSession == sender && hostInterSite == site)
                    HostCancelInteract();
                break;
            }
        }
    }

    private void HostStartInteract(uint sender, int siteIndex, bool isPlant)
    {
        if (hostInterActive) return;                                 // 한 번에 하나만 (중복 설치 방지)
        var player = Net.GetPlayer(sender);
        var site = GetSite(siteIndex);
        if (player == null || player.IsForfeited || site == null || !site.IsActive) return;
        if (State != null && State.IsDead(sender)) return;

        if (isPlant)
        {
            if (hostPlanted || player.Team != InstallerTeam) return;
        }
        else
        {
            if (!hostPlanted || siteIndex != PlantedSite || player.Team != DefenderTeam || !site.IsPlantedHere) return;
        }

        if (!IsWithinRange(sender, site)) return;

        hostInterActive = true;
        hostInterSite = siteIndex;
        hostInterIsPlant = isPlant;
        hostInterSession = sender;
        hostInterElapsed = 0f;
        hostInterDuration = Mathf.Max(0.1f, isPlant ? plantSeconds : defuseSeconds);

        float duration = hostInterDuration;
        HostBroadcast(EvInteractStart, w =>
        {
            w.WriteByte((byte)siteIndex);
            w.WriteBool(isPlant);
            w.WriteUInt(sender);
            w.WriteFloat(duration);
        });
    }

    private bool IsWithinRange(uint session, BombSite site)
    {
        var entity = PlayerEntity.Get(session);
        if (entity == null) return false;
        float limit = site.interactRange * Mathf.Max(1f, hostRangeTolerance);
        return Vector3.Distance(entity.transform.position, site.AnchorPosition) <= limit;
    }

    private void HostCancelInteract()
    {
        if (!hostInterActive) return;
        int site = hostInterSite;
        hostInterActive = false;
        HostBroadcast(EvInteractCancel, w => w.WriteByte((byte)site));
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 호스트: 매 프레임 판정

    protected override void OnHostUpdate()
    {
        // (1) 첫 라운드 시작 (아직 아무 라운드도 시작되지 않았다면)
        if (CurrentRound == 0)
        {
            HostSendRoundStart(1);
            return;
        }

        // (2) 라운드 사이: 매치 종료 / 다음 라운드
        if (!RoundActive)
        {
            if (IsMatchOver()) TryEndMatch(WinnerByScore());
            else if (NetClock.Now >= nextRoundAt) HostSendRoundStart(CurrentRound + 1);
            return;
        }

        if (hostClosed) return;

        // (3) 설치/해제 진행
        HostTickInteraction();
        if (hostClosed) return;

        // (4) 시간 만료
        if (NetClock.Now >= roundEndAt)
        {
            if (!hostPlanted) HostCloseRound(DefenderTeam, RoundEndReason.TimeOut);
            else HostCloseRound(InstallerTeam, RoundEndReason.Exploded);
            return;
        }

        // (5) 전멸
        HostCheckElimination();
    }

    private void HostSendRoundStart(int round)
    {
        if (Time.unscaledTime - lastRoundStartSentAt < 1.2f) return;      // 에코 대기 중 재전송 방지
        lastRoundStartSentAt = Time.unscaledTime;

        NetTeam installer = (round % 2 == 1) ? firstInstallerTeam : Opposite(firstInstallerTeam);
        float seconds = roundSeconds;
        HostBroadcast(EvRoundStart, w =>
        {
            w.WriteByte((byte)round);
            w.WriteByte((byte)installer);
            w.WriteFloat(seconds);
        });
    }

    private void HostTickInteraction()
    {
        if (!hostInterActive) return;

        var site = GetSite(hostInterSite);
        var player = Net.GetPlayer(hostInterSession);
        var entity = PlayerEntity.Get(hostInterSession);

        bool invalid =
            site == null || !site.IsActive ||
            player == null || player.IsForfeited ||
            entity == null || !entity.IsPresent ||
            (State != null && State.IsDead(hostInterSession)) ||
            (hostInterIsPlant && hostPlanted) ||
            (!hostInterIsPlant && !hostPlanted) ||
            !IsWithinRange(hostInterSession, site);

        if (invalid) { HostCancelInteract(); return; }

        hostInterElapsed += Time.deltaTime;
        if (hostInterElapsed < hostInterDuration) return;

        // 완료
        bool wasPlant = hostInterIsPlant;
        int siteIndex = hostInterSite;
        hostInterActive = false;

        if (wasPlant)
        {
            hostPlanted = true;
            float bombSeconds = bombCountdownSeconds;
            HostBroadcast(EvPlanted, w => { w.WriteByte((byte)siteIndex); w.WriteFloat(bombSeconds); });
        }
        else
        {
            HostCloseRound(DefenderTeam, RoundEndReason.Defused);
        }
    }

    private bool TeamHasPlayers(NetTeam team)
    {
        foreach (var p in Net.PlayersOf(team))
            if (!p.IsForfeited) return true;
        return false;
    }

    private void HostCheckElimination()
    {
        if (State == null) return;
        NetTeam atk = InstallerTeam, def = DefenderTeam;

        if (skipEliminationForEmptyTeam && (!TeamHasPlayers(atk) || !TeamHasPlayers(def))) return;

        int atkAlive = State.AliveCount(atk);
        int defAlive = State.AliveCount(def);

        if (!hostPlanted)
        {
            if (atkAlive == 0) HostCloseRound(def, RoundEndReason.Eliminated);       // 기존과 같은 우선순위
            else if (defAlive == 0) HostCloseRound(atk, RoundEndReason.Eliminated);
        }
        else
        {
            // 설치 후: 해제팀 전멸 → 설치팀 승. (설치팀만 전멸한 것은 종료 사유가 아님: 해제 가능)
            if (defAlive == 0) HostCloseRound(atk, RoundEndReason.Eliminated);
        }
    }

    private void HostCloseRound(NetTeam winner, RoundEndReason reason)
    {
        if (hostClosed) return;
        hostClosed = true;
        hostInterActive = false;

        int red = RedWins + (winner == NetTeam.Red ? 1 : 0);
        int blue = BlueWins + (winner == NetTeam.Blue ? 1 : 0);

        HostBroadcast(EvRoundEnd, w =>
        {
            w.WriteByte((byte)winner);
            w.WriteByte((byte)reason);
            w.WriteByte((byte)Mathf.Clamp(red, 0, 255));
            w.WriteByte((byte)Mathf.Clamp(blue, 0, 255));
        });
    }

    private bool IsMatchOver()
    {
        return RedWins >= WinsNeeded || BlueWins >= WinsNeeded || CurrentRound >= totalRounds;
    }

    protected override void OnHostChangedHook(bool isMe)
    {
        if (!isMe) return;
        // 이전 호스트가 하던 설치/해제 진행은 이어받을 수 없다 → 화면에 남은 진행을 취소시킨다.
        hostInterActive = false;
        if (interActive) HostBroadcast(EvInteractCancel, w => w.WriteByte((byte)interSite));
        hostClosed = !RoundActive && CurrentRound > 0;
        hostPlanted = BombPlanted;
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 모든 기기: 이벤트 반영

    protected override void OnModeEvent(byte kind, NetReader r, uint sender)
    {
        switch (kind)
        {
            case EvRoundStart:
            {
                int round = r.ReadByte();
                var installer = (NetTeam)r.ReadByte();
                float seconds = r.ReadFloat();
                ApplyRoundStart(round, installer, seconds);
                break;
            }
            case EvInteractStart:
            {
                int site = r.ReadByte();
                bool isPlant = r.ReadBool();
                uint session = r.ReadUInt();
                float duration = r.ReadFloat();
                ApplyInteractStart(site, isPlant, session, duration);
                break;
            }
            case EvInteractCancel:
            {
                int site = r.ReadByte();
                ApplyInteractCancel(site);
                break;
            }
            case EvPlanted:
            {
                int site = r.ReadByte();
                float seconds = r.ReadFloat();
                ApplyPlanted(site, seconds);
                break;
            }
            case EvRoundEnd:
            {
                var winner = (NetTeam)r.ReadByte();
                var reason = (RoundEndReason)r.ReadByte();
                int red = r.ReadByte();
                int blue = r.ReadByte();
                ApplyRoundEnd(winner, reason, red, blue);
                break;
            }
        }
    }

    private void ApplyRoundStart(int round, NetTeam installer, float seconds)
    {
        if (round <= CurrentRound) return;                       // 이미 처리한 번호 (중복/역순)

        CurrentRound = round;
        InstallerTeam = installer;
        RoundActive = true;
        BombPlanted = false;
        PlantedSite = -1;
        roundEndAt = NetClock.Now + seconds;

        hostClosed = false;
        hostPlanted = false;
        hostInterActive = false;
        interActive = false;
        CancelLocalInteractionUi();

        if (State != null) State.ResetAlive();

        if (sites != null)
            foreach (var s in sites) if (s != null) s.ResetForRound();

        // 라운드 시작: 내 캐릭터를 시작 위치에서 되살린다 (1라운드는 이미 시작 위치)
        if (respawnAtRoundStart && round > 1 && Spawner != null)
        {
            var me = PlayerEntity.Local;
            if (me != null && me.Info != null && !me.Info.IsForfeited) Spawner.RespawnLocal(me, true);
        }

        RaiseModeChanged();
        if (OnRoundStarted != null) OnRoundStarted(round);
    }

    private void ApplyInteractStart(int siteIndex, bool isPlant, uint session, float duration)
    {
        if (!RoundActive) return;

        interActive = true;
        interSite = siteIndex;
        interIsPlant = isPlant;
        interSession = session;
        interStartAt = NetClock.Now;
        interDuration = duration;

        var site = GetSite(siteIndex);
        if (site != null) site.ShowProgress(isPlant, 0f);

        var net = Net;
        if (net != null && session == net.LocalSessionId)
        {
            localInteracting = true;
            var me = PlayerEntity.Local;
            if (me != null && me.Motor != null) me.Motor.SetMovementLock(true);
        }
    }

    private void ApplyInteractCancel(int siteIndex)
    {
        if (!interActive || interSite != siteIndex) return;

        var site = GetSite(siteIndex);
        if (site != null) site.HideProgress();

        if (localInteracting && interSession == Net.LocalSessionId)
        {
            UnlockLocalMovement();
            localInteracting = false;
            InteractProgress = 0f;
        }
        interActive = false;
    }

    private void ApplyPlanted(int siteIndex, float seconds)
    {
        if (!RoundActive || BombPlanted) return;

        BombPlanted = true;
        PlantedSite = siteIndex;
        hostPlanted = true;
        roundEndAt = NetClock.Now + seconds;

        // 진행 표시 정리
        if (localInteracting) { UnlockLocalMovement(); localInteracting = false; InteractProgress = 0f; }
        interActive = false;

        var site = GetSite(siteIndex);
        if (site != null)
        {
            site.SetPlanted();
            // 해제팀에게만 방향 안내
            var local = Net.LocalPlayer;
            if (local != null && local.Team == DefenderTeam && OnBombAlert != null && !string.IsNullOrEmpty(msgBombPlanted))
                OnBombAlert(site.AnchorTransform, msgBombPlanted);
        }
        RaiseModeChanged();
    }

    private void ApplyRoundEnd(NetTeam winner, RoundEndReason reason, int red, int blue)
    {
        if (!RoundActive) return;                                // 중복 방지

        RoundActive = false;
        hostClosed = true;
        LastRoundWinner = winner;
        LastEndReason = reason;
        RedWins = red;
        BlueWins = blue;
        nextRoundAt = NetClock.Now + betweenRoundsSeconds;
        InteractAvailable = false;

        CancelLocalInteractionUi();
        if (sites != null) foreach (var s in sites) if (s != null) { s.HideProgress(); s.SetLocalPreview(false); s.Deactivate(); }

        var local = Net.LocalPlayer;
        if (local != null)
        {
            string msg = local.Team == winner ? msgRoundWin : msgRoundLose;
            RaiseAlert(msg);
        }

        RaiseModeChanged();
        if (OnRoundEnded != null) OnRoundEnded(winner, reason);
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region 재접속 복구

    // v1: [라운드][설치팀][진행중][설치됨][설치 지점+1][남은 초][레드 승][블루 승][다음 라운드까지 초]
    protected override void WriteModeState(NetWriter writer)
    {
        writer.WriteByte((byte)CurrentRound);
        writer.WriteByte((byte)InstallerTeam);
        writer.WriteBool(RoundActive);
        writer.WriteBool(BombPlanted);
        writer.WriteByte((byte)(PlantedSite + 1));
        writer.WriteFloat(Mathf.Max(0f, (float)(roundEndAt - NetClock.Now)));
        writer.WriteByte((byte)RedWins);
        writer.WriteByte((byte)BlueWins);
        writer.WriteFloat(Mathf.Max(0f, (float)(nextRoundAt - NetClock.Now)));
    }

    protected override void ReadModeState(NetReader reader)
    {
        CurrentRound = reader.ReadByte();
        InstallerTeam = (NetTeam)reader.ReadByte();
        RoundActive = reader.ReadBool();
        BombPlanted = reader.ReadBool();
        PlantedSite = reader.ReadByte() - 1;
        roundEndAt = NetClock.Now + reader.ReadFloat();
        RedWins = reader.ReadByte();
        BlueWins = reader.ReadByte();
        nextRoundAt = NetClock.Now + reader.ReadFloat();

        hostClosed = !RoundActive && CurrentRound > 0;
        hostPlanted = BombPlanted;
        hostInterActive = false;
        interActive = false;
        localInteracting = false;

        if (sites != null)
        {
            for (int i = 0; i < sites.Length; i++)
            {
                if (sites[i] == null) continue;
                if (RoundActive) sites[i].ResetForRound(); else sites[i].Deactivate();
                if (BombPlanted && i == PlantedSite) sites[i].SetPlanted();
            }
        }
    }

    #endregion
}
