using UnityEngine;
using Fusion;

[RequireComponent(typeof(Collider2D))]
public class FlagGoalZone : MonoBehaviour
{
    public TeamAssigner.Team goalTeam = TeamAssigner.Team.Red; // 이 구역에서 득점할 팀

    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
        gameObject.layer = LayerMask.NameToLayer("Default");
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var ctrl = other.GetComponentInParent<PlayerNetworkController>();
        if (ctrl == null || ctrl.Object == null) return;

        var handler = FindObjectOfType<FlagModeHandler>();
        if (handler == null) return;

        var player = ctrl.Object.InputAuthority;
        if (!player.IsValid) return;

        // 서버에 "골인 요청" RPC
        handler.RPC_RequestScore(goalTeam, player);
    }
}
