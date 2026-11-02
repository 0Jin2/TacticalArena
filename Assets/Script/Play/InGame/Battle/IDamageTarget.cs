using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// IDamageTarget : "맞을 수 있는 것"의 공통 규격 (캐릭터, 방벽 ...)
//
//  맞았는지 판정하는 사람  = 총알을 쏜 사람 (사격자 기기)
//  체력을 깎는 사람        = 맞은 대상의 주인 (대상의 주인 기기)  ← 체력은 주인이 책임진다
//
//  흐름:  사격자가 DamageRegistry.Send() → 모든 기기가 Damage 패킷 수신
//         → 대상의 ReceiveDamage() 호출 → 주인 기기는 실제로 체력을 깎고, 나머지 기기는 피격 연출만 재생
//
//  대상 식별: (주인 세션, 슬롯)   슬롯 0 = 캐릭터 본체, 1~ = 방벽 같은 설치물
// ─────────────────────────────────────────────────────────────────────────────

public enum DamageTargetKind : byte { Player = 0, Deployable = 1 }

[Flags]
public enum DamageFlags : byte
{
    None = 0,
    Heal = 1,       // 피해가 아니라 회복
    True = 2,       // 방어력/무적/보호막 무시 (낙사 지역 등)
    Silent = 4,     // 피격 연출(소리/깜빡임) 없음
}

public struct DamageInfo
{
    public uint Attacker;           // 가해자 세션 (0 = 없음)
    public int Amount;
    public DamageFlags Flags;

    public bool IsHeal { get { return (Flags & DamageFlags.Heal) != 0; } }
    public bool IsTrue { get { return (Flags & DamageFlags.True) != 0; } }
    public bool IsSilent { get { return (Flags & DamageFlags.Silent) != 0; } }
}

public interface IDamageTarget
{
    uint OwnerSession { get; }
    byte Slot { get; }
    NetTeam Team { get; }
    DamageTargetKind Kind { get; }
    bool IsAlive { get; }
    bool IsOwnerLocal { get; }      // 이 대상의 주인이 "이 기기"인가
    Vector3 Position { get; }

    /// <summary>모든 기기에서 호출된다. 주인 기기만 체력을 실제로 바꾸고, 나머지는 연출만 한다.</summary>
    void ReceiveDamage(DamageInfo info);
}

/// <summary>(주인, 슬롯) → 대상 찾기 표. Damage 패킷이 어떤 대상인지 알아내는 데 쓴다.</summary>
public static class DamageRegistry
{
    private static readonly Dictionary<ulong, IDamageTarget> map = new Dictionary<ulong, IDamageTarget>();

    private static ulong Key(uint owner, byte slot) { return ((ulong)owner << 8) | slot; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() { map.Clear(); }

    public static void Register(IDamageTarget target)
    {
        map[Key(target.OwnerSession, target.Slot)] = target;
    }

    public static void Unregister(IDamageTarget target)
    {
        ulong key = Key(target.OwnerSession, target.Slot);
        IDamageTarget current;
        if (map.TryGetValue(key, out current) && ReferenceEquals(current, target)) map.Remove(key);
    }

    public static IDamageTarget Find(uint owner, byte slot)
    {
        IDamageTarget target;
        return map.TryGetValue(Key(owner, slot), out target) ? target : null;
    }

    /// <summary>방 전체에 "이 대상에게 피해/회복" 패킷을 보낸다. 유실되면 안 되므로 critical.</summary>
    public static bool Send(IDamageTarget target, uint attacker, int amount, DamageFlags flags = DamageFlags.None)
    {
        var net = InGameNet.Instance;
        if (net == null || target == null) return false;

        uint owner = target.OwnerSession;
        byte slot = target.Slot;
        return net.Send(NetPacketType.Damage, w =>
        {
            w.WriteUInt(attacker);
            w.WriteUInt(owner);
            w.WriteByte(slot);
            w.WriteInt(amount);
            w.WriteByte((byte)flags);
        }, true);
    }
}
