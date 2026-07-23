using System;
using Fusion;
using UnityEngine;
using Fusion.Collections;
using System.Collections.Generic;
using System.Linq;

public class CharacterSelectionNetworkData : NetworkBehaviour
{
    public static CharacterSelectionNetworkData Local => Instance;
    public static CharacterSelectionNetworkData Instance { get; private set; }

    [Networked, Capacity(10)] public NetworkDictionary<PlayerRef, string> SelectedCharacters { get; }
    [Networked, Capacity(10)] public NetworkDictionary<PlayerRef, bool> ReadyStates { get; }
    [Networked, Capacity(10)] public NetworkDictionary<PlayerRef, float> Heartbeats { get; }

    public event Action<PlayerRef, string> OnCharacterSelected;
    public event Action<PlayerRef, bool> OnReadyToggled;
    private readonly Dictionary<PlayerRef, string> lastSelected = new();
    private readonly Dictionary<PlayerRef, bool> lastReady = new();

    public override void Spawned()
    {
        Instance = this;

        var ui = FindObjectOfType<CharacterSelectionManager>();
        if (ui != null)
        {
            OnCharacterSelected -= ui.HandleCharacterSelected;
            OnReadyToggled -= ui.HandleReadyStateChanged;

            OnCharacterSelected += ui.HandleCharacterSelected;
            OnReadyToggled += ui.HandleReadyStateChanged;
        }

        StartCoroutine(WaitAndSendNickname());

        if (TeamAssigner.Instance != null)
        {
            TeamAssigner.Instance.OnPlayerTeamAssigned += OnTeamAssigned;
        }

        foreach (var player in Runner.ActivePlayers)
        {
            if (Object.HasStateAuthority && !ReadyStates.ContainsKey(player))
                ReadyStates.Set(player, false);
        }

        foreach (var kvp in SelectedCharacters)
            OnCharacterSelected?.Invoke(kvp.Key, kvp.Value);
        foreach (var kvp in ReadyStates)
            OnReadyToggled?.Invoke(kvp.Key, kvp.Value);

        Debug.Log($"🧬 CharacterSelectionNetworkData Spawned | Authority={Object.InputAuthority}");
    }

    public override void FixedUpdateNetwork()
    {
        foreach (var player in Runner.ActivePlayers)
        {
            if (SelectedCharacters.TryGet(player, out var id))
            {
                if (!lastSelected.ContainsKey(player))
                {
                    lastSelected[player] = id;
                    OnCharacterSelected?.Invoke(player, id);
                }
                else if (lastSelected[player] != id)
                {
                    lastSelected[player] = id;
                    OnCharacterSelected?.Invoke(player, id);
                }
            }

            if (ReadyStates.TryGet(player, out var ready))
            {
                if (!lastReady.ContainsKey(player))
                {
                    lastReady[player] = ready;
                    OnReadyToggled?.Invoke(player, ready);
                }
                else if (lastReady[player] != ready)
                {
                    lastReady[player] = ready;
                    OnReadyToggled?.Invoke(player, ready);
                }
            }
        }
    }

    private void OnTeamAssigned(PlayerRef player)
    {
        if (player != Runner.LocalPlayer) return;
        RequestNickname(GPGS_BACKEND.MyNickname);
        TeamAssigner.Instance.OnPlayerTeamAssigned -= OnTeamAssigned;
    }

    public void RequestSelectCharacter(string characterId)
    {
        Rpc_SubmitCharacterSelection(Runner.LocalPlayer, characterId);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SubmitCharacterSelection(PlayerRef player, string characterId)
    {
        Debug.Log($"🛰 캐릭터 선택 요청 수신: {player} → {characterId}");
        if (string.IsNullOrEmpty(characterId)) return;

        SelectedCharacters.Set(player, characterId);

        // ✅ 강제로 이벤트 재호출 유도
        if (lastSelected.ContainsKey(player))
            lastSelected.Remove(player);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_SubmitReadyToggle(PlayerRef player)
    {
        if (ReadyStates.TryGet(player, out var currentReady))
        {
            ReadyStates.Set(player, !currentReady);
        }
        else
        {
            ReadyStates.Add(player, true);
        }

        // 강제 이벤트 재호출 유도
        if (lastReady.ContainsKey(player))
            lastReady.Remove(player);
    }

    public void RequestNickname(string nickname)
    {
        Rpc_SubmitNicknameRequest(Runner.LocalPlayer, nickname);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SubmitNicknameRequest(PlayerRef player, string nickname)
    {
        Debug.Log($"🛰 닉네임 요청 수신: {player} → {nickname}");
        TeamAssigner.Instance?.RegisterNickname(player, nickname);
    }

    private System.Collections.IEnumerator WaitAndSendNickname()
    {
        // TeamAssigner가 실제로 등장할 때까지 '무기한' 대기 (씬 전환/복제 지연 안전)
        while (TeamAssigner.Instance == null)
            yield return null;

        var player = Runner.LocalPlayer;
        string nick = GPGS_BACKEND.MyNickname;

        // 이미 등록돼 있더라도 한 번 더 보냄(최신값 보정)
        if (TeamAssigner.Instance.Nicknames.ContainsKey(player))
        {
            RequestNickname(nick);
            yield break;
        }

        // 확실히 등록될 때까지 짧은 간격으로 여러 번 전송 (지연/패킷 유실 대비)
        float timeout = 8f;
        float t = 0f;
        while (t < timeout && !TeamAssigner.Instance.Nicknames.ContainsKey(player))
        {
            RequestNickname(nick);
            yield return new WaitForSeconds(0.25f);
            t += 0.25f;
        }
    }

    public void RequestToggleReady()
    {
        var localPlayer = Runner.LocalPlayer;

        // 🔁 먼저 즉시 UI 업데이트용 이벤트 호출 (로컬 반영)
        if (ReadyStates.TryGet(localPlayer, out var current))
        {
            OnReadyToggled?.Invoke(localPlayer, !current);
        }
        else
        {
            OnReadyToggled?.Invoke(localPlayer, true);
        }

        // 🛰 실제 네트워크 요청 전송
        Rpc_SubmitReadyToggle(localPlayer);
    }

    public void ForceToggleReady(PlayerRef player, bool isReady)
    {
        if (!Object.HasStateAuthority) return;

        ReadyStates.Set(player, isReady);
        lastReady[player] = isReady;
        OnReadyToggled?.Invoke(player, isReady);
    }

    public void UpdateHeartbeat()
    {
        if (!Runner || !Runner.IsRunning) return;
        if (!Object.HasStateAuthority) return;

        float now = Runner.SimulationTime;
        Heartbeats.Set(Runner.LocalPlayer, now);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_SendHeartbeat(PlayerRef player, float time)
    {
        Heartbeats.Set(player, time);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}