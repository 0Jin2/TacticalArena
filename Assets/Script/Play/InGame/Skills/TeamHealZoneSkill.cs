using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// TeamHealZoneSkill : 윤서희 - 팀 회복 구역
//   spawnPoint 위치에 회복 구역(HealZoneActor)을 만든다.
//   구역 안의 "같은 팀" 캐릭터를 healInterval 초마다 healAmount 만큼 회복시킨다. (zoneDuration 초 동안)
// ─────────────────────────────────────────────────────────────────────────────

public class TeamHealZoneSkill : SkillBase
{
    [Header("회복 구역")]
    public HealZoneActor healZonePrefab;
    public Transform spawnPoint;
    public float zoneDuration = 5f;
    public float healInterval = 1f;
    public int healAmount = 10;

    private const byte PhaseSpawn = 1;      // [위치 vec2][크기 vec2][지속 float][간격 float][회복량 int]

    protected override IEnumerator Run()
    {
        if (healZonePrefab == null || spawnPoint == null)
        {
            NetLog.Warn("TeamHealZoneSkill: healZonePrefab 또는 spawnPoint 가 비어 있습니다.");
            yield break;
        }

        Vector2 pos = spawnPoint.position;
        Vector2 scale = spawnPoint.localScale;
        float duration = zoneDuration, interval = healInterval;
        int amount = healAmount;

        Broadcast(PhaseSpawn, w =>
        {
            w.WriteVector2(pos);
            w.WriteVector2(scale);
            w.WriteFloat(duration);
            w.WriteFloat(interval);
            w.WriteInt(amount);
        });
        yield return null;
    }

    protected override void OnNetEvent(byte phase, NetReader r)
    {
        if (phase != PhaseSpawn || healZonePrefab == null) return;

        Vector2 pos = r.ReadVector2();
        Vector2 scale = r.ReadVector2();
        float duration = r.ReadFloat();
        float interval = r.ReadFloat();
        int amount = r.ReadInt();

        var zone = Instantiate(healZonePrefab, pos, Quaternion.identity);
        zone.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        zone.Initialize(Entity.SessionId, duration, interval, amount);
    }
}
