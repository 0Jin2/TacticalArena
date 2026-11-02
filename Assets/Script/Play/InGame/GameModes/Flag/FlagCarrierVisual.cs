using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// FlagCarrierVisual : 캐릭터 프리팹에 붙는 "깃발 들고 있는 모습" 표시  (기존 FlagCarrier 대체)
//
//  - 포톤의 NetworkBehaviour/RPC 를 없애고, FlagMode 가 모든 기기에서 SetCarry() 를 직접 호출한다.
//  - 필드 이름은 기존 FlagCarrier 와 같게 두었습니다. (프리팹에서 스크립트만 바꾸고 연결을 다시 해 주세요)
//
//   redFlagVisual  : "블루 깃발을 들고 있는 레드팀" 이 켜는 오브젝트  (기존과 동일한 규칙)
//   blueFlagVisual : "레드 깃발을 들고 있는 블루팀" 이 켜는 오브젝트
// ─────────────────────────────────────────────────────────────────────────────

public class FlagCarrierVisual : MonoBehaviour
{
    [Header("캐리 비주얼(깃발) 오브젝트")]
    [Tooltip("블루 깃발을 들었을 때(= 이 캐릭터가 레드팀) 켜지는 오브젝트")]
    public GameObject redFlagVisual;
    [Tooltip("레드 깃발을 들었을 때(= 이 캐릭터가 블루팀) 켜지는 오브젝트")]
    public GameObject blueFlagVisual;

    [Header("좌우 반전")]
    [Tooltip("캐릭터가 왼쪽을 볼 때 깃발이 뒤집혀 보이지 않도록 스케일을 보정합니다. (기존 동작 유지)")]
    public bool keepFlagUpright = true;

    private Vector3 redInitScale = Vector3.one, blueInitScale = Vector3.one;
    private bool initialized;

    private void Awake()
    {
        Init();
        Clear();
    }

    private void Init()
    {
        if (initialized) return;
        initialized = true;
        if (redFlagVisual != null) redInitScale = redFlagVisual.transform.localScale;
        if (blueFlagVisual != null) blueInitScale = blueFlagVisual.transform.localScale;
    }

    private void LateUpdate()
    {
        if (!keepFlagUpright) return;
        float parentSign = transform.lossyScale.x < 0f ? -1f : 1f;

        if (redFlagVisual != null)
        {
            var s = redInitScale;
            s.x = Mathf.Abs(s.x) * parentSign;
            redFlagVisual.transform.localScale = s;
        }
        if (blueFlagVisual != null)
        {
            var s = blueInitScale;
            s.x = Mathf.Abs(s.x) * parentSign;
            blueFlagVisual.transform.localScale = s;
        }
    }

    /// <summary>flagOwnerTeam 의 깃발을 들고 있다/내려놓았다. (None 이면 모두 끔)</summary>
    public void SetCarry(NetTeam flagOwnerTeam, bool active)
    {
        Init();
        if (flagOwnerTeam == NetTeam.Red)
        {
            // 레드 깃발을 들고 있는 것은 블루팀
            if (blueFlagVisual != null) blueFlagVisual.SetActive(active);
            if (redFlagVisual != null) redFlagVisual.SetActive(false);
        }
        else if (flagOwnerTeam == NetTeam.Blue)
        {
            if (redFlagVisual != null) redFlagVisual.SetActive(active);
            if (blueFlagVisual != null) blueFlagVisual.SetActive(false);
        }
        else Clear();
    }

    public void Clear()
    {
        if (redFlagVisual != null) redFlagVisual.SetActive(false);
        if (blueFlagVisual != null) blueFlagVisual.SetActive(false);
    }
}
