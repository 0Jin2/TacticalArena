using TMPro;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// PlayerNameTag : 캐릭터 머리 위 닉네임 (기존 NameTagUI 대체)
//   색상: 나 = 노랑 / 우리 팀 = 초록 / 상대 팀 = 빨강
//   연결이 끊긴 유저는 닉네임 뒤에 표시를 붙인다.
// ─────────────────────────────────────────────────────────────────────────────

public class PlayerNameTag : MonoBehaviour
{
    [Header("연결")]
    public TMP_Text text;

    [Header("색상")]
    public Color mineColor = Color.yellow;
    public Color allyColor = Color.green;
    public Color enemyColor = Color.red;

    [Header("연결 끊김 표시")]
    public string offlineSuffix = " (재접속 중)";

    private PlayerEntity entity;
    private float initialScaleX = 1f;

    private void Awake()
    {
        entity = GetComponentInParent<PlayerEntity>();
        initialScaleX = transform.localScale.x;
        if (text == null) text = GetComponentInChildren<TMP_Text>(true);
    }

    private void Start()
    {
        if (entity == null) { enabled = false; return; }
        entity.WhenReady(_ => Refresh());
        var net = InGameNet.Instance;
        if (net != null) net.PlayersChanged += Refresh;
    }

    private void OnDestroy()
    {
        var net = InGameNet.Instance;
        if (net != null) net.PlayersChanged -= Refresh;
    }

    public void Refresh()
    {
        if (text == null || entity == null || !entity.IsInitialized) return;

        var net = InGameNet.Instance;
        var local = net != null ? net.LocalPlayer : null;

        string nick = entity.Nickname;
        if (entity.Info != null && !entity.Info.IsOnline) nick += offlineSuffix;
        text.text = nick;

        if (entity.IsMine) text.color = mineColor;
        else if (local != null && local.Team == entity.Team) text.color = allyColor;
        else text.color = enemyColor;
    }

    private void LateUpdate()
    {
        // 캐릭터가 뒤집혀도 글자는 항상 정방향
        if (transform.parent != null)
        {
            float parentSign = Mathf.Sign(transform.parent.lossyScale.x == 0f ? 1f : transform.parent.lossyScale.x);
            var ls = transform.localScale;
            ls.x = Mathf.Abs(initialScaleX) * (initialScaleX < 0f ? -1f : 1f) * parentSign;
            transform.localScale = ls;
        }
    }
}
