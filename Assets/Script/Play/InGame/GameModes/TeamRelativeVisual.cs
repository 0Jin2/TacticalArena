using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// TeamRelativeVisual : 팀 소유 오브젝트(깃발 자리, 팀 기지 등)를 "보는 사람 기준" 색으로 보여 주는 부품
//
//   보는 사람이 그 팀이면 "우리 팀 모습", 아니면 "상대 팀 모습"을 켠다.
//   예) 1팀 깃발 자리 : 1팀 유저에게는 파란 깃발, 2팀 유저에게는 빨간 깃발
//
//   설정 방법 (둘 중 편한 쪽)
//     A) 오브젝트 바꾸기 : allyLook(파란 모습)과 enemyLook(빨간 모습) 오브젝트를 각각 연결
//     B) 색 칠하기       : tintTargets 에 SpriteRenderer 를 넣으면 TeamColorSettings 의 파랑/빨강을 칠함
//                          (원본 그림이 흰색/회색 계열일 때 잘 보입니다)
// ─────────────────────────────────────────────────────────────────────────────

public class TeamRelativeVisual : MonoBehaviour
{
    [Header("이 오브젝트의 주인 팀")]
    public NetTeam ownerTeam = NetTeam.Red;  // 이 오브젝트가 어느 팀 것인지 (Red = 1팀, Blue = 2팀)

    [Header("A) 오브젝트 바꾸기 (선택)")]
    public GameObject allyLook;   // 보는 사람이 주인 팀일 때 켜질 모습 (파란 깃발 등)
    public GameObject enemyLook;  // 보는 사람이 상대 팀일 때 켜질 모습 (빨간 깃발 등)

    [Header("B) 색 칠하기 (선택)")]
    public SpriteRenderer[] tintTargets;  // 우리 팀이면 파랑, 상대 팀이면 빨강으로 칠할 그림

    private TeamRelation applied = (TeamRelation)255;

    private void OnEnable()
    {
        var net = InGameNet.Instance;
        if (net != null) net.PlayersChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        var net = InGameNet.Instance;
        if (net != null) net.PlayersChanged -= Apply;
    }

    private void Update()
    {
        // 내 팀이 늦게 정해지는 경우(재접속 등)를 위해, 모를 때만 계속 확인한다.
        if (applied == TeamRelation.None) Apply();
    }

    public void Apply()
    {
        var relation = TeamView.RelationOfTeam(ownerTeam);
        if (relation == applied) return;
        applied = relation;

        bool mine = relation != TeamRelation.Enemy;      // 아직 모르면 우리 팀 모습으로
        if (allyLook != null && allyLook.activeSelf != mine) allyLook.SetActive(mine);
        if (enemyLook != null && enemyLook.activeSelf == mine) enemyLook.SetActive(!mine);

        if (tintTargets != null && relation != TeamRelation.None)
        {
            Color c = TeamView.TeamColor(ownerTeam);
            foreach (var r in tintTargets) if (r != null) r.color = c;
        }
    }
}
