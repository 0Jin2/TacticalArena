using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Fusion;

[RequireComponent(typeof(Canvas))]
public class NameTagUI : MonoBehaviour
{
    public TMP_Text text;                // Inspector에 드래그
    private PlayerNetworkController pc;  // 부모에서 가져올 컨트롤러
    private PlayerRef owner;             // 이 NameTag의 주인 PlayerRef
    private TeamAssigner.Team myTeam;    // 내 팀
    private TeamAssigner.Team ownerTeam; // 주인 팀

    private float initialLocalScaleX;

    void Awake()
    {
        pc = GetComponentInParent<PlayerNetworkController>();
        if (pc == null)
            Debug.LogError("NameTagUI가 PlayerNetworkController를 찾지 못했습니다.");

        initialLocalScaleX = transform.localScale.x;
    }

    IEnumerator Start()
    {
        // Nicknames 딕셔너리가 준비될 때까지 대기
        while (TeamAssigner.Instance == null
               || !TeamAssigner.Instance.Nicknames.ContainsKey(pc.Object.InputAuthority))
            yield return null;

        owner = pc.Object.InputAuthority;

        myTeam = TeamAssigner.Instance.GetTeam(pc.Runner.LocalPlayer);
        ownerTeam = TeamAssigner.Instance.GetTeam(owner);

        // 닉네임과 색상 설정
        UpdateNameAndColor(TeamAssigner.Instance.Nicknames[owner]);

        // 닉네임 변경 이벤트 구독
        TeamAssigner.Instance.OnNicknameUpdated += (player, newName) =>
        {
            if (player == owner)
                UpdateNameAndColor(newName);
        };
    }

    void UpdateNameAndColor(string nickname)
    {
        text.text = nickname;

        if (owner == pc.Runner.LocalPlayer)
            text.color = Color.yellow;
        else if (ownerTeam == myTeam)
            text.color = Color.green;
        else
            text.color = Color.red;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            // 카메라를 바라보게
            transform.rotation = Quaternion.LookRotation(
                transform.position - cam.transform.position,
                Vector3.up
            );
        }

        // 부모 뒤집힘 보정 (텍스트는 항상 정방향)
        if (transform.parent != null)
        {
            float parentSign = Mathf.Sign(transform.parent.localScale.x);
            Vector3 ls = transform.localScale;
            ls.x = initialLocalScaleX * parentSign;
            transform.localScale = ls;
        }
    }
}