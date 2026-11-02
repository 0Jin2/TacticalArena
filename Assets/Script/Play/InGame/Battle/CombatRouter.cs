using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// CombatRouter : 전투 관련 패킷을 "해당 캐릭터"에게 나눠 주는 우체부
//
//  패킷 → 받는 곳
//   PlayerState  → 보낸 사람의 캐릭터 PlayerNetSync.ApplyState
//   PlayerSpawn  → 보낸 사람의 캐릭터 ApplyRemoteSpawn
//   PlayerDied   → 죽은 캐릭터 PlayerHealth.RemoteDie        (킬/데스 집계는 MatchState 가 따로 처리)
//   Fire         → 보낸 사람의 캐릭터 PlayerCombat.OnRemoteFire
//   Damage       → DamageRegistry 에서 찾은 대상.ReceiveDamage
//   Knockback    → 대상 캐릭터(내 것일 때만) PlayerMotor.ApplyKnockback
//   CombatEvent  → 보낸 사람의 캐릭터 (재장전/점프 효과음)
//   SkillEvent   → 보낸 사람의 캐릭터 SkillBase.HandleNetEvent
//
//  "내가 보낸 패킷이 되돌아온 것(FromLocal)"은 이미 내 기기에서 처리했으므로 무시한다.
//  (단, Damage / Knockback / PlayerDied 는 되돌아온 것도 처리한다)
//  PlayerSpawner 가 자동으로 붙여 주므로 씬에 직접 배치하지 않아도 된다.
// ─────────────────────────────────────────────────────────────────────────────

public class CombatRouter : MonoBehaviour
{
    private InGameNet net;

    private void OnEnable()
    {
        net = InGameNet.Ensure();
        net.Subscribe(NetPacketType.PlayerState, OnPlayerState);
        net.Subscribe(NetPacketType.PlayerSpawn, OnPlayerSpawn);
        net.Subscribe(NetPacketType.PlayerDied, OnPlayerDied);
        net.Subscribe(NetPacketType.Fire, OnFire);
        net.Subscribe(NetPacketType.Damage, OnDamage);
        net.Subscribe(NetPacketType.Knockback, OnKnockback);
        net.Subscribe(NetPacketType.CombatEvent, OnCombatEvent);
        net.Subscribe(NetPacketType.SkillEvent, OnSkillEvent);
    }

    private void OnDisable()
    {
        if (net == null) return;
        net.Unsubscribe(NetPacketType.PlayerState, OnPlayerState);
        net.Unsubscribe(NetPacketType.PlayerSpawn, OnPlayerSpawn);
        net.Unsubscribe(NetPacketType.PlayerDied, OnPlayerDied);
        net.Unsubscribe(NetPacketType.Fire, OnFire);
        net.Unsubscribe(NetPacketType.Damage, OnDamage);
        net.Unsubscribe(NetPacketType.Knockback, OnKnockback);
        net.Unsubscribe(NetPacketType.CombatEvent, OnCombatEvent);
        net.Unsubscribe(NetPacketType.SkillEvent, OnSkillEvent);
    }

    private void OnPlayerState(NetPacket p)
    {
        if (p.FromLocal) return;
        var e = PlayerEntity.Get(p.Sender);
        if (e == null || e.Sync == null) return;
        e.Sync.ApplyState(p.OpenReader());
    }

    private void OnPlayerSpawn(NetPacket p)
    {
        if (p.FromLocal) return;
        var e = PlayerEntity.Get(p.Sender);
        if (e == null) return;
        e.ApplyRemoteSpawn(p.OpenReader());
    }

    // [피해자 uint][가해자 uint]
    private void OnPlayerDied(NetPacket p)
    {
        var r = p.OpenReader();
        uint victim = r.ReadUInt();
        uint killer = r.ReadUInt();

        var e = PlayerEntity.Get(victim);
        if (e == null || e.IsMine || e.Health == null) return;   // 내 캐릭터는 이미 처리함
        e.Health.RemoteDie(killer);
    }

    private void OnFire(NetPacket p)
    {
        if (p.FromLocal) return;
        var e = PlayerEntity.Get(p.Sender);
        if (e == null || e.Combat == null || (e.Health != null && e.Health.IsDead)) return;
        e.Combat.OnRemoteFire(p.OpenReader());
    }

    // [가해자 uint][대상 주인 uint][슬롯 byte][양 int][플래그 byte]
    private void OnDamage(NetPacket p)
    {
        var r = p.OpenReader();
        var info = new DamageInfo();
        info.Attacker = r.ReadUInt();
        uint owner = r.ReadUInt();
        byte slot = r.ReadByte();
        info.Amount = r.ReadInt();
        info.Flags = (DamageFlags)r.ReadByte();

        var target = DamageRegistry.Find(owner, slot);
        if (target == null) return;
        target.ReceiveDamage(info);
    }

    // [대상 주인 uint][방향 vec2][힘 float][위 보정 float][잠금 float][반대방향 bool]
    private void OnKnockback(NetPacket p)
    {
        var r = p.OpenReader();
        uint victim = r.ReadUInt();
        Vector2 dir = r.ReadVector2();
        float hopX = r.ReadFloat();
        float hopY = r.ReadFloat();
        float lockSeconds = r.ReadFloat();
        bool opposite = r.ReadBool();

        var e = PlayerEntity.Get(victim);
        if (e == null || !e.IsMine || e.Motor == null) return;
        if (e.Health != null && (e.Health.IsDead || e.Health.IsInvulnerable)) return;
        e.Motor.ApplyKnockback(dir, hopX, hopY, lockSeconds, opposite);
    }

    // [종류 byte] ...
    private void OnCombatEvent(NetPacket p)
    {
        if (p.FromLocal) return;
        var e = PlayerEntity.Get(p.Sender);
        if (e == null) return;

        var r = p.OpenReader();
        byte kind = r.ReadByte();
        switch (kind)
        {
            case CombatEventKind.Reload:
                if (e.Combat != null) e.Combat.OnRemoteReload(r.ReadByte(), r.ReadFloat());
                break;
            case CombatEventKind.Jump:
                if (e.Motor != null) e.Motor.PlayRemoteJump();
                break;
        }
    }

    private void OnSkillEvent(NetPacket p)
    {
        if (p.FromLocal) return;
        var e = PlayerEntity.Get(p.Sender);
        if (e == null || e.Skill == null) return;
        e.Skill.HandleNetEvent(p.OpenReader());
    }
}
