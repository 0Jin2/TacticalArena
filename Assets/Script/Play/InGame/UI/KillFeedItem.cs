using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
// KillFeedItem : 킬로그 한 줄
//
//  킬 프리팹 3종 (내 킬 / 아군 킬 / 적 팀 킬)
//              : [가해자 캐릭터 아이콘][가해자 닉네임][무기/스킬 아이콘][피해자 캐릭터 아이콘][피해자 닉네임]
//  자멸 프리팹 : [죽은 유저 캐릭터 아이콘][닉네임][사인 아이콘]   (낙사 / 함정 / 폭발 등 가해자가 없는 죽음)
//
//  모든 프리팹에 이 스크립트 하나를 붙이고, 그 프리팹에 있는 칸만 연결하면 된다. (비어 있는 칸은 무시)
//  생성/삭제는 GameHud 가 한다.
// ─────────────────────────────────────────────────────────────────────────────

public class KillFeedItem : MonoBehaviour
{
    [Header("가해자 (킬 프리팹만 연결)")]
    public Image killerIcon;     // 가해자 캐릭터 아이콘
    public TMP_Text killerName;  // 가해자 닉네임

    [Header("가운데 아이콘")]
    [Tooltip("킬 프리팹: 무기/스킬 아이콘 / 자멸 프리팹: 사인 아이콘(낙사·함정·폭발)")]
    public Image causeIcon;      // 킬 프리팹 = 무기/스킬 아이콘, 자멸 프리팹 = 사인 아이콘

    [Header("피해자 (자멸 프리팹은 죽은 유저 본인)")]
    public Image victimIcon;     // 피해자 캐릭터 아이콘
    public TMP_Text victimName;  // 피해자 닉네임

    /// <summary>킬 프리팹용: 누가 누구를 무엇으로 죽였는지 채운다.</summary>
    public void SetKill(Sprite killerSprite, string killerNick, Color killerColor,
                        Sprite middleSprite,
                        Sprite victimSprite, string victimNick, Color victimColor)
    {
        SetImage(killerIcon, killerSprite);
        SetText(killerName, killerNick, killerColor);
        SetImage(causeIcon, middleSprite);
        SetImage(victimIcon, victimSprite);
        SetText(victimName, victimNick, victimColor);
    }

    /// <summary>자멸 프리팹용: 죽은 유저의 캐릭터 아이콘 + 닉네임 + 사인 아이콘을 채운다.</summary>
    public void SetSelfDeath(Sprite victimSprite, string victimNick, Color victimColor, Sprite deathSprite)
    {
        SetImage(victimIcon, victimSprite);
        SetText(victimName, victimNick, victimColor);
        SetImage(causeIcon, deathSprite);
    }

    // 그림이 없으면 Image 를 꺼서 흰 네모가 보이지 않게 한다.
    private static void SetImage(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }

    private static void SetText(TMP_Text text, string value, Color color)
    {
        if (text == null) return;
        text.text = value ?? string.Empty;
        text.color = color;
    }
}
