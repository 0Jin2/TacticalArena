using System;
using System.IO;
using System.Text;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// NetPacket.cs : 인게임 네트워크 "공용 규약" 파일
//   - 패킷 종류(NetPacketType), 직렬화(NetWriter/NetReader), 수신 패킷(NetPacket)
//   - 재접속 시 상태 복구 규약(INetStateProvider, NetStateIds)
// 새 패킷/새 복구 대상이 생기면 이 파일에 "한 줄"만 추가하면 된다. (로직은 각 스크립트에)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 패킷 종류. 번호 대역을 나눠 쓰면 충돌 없이 확장할 수 있다.
///   0x01~0x0F 네트워크 코어 / 0x10~0x1F 매치 흐름 / 0x20~0x3F 플레이어
///   0x40~0x5F 전투         / 0x60~0x7F 모드 전용
/// </summary>
public enum NetPacketType : byte
{
    None = 0,

    // ── 네트워크 코어 (InGameNet / NetConnectionManager 전용)
    SceneReady = 0x01,            // 씬 로드·초기화 완료 알림
    Ping = 0x02,                  // 연결 확인용. 서버가 나에게도 되돌려주므로 "에코가 오면 연결 정상"
    StateSnapshotRequest = 0x03,  // 재접속한 유저 → 호스트: 현재 상태를 달라
    StateSnapshot = 0x04,         // 호스트 → 재접속한 유저: 현재 상태 묶음

    // ── 매치 흐름 (MatchFlow)   0x10~
    PhaseChange = 0x10,           // 호스트 → 전체: 다음 단계로 이동 (단계, 시간, 승리팀, 캐릭터 확정 목록)
    CharacterSelect = 0x11,       // 각 유저 → 전체: 캐릭터를 골랐다 (바꿀 수 있음)
    CharacterReady = 0x12,        // 각 유저 → 전체: 준비 완료/해제 (bool)
    ScoreAdd = 0x13,              // 호스트 → 전체: 팀 점수 가산 (팀, 점수)
    ZoomReady = 0x14,             // 각 유저 → 전체: 캐릭터 생성 + 내 카메라 줌인이 끝났다 (호스트가 전원 확인 후 Playing 으로)
    // ── 플레이어 (이동 동기화)   0x20~
    PlayerState = 0x20,           // 주인 → 전체: 위치/속도/방향/체력 (주기 전송, 유실 허용)
    PlayerSpawn = 0x21,           // 주인 → 전체: 부활(위치, 방향, 무적시간)
    PlayerDied = 0x22,            // 주인 → 전체: 사망 (피해자, 가해자)
    // ── 전투 (발사/피격/스킬)    0x40~
    Fire = 0x40,                  // 사격자 → 전체: 총알 생성
    Damage = 0x41,                // 가해자 → 전체: 피해/치유 (대상의 주인 기기가 처리)
    SkillEvent = 0x42,            // 스킬 사용자 → 전체: 스킬 연출/상태
    Knockback = 0x43,             // 총알 주인 → 전체: 대상 넉백 (대상의 주인 기기가 적용)
    CombatEvent = 0x44,           // 캐릭터 주인 → 전체: 재장전/점프 효과음 같은 연출 이벤트
    // ── 모드 전용 (폭탄/깃발)    0x60~
    ModeEvent = 0x60,             // 호스트 → 전체: 모드 확정 이벤트 (모드 스크립트가 해석)
    ModeRequest = 0x61,           // 유저 → 전체: 모드 요청 (호스트가 검증 후 ModeEvent로 확정)
}

public static class NetClock
{
    private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// 벽시계 기준 초. 모바일은 백그라운드(OnApplicationPause) 동안 Time.time이 멈추거나 튀므로,
    /// "재접속 제한 시간" 같은 실제 경과 시간은 이 값으로 잰다.
    /// </summary>
    public static double Now => (DateTime.UtcNow - Epoch).TotalSeconds;
}

public static class NetLog
{
    public static bool Verbose;
    public static void Info(string message) { if (Verbose) Debug.Log("[Net] " + message); }
    public static void Warn(string message) { Debug.LogWarning("[Net] " + message); }
    public static void Error(string message) { Debug.LogError("[Net] " + message); }
}

/// <summary>수신한 패킷 1개. 핸들러마다 OpenReader()로 새 리더를 열어 읽는다.</summary>
public sealed class NetPacket
{
    public readonly NetPacketType Type;
    public readonly uint Sender;       // 보낸 유저의 세션 ID
    public readonly bool FromLocal;    // 내가 보낸 패킷이 서버를 거쳐 돌아온 것인가
    private readonly byte[] payload;

    public NetPacket(NetPacketType type, uint sender, bool fromLocal, byte[] payload)
    {
        Type = type;
        Sender = sender;
        FromLocal = fromLocal;
        this.payload = payload ?? Array.Empty<byte>();
    }

    public int PayloadLength => payload.Length;
    public NetReader OpenReader() { return new NetReader(payload); }
}

public sealed class NetWriter
{
    private readonly MemoryStream stream;
    private readonly BinaryWriter writer;

    public NetWriter(int capacity = 64)
    {
        stream = new MemoryStream(capacity);
        writer = new BinaryWriter(stream, new UTF8Encoding(false));
    }

    public int Length { get { return (int)stream.Length; } }

    public NetWriter WriteByte(byte v) { writer.Write(v); return this; }
    public NetWriter WriteBool(bool v) { writer.Write(v); return this; }
    public NetWriter WriteUShort(ushort v) { writer.Write(v); return this; }
    public NetWriter WriteInt(int v) { writer.Write(v); return this; }
    public NetWriter WriteUInt(uint v) { writer.Write(v); return this; }
    public NetWriter WriteFloat(float v) { writer.Write(v); return this; }
    public NetWriter WriteString(string v) { writer.Write(v ?? string.Empty); return this; }
    public NetWriter WriteVector2(Vector2 v) { writer.Write(v.x); writer.Write(v.y); return this; }

    /// <summary>길이(int) + 바이트. 중첩 블록(스냅샷의 Provider별 데이터)에 사용.</summary>
    public NetWriter WriteBlob(byte[] data)
    {
        data = data ?? Array.Empty<byte>();
        writer.Write(data.Length);
        writer.Write(data);
        return this;
    }

    public byte[] ToArray() { writer.Flush(); return stream.ToArray(); }
}

public sealed class NetReader
{
    private readonly MemoryStream stream;
    private readonly BinaryReader reader;

    public NetReader(byte[] data)
    {
        stream = new MemoryStream(data ?? Array.Empty<byte>(), false);
        reader = new BinaryReader(stream, new UTF8Encoding(false));
    }

    public int Remaining { get { return (int)(stream.Length - stream.Position); } }

    public byte ReadByte() { return reader.ReadByte(); }
    public bool ReadBool() { return reader.ReadBoolean(); }
    public ushort ReadUShort() { return reader.ReadUInt16(); }
    public int ReadInt() { return reader.ReadInt32(); }
    public uint ReadUInt() { return reader.ReadUInt32(); }
    public float ReadFloat() { return reader.ReadSingle(); }
    public string ReadString() { return reader.ReadString(); }
    public Vector2 ReadVector2() { float x = reader.ReadSingle(); float y = reader.ReadSingle(); return new Vector2(x, y); }

    public byte[] ReadBlob()
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > Remaining) throw new InvalidDataException("잘못된 블록 길이: " + length);
        return reader.ReadBytes(length);
    }
}

/// <summary>
/// 재접속 시 복구해야 하는 상태를 가진 스크립트가 구현한다. (MatchFlow, MatchState, 플레이어 등)
///
/// 동작:
///  - 재접속한 유저가 호스트에게 스냅샷을 요청하면, 호스트는 등록된 모든 Provider의 WriteState를 모아 보낸다.
///  - 재접속한 유저는 받은 스냅샷을 ReadState로 각 Provider에 나눠 준다.
/// → "복구해야 할 상태"가 늘어나면 해당 스크립트가 이 인터페이스만 구현하고 등록하면 끝. 재접속 코드는 수정하지 않는다.
///
/// 작성 규칙:
///  1) 시각은 "절대 시각"이 아니라 "남은 시간(초)"으로 쓴다. (기기마다 시계가 다르다)
///  2) 블록 맨 앞에 버전 byte를 쓰고, ReadState에서 버전별로 분기한다. (필드 추가 시 구버전 호환)
///  3) 등록은 OnEnable/Start에서 InGameNet.RegisterStateProvider, 해제는 OnDisable/OnDestroy에서.
///  4) 스냅샷 전체 크기는 슈퍼 게이머 전송 한도(30KB/s)보다 충분히 작게 유지한다. (권장 8KB 이하)
/// </summary>
public interface INetStateProvider
{
    ushort StateId { get; }               // NetStateIds의 상수 사용 (중복 금지)
    void WriteState(NetWriter writer);    // 호스트: 현재 상태 기록
    void ReadState(NetReader reader);     // 재접속자: 받은 상태로 복구
}

/// <summary>스냅샷 블록 ID 목록. 새 Provider를 만들면 여기에 번호를 추가한다. (중복 금지)</summary>
public static class NetStateIds
{
    public const ushort Roster = 1;       // 참가자 명단/팀/캐릭터/접속 상태 (InGameNet이 처리)
    public const ushort MatchFlow = 2;    // 현재 페이즈 + 남은 시간
    public const ushort MatchState = 3;   // 팀 점수, 킬/데스
    public const ushort Players = 4;      // 플레이어 위치/HP/무적/스킬 쿨타임
    public const ushort Mode = 5;         // 모드 전용 상태 (폭탄 라운드, 깃발 위치 등)
}
