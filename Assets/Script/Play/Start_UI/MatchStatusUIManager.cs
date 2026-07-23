using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class MatchStatusUIManager : MonoBehaviour
{
    public static MatchStatusUIManager Instance;

    public Transform redTeamParent;
    public Transform blueTeamParent;
    public GameObject matchStatusItemPrefab;

    private Dictionary<PlayerRef, MatchStatusItem> items = new();

    private void Awake()
    {
        Instance = this;
    }

    public void CreateAllPlayerStatusItems()
    {
        var runner = FindObjectOfType<NetworkRunner>();
        var teamAssigner = TeamAssigner.Instance;

        foreach (var player in runner.ActivePlayers)
        {
            var team = teamAssigner.GetTeam(player);
            var nickname = teamAssigner.Nicknames[player];
            var charId = CharacterSelectionNetworkData.Instance.SelectedCharacters[player];
            var character = CharacterSelectionManager.Instance.GetCharacterInfoById(charId);

            var parent = team == TeamAssigner.Team.Red ? redTeamParent : blueTeamParent;
            var itemObj = Instantiate(matchStatusItemPrefab, parent);
            var item = itemObj.GetComponent<MatchStatusItem>();
            item.Initialize(player, nickname, character.characterIcon);

            items[player] = item;
        }
    }

    private void Update()
    {
        if (MatchStatusTracker.Instance == null || TeamAssigner.Instance == null) return;

        foreach (var kvp in items)
        {
            var player = kvp.Key;
            var item = kvp.Value;

            MatchStatusTracker.Instance.TryGetKill(player, out int kills);
            MatchStatusTracker.Instance.TryGetDeath(player, out int deaths);

            item.UpdateKillDeath(kills, deaths);

            var runner = FindObjectOfType<NetworkRunner>();
            if (runner.TryGetPlayerObject(player, out var netObj))
            {
                var health = netObj.GetComponent<Health>();
                item.SetDead(health?.currentHP == 0);
            }
        }

        // 🔴 팀 점수 UI 갱신
        GamePlayUIController.Instance?.UpdateTeamScoreUI(
            TeamAssigner.Instance.redTeamScore,
            TeamAssigner.Instance.blueTeamScore
        );
    }
}
