using UnityEngine;
using Fusion;
using System;

public class FusionTimerData : NetworkBehaviour
{
    // 싱글톤
    public static FusionTimerData Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────────
    // 1) 호스트가 찍은 “UTC Unix 밀리초” 시작 시각
    [Networked(OnChanged = nameof(OnSelectionStartChanged))]
    public long SelectionStartUnixMillis { get; set; }

    [Networked(OnChanged = nameof(OnFinalStartChanged))]
    public long FinalStartUnixMillis { get; set; }

    [Networked]
    public bool IsFinalCountdown { get; set; }
    // ─────────────────────────────────────────────────────────────────

    [Header("타이머 설정 (초)")]
    public float SelectionDuration = 30f;
    public float FinalCountdownDuration = 10f;

    // 클라이언트가 계산해 둘, “호스트 시계 − 내 로컬 시계” 차이
    long _hostToLocalOffsetMillis;

    void Awake()
    {
        Instance = this;
        Application.runInBackground = true;
    }

    public override void Spawned()
    {
        // 호스트(StateAuthority)에서만 최초 시작 시각을 찍어 준다
        if (Object.HasStateAuthority && SelectionStartUnixMillis == 0L)
        {
            SelectionStartUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // FinalStartUnixMillis는 나중에 자동 트리거
        }
    }

    // 호스트가 SelectionStartUnixMillis를 설정하자마자(클라이언트 복제 시점)에
    // 클라이언트 각자가 오프셋을 계산하도록 하는 콜백
    static void OnSelectionStartChanged(Changed<FusionTimerData> info)
    {
        if (!info.Behaviour.Object.HasStateAuthority)
        {
            long hostTime = info.Behaviour.SelectionStartUnixMillis;
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            info.Behaviour._hostToLocalOffsetMillis = hostTime - localNow;
        }
    }

    // FinalCountdown 시작 시점에도 같은 방식으로 offset 보정해 둬도 좋다
    static void OnFinalStartChanged(Changed<FusionTimerData> info)
    {
        if (!info.Behaviour.Object.HasStateAuthority)
        {
            long hostTime = info.Behaviour.FinalStartUnixMillis;
            long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // (offset은 사실 Selection과 같아도 되지만, 네트워크 지연을 재보정)
            info.Behaviour._hostToLocalOffsetMillis = hostTime - localNow;
        }
    }

    public override void FixedUpdateNetwork()
    {
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Tutorial"))
        {
            // Tutorial 모드에서는 선택/파이널 카운트다운 아예 진행되지 않음
            return;
        }

        // 호스트에서만, 30초 지나면 FinalCountdown 트리거
        if (Object.HasStateAuthority && !IsFinalCountdown)
        {
            if (GetRemainingSelectionTime() <= 0f)
            {
                FinalStartUnixMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                IsFinalCountdown = true;
            }
        }
    }

    /// <summary>
    /// “호스트 시계” 기준으로 계산한 남은 선택 시간
    /// </summary>
    public float GetRemainingSelectionTime()
    {
        long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long hostNow = localNow + _hostToLocalOffsetMillis;
        double elapsed = (hostNow - SelectionStartUnixMillis) / 1000.0;
        return Mathf.Max(SelectionDuration - (float)elapsed, 0f);
    }

    /// <summary>
    /// “호스트 시계” 기준으로 계산한 남은 파이널 카운트다운
    /// </summary>
    public float GetRemainingFinalCountdown()
    {
        if (!IsFinalCountdown) return 0f;

        long localNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long hostNow = localNow + _hostToLocalOffsetMillis;
        double elapsed = (hostNow - FinalStartUnixMillis) / 1000.0;
        return Mathf.Max(FinalCountdownDuration - (float)elapsed, 0f);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}