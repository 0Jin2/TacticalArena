using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// ShieldSummonSkill : 미레이 - 방벽 소환
//   spawnPoint 위치에 방벽(BarrierActor)을 세운다. 방벽은 적 총알을 막고, barrierDuration 초 뒤 사라진다.
//
//   모든 기기가 같은 방벽을 각자 만든다. (이벤트로 위치/방향/슬롯 번호를 전달)
//   방벽 체력은 "방벽 주인의 기기"가 계산하고, 부서지면 모든 기기에 알린다.
// ─────────────────────────────────────────────────────────────────────────────

public class ShieldSummonSkill : SkillBase
{
    [Header("방벽")]
    public BarrierActor barrierPrefab;
    public Transform spawnPoint;
    public float barrierDuration = 3f;

    private const byte PhaseSpawn = 1;      // [슬롯 byte][위치 vec2][회전Z float][방향 byte][지속 float]
    private const byte PhaseBreak = 2;      // [슬롯 byte]

    private readonly Dictionary<byte, BarrierActor> barriers = new Dictionary<byte, BarrierActor>();
    private byte nextSlot = 1;

    protected override IEnumerator Run()
    {
        if (barrierPrefab == null || spawnPoint == null)
        {
            NetLog.Warn("ShieldSummonSkill: barrierPrefab 또는 spawnPoint 가 비어 있습니다.");
            yield break;
        }

        byte slot = AllocateSlot();
        Vector2 pos = spawnPoint.position;
        float rotZ = spawnPoint.eulerAngles.z;
        byte facing = Entity.Motor.Facing < 0f ? (byte)0 : (byte)1;
        float duration = barrierDuration;

        Broadcast(PhaseSpawn, w =>
        {
            w.WriteByte(slot);
            w.WriteVector2(pos);
            w.WriteFloat(rotZ);
            w.WriteByte(facing);
            w.WriteFloat(duration);
        });
        yield return null;
    }

    /// <summary>슬롯 1~200 을 돌려 쓴다. 아직 살아 있는 방벽의 슬롯은 건너뛴다.</summary>
    private byte AllocateSlot()
    {
        for (int i = 0; i < 200; i++)
        {
            byte candidate = nextSlot;
            nextSlot = (byte)(nextSlot >= 200 ? 1 : nextSlot + 1);
            if (!barriers.ContainsKey(candidate)) return candidate;
        }
        return nextSlot;
    }

    protected override void OnNetEvent(byte phase, NetReader r)
    {
        switch (phase)
        {
            case PhaseSpawn:
            {
                byte slot = r.ReadByte();
                Vector2 pos = r.ReadVector2();
                float rotZ = r.ReadFloat();
                float facing = r.ReadByte() == 0 ? -1f : 1f;
                float duration = r.ReadFloat();
                CreateBarrier(slot, pos, rotZ, facing, duration);
                break;
            }
            case PhaseBreak:
            {
                byte slot = r.ReadByte();
                BarrierActor barrier;
                if (barriers.TryGetValue(slot, out barrier) && barrier != null) barrier.Break();
                break;
            }
        }
    }

    private void CreateBarrier(byte slot, Vector2 pos, float rotZ, float facing, float duration)
    {
        if (barrierPrefab == null) return;

        BarrierActor old;
        if (barriers.TryGetValue(slot, out old) && old != null) old.Break();

        var barrier = Instantiate(barrierPrefab, pos, Quaternion.Euler(0f, 0f, rotZ));
        barrier.Initialize(this, Entity.SessionId, slot, duration, facing);
        barriers[slot] = barrier;
    }

    /// <summary>[BarrierActor 가 부르는 함수] 방벽 체력이 0 → 모든 기기에 알린다. (방벽 주인 기기에서만)</summary>
    public void RequestBreak(byte slot)
    {
        if (Entity == null || !Entity.IsMine) return;
        Broadcast(PhaseBreak, w => w.WriteByte(slot));
    }

    public void ForgetBarrier(byte slot)
    {
        barriers.Remove(slot);
    }

    // 방벽은 주인이 죽어도 남는다. (기존 동작: 방벽은 독립된 오브젝트)
}
