using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class GameModeHelper
{
    public enum GameMode { TDM, FLAG, BOMB, FD }

    public static GameMode GetModeFromScene()
    {
        var name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (name.Contains("TDM")) return GameMode.TDM;
        if (name.Contains("FLAG")) return GameMode.FLAG;
        if (name.Contains("BOMB")) return GameMode.BOMB;
        if (name.Contains("FD")) return GameMode.FD;
        Debug.LogWarning($"Unknown mode in scene '{name}', defaulting to TDM");
        return GameMode.TDM;
    }
}
