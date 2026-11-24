using UnityEngine;
using UnityEngine.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// FlagCarrierVisual : 캐릭터 프리팹에 붙는 "깃발 들고 있는 모습" 표시  (기존 FlagCarrier 대체)
//
//  - FlagMode 가 모든 기기에서 SetCarry() 를 직접 호출한다.
//  - 깃발 색은 "보는 사람 기준"이다.  우리 팀 깃발 = 파랑 / 상대 팀 깃발 = 빨강
//      예) 내 팀원이 상대 깃발을 들고 오면 → 빨간 깃발
//          상대가 우리 깃발을 들고 가면   → 파란 깃발
//
//  ※ 예전 칸 이름 redFlagVisual(블루 깃발 그림) → blueFlagLook, blueFlagVisual(레드 깃발 그림) → redFlagLook 으로
//     이름만 바뀌었고 프리팹 연결은 그대로 유지됩니다. 파란 칸에 파란 깃발 그림이 들어 있는지 한 번 확인하세요.
// ─────────────────────────────────────────────────────────────────────────────

public class FlagCarrierVisual : MonoBehaviour
{
    [Header("캐리 비주얼(깃발) 오브젝트 (보는 사람 기준 색)")]
    [Tooltip("파란 깃발 그림. '우리 팀 깃발'을 누군가 들고 있을 때 켜집니다.")]
    [FormerlySerializedAs("redFlagVisual")]
    public GameObject blueFlagLook;  // 파란 깃발 그림 (우리 팀 깃발을 들고 있을 때)
    [Tooltip("빨간 깃발 그림. '상대 팀 깃발'을 누군가 들고 있을 때 켜집니다.")]
    [FormerlySerializedAs("blueFlagVisual")]
    public GameObject redFlagLook;   // 빨간 깃발 그림 (상대 팀 깃발을 들고 있을 때)

    [Header("좌우 반전")]
    [Tooltip("캐릭터가 왼쪽을 볼 때 깃발이 뒤집혀 보이지 않도록 스케일을 보정합니다. (기존 동작 유지)")]
    public bool keepFlagUpright = true;  // 켜면 왼쪽을 볼 때 깃발이 뒤집혀 보이지 않게 보정

    private Vector3 blueInitScale = Vector3.one, redInitScale = Vector3.one;
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
        if (blueFlagLook != null) blueInitScale = blueFlagLook.transform.localScale;
        if (redFlagLook != null) redInitScale = redFlagLook.transform.localScale;
    }

    private void LateUpdate()
    {
        if (!keepFlagUpright) return;
        float parentSign = transform.lossyScale.x < 0f ? -1f : 1f;

        if (blueFlagLook != null)
        {
            var s = blueInitScale;
            s.x = Mathf.Abs(s.x) * parentSign;
            blueFlagLook.transform.localScale = s;
        }
        if (redFlagLook != null)
        {
            var s = redInitScale;
            s.x = Mathf.Abs(s.x) * parentSign;
            redFlagLook.transform.localScale = s;
        }
    }

    /// <summary>flagOwnerTeam 의 깃발을 들고 있다/내려놓았다. (None 이면 모두 끔)</summary>
    public void SetCarry(NetTeam flagOwnerTeam, bool active)
    {
        Init();
        if (flagOwnerTeam != NetTeam.Red && flagOwnerTeam != NetTeam.Blue) { Clear(); return; }

        // 우리 팀 깃발이면 파랑, 상대 팀 깃발이면 빨강. (내 팀을 아직 모르면 1팀 깃발 = 빨강, 2팀 깃발 = 파랑)
        bool ourFlag = TeamView.LocalTeam != NetTeam.None ? TeamView.IsMyTeam(flagOwnerTeam) : flagOwnerTeam == NetTeam.Blue;
        if (blueFlagLook != null) blueFlagLook.SetActive(active && ourFlag);
        if (redFlagLook != null) redFlagLook.SetActive(active && !ourFlag);
    }

    public void Clear()
    {
        if (blueFlagLook != null) blueFlagLook.SetActive(false);
        if (redFlagLook != null) redFlagLook.SetActive(false);
    }
}
