using UnityEngine;
using Fusion;

public class FlagCarrier : NetworkBehaviour
{
    [Header("캐리 비주얼(깃발) 오브젝트")]
    public GameObject redFlagVisual;   // 레드 깃발 비주얼
    public GameObject blueFlagVisual;  // 블루 깃발 비주얼

    private Vector3 _redInitScale, _blueInitScale;

    public override void Spawned()
    {
        if (redFlagVisual != null) redFlagVisual.SetActive(false);
        if (blueFlagVisual != null) blueFlagVisual.SetActive(false);

        // ✅ [추가] 현재 프리팹의 초기 스케일 저장
        if (redFlagVisual != null) _redInitScale = redFlagVisual.transform.localScale;
        if (blueFlagVisual != null) _blueInitScale = blueFlagVisual.transform.localScale;
    }

    private void LateUpdate()
    {
        float parentSign = Mathf.Sign(transform.lossyScale.x == 0f ? 1f : transform.lossyScale.x);

        if (redFlagVisual != null)
        {
            var s = _redInitScale;
            s.x = Mathf.Abs(s.x) * (parentSign < 0f ? -1f : 1f);
            redFlagVisual.transform.localScale = s;
        }

        if (blueFlagVisual != null)
        {
            var s = _blueInitScale;
            s.x = Mathf.Abs(s.x) * (parentSign < 0f ? -1f : 1f);
            blueFlagVisual.transform.localScale = s;
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SetCarry(TeamAssigner.Team flagOwnerTeam, bool active)
    {
        // 깃발 소유팀 기준으로 비주얼 켜기
        if (flagOwnerTeam == TeamAssigner.Team.Red)
        {
            // 레드 깃발을 뺏겼다는 뜻 → 블루팀이 들고 있어야 함
            if (blueFlagVisual != null) blueFlagVisual.SetActive(active);
            if (redFlagVisual != null) redFlagVisual.SetActive(false);
        }
        else if (flagOwnerTeam == TeamAssigner.Team.Blue)
        {
            // 블루 깃발을 뺏겼다는 뜻 → 레드팀이 들고 있어야 함
            if (redFlagVisual != null) redFlagVisual.SetActive(active);
            if (blueFlagVisual != null) blueFlagVisual.SetActive(false);
        }
        else
        {
            // None이면 전부 끄기
            if (redFlagVisual != null) redFlagVisual.SetActive(false);
            if (blueFlagVisual != null) blueFlagVisual.SetActive(false);
        }
    }
}
