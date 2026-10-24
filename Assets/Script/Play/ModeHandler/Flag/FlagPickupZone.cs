using UnityEngine;
using Fusion;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D))]
public class FlagPickupZone : MonoBehaviour
{
    public TeamAssigner.Team ownerTeam = TeamAssigner.Team.Red; // 이 구역의 깃발 소유
                                                                // 팀
    [Tooltip("이 구역에 들어왔을 때 최소 체류 시간(초). 너무 짧은 스쳐지나감 방지용")]
    public float minStaySeconds = 0.05f;
    private float stayTimer = 0f;

    private readonly Dictionary<EntityId, float> _stayTimers = new Dictionary<EntityId, float>();

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
        gameObject.layer = LayerMask.NameToLayer("Default");
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        var id = other.GetEntityId();
        if (_stayTimers.TryGetValue(id, out float t))
            _stayTimers[id] = t + Time.deltaTime;
        else
            _stayTimers[id] = Time.deltaTime;

        if (_stayTimers[id] < minStaySeconds) return;

        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj == null || netObj.Runner == null) return;

        var ctrl = other.GetComponentInParent<PlayerNetworkController>();
        if (ctrl == null || ctrl.Object == null) return;

        var handler = FindObjectOfType<FlagModeHandler>();
        var player = ctrl.Object.InputAuthority;

        if (handler != null && player.IsValid)
        {
            // 서버로 “픽업 요청” 전송 (이제 RpcSources.All이라 정상 수신)
            handler.RPC_RequestPickup(ownerTeam, player);

            // 한 번 요청한 뒤에는 중복 전송 방지용으로 타이머 리셋(선택)
            _stayTimers[id] = 0f;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var id = other.GetEntityId();
        if (_stayTimers.ContainsKey(id))
            _stayTimers.Remove(id);
    }
}
