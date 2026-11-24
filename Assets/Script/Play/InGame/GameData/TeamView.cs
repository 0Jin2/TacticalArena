using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// TeamView : "이 유저/팀이 나에게 어떤 관계인가"를 알려 주고, 그에 맞는 색/이름을 돌려주는 도우미
//
//   관계: Self(나) / Ally(같은 팀) / Enemy(상대 팀) / None(아직 모름)
//   색과 이름은 TeamColorSettings (Assets/Resources/TeamColorSettings) 에서 가져온다.
//
//   예)  nicknameText.color = TeamView.ColorOf(player);          // 나 노랑 / 아군 파랑 / 적 빨강
//        teamText.text      = TeamView.TeamName(player.Team);     // "1팀" / "2팀"
//        bool mine          = TeamView.IsMyTeam(team);            // 우리 팀인가
// ─────────────────────────────────────────────────────────────────────────────

public enum TeamRelation : byte { None = 0, Self = 1, Ally = 2, Enemy = 3 }

public static class TeamView
{
    private const string SettingsResourcePath = "TeamColorSettings";
    private static TeamColorSettings settings;

    /// <summary>색/이름 설정. Resources 에 에셋이 없으면 기본값으로 만든다.</summary>
    public static TeamColorSettings Settings
    {
        get
        {
            if (settings == null)
            {
                settings = Resources.Load<TeamColorSettings>(SettingsResourcePath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<TeamColorSettings>();
                    settings.hideFlags = HideFlags.HideAndDontSave;
                }
            }
            return settings;
        }
    }

    /// <summary>내 팀. 아직 모르면 None</summary>
    public static NetTeam LocalTeam
    {
        get
        {
            var net = InGameNet.Instance;
            var me = net != null ? net.LocalPlayer : null;
            return me != null ? me.Team : NetTeam.None;
        }
    }

    /// <summary>우리 팀인가 (내 팀을 모르면 false)</summary>
    public static bool IsMyTeam(NetTeam team)
    {
        var local = LocalTeam;
        return team != NetTeam.None && local != NetTeam.None && team == local;
    }

    /// <summary>상대 팀 (Red ↔ Blue)</summary>
    public static NetTeam OtherTeam(NetTeam team)
    {
        return team == NetTeam.Red ? NetTeam.Blue : team == NetTeam.Blue ? NetTeam.Red : NetTeam.None;
    }

    // ── 관계 ────────────────────────────────────────────────────────────────
    public static TeamRelation RelationOf(NetPlayer player)
    {
        return player == null ? TeamRelation.None : RelationOf(player.IsLocal, player.Team);
    }

    public static TeamRelation RelationOf(bool isLocal, NetTeam team)
    {
        if (isLocal) return TeamRelation.Self;
        return RelationOfTeam(team);
    }

    /// <summary>팀 단위 관계 (나를 따로 구분하지 않음): Ally / Enemy / None</summary>
    public static TeamRelation RelationOfTeam(NetTeam team)
    {
        var local = LocalTeam;
        if (team == NetTeam.None || local == NetTeam.None) return TeamRelation.None;
        return team == local ? TeamRelation.Ally : TeamRelation.Enemy;
    }

    // ── 색 ──────────────────────────────────────────────────────────────────
    public static Color ColorOf(TeamRelation relation)
    {
        var s = Settings;
        switch (relation)
        {
            case TeamRelation.Self: return s.selfColor;
            case TeamRelation.Ally: return s.allyColor;
            case TeamRelation.Enemy: return s.enemyColor;
            default: return s.unknownColor;
        }
    }

    /// <summary>유저 색: 나 노랑 / 같은 팀 파랑 / 상대 팀 빨강</summary>
    public static Color ColorOf(NetPlayer player) { return ColorOf(RelationOf(player)); }

    /// <summary>캐릭터 색 (PlayerEntity 등): 내 캐릭터면 노랑</summary>
    public static Color ColorOf(bool isLocal, NetTeam team) { return ColorOf(RelationOf(isLocal, team)); }

    /// <summary>팀 색: 우리 팀 파랑 / 상대 팀 빨강</summary>
    public static Color TeamColor(NetTeam team) { return ColorOf(RelationOfTeam(team)); }

    /// <summary>팀 패널 배경 색: 우리 팀 어두운 파랑 / 상대 팀 어두운 빨강</summary>
    public static Color PanelColor(NetTeam team)
    {
        return RelationOfTeam(team) == TeamRelation.Enemy ? Settings.enemyPanelColor : Settings.allyPanelColor;
    }

    // ── 이름 ────────────────────────────────────────────────────────────────
    /// <summary>"1팀" / "2팀" (Red = 1팀, Blue = 2팀)</summary>
    public static string TeamName(NetTeam team)
    {
        var s = Settings;
        if (team == NetTeam.Red) return s.team1Name;
        if (team == NetTeam.Blue) return s.team2Name;
        return string.Empty;
    }

    /// <summary>TMP 글자에 색을 입힌 문자열: &lt;color=#RRGGBB&gt;글자&lt;/color&gt;</summary>
    public static string Colorize(string text, Color color)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";
    }
}
