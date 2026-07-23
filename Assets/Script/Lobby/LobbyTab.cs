using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LobbyTab : MonoBehaviour
{
    private UserInfoManager userInfoManager;

    void Start()
    {
        userInfoManager = FindObjectOfType<UserInfoManager>();
    }

    public void OnLobbyTabToggled()
    {
        if (userInfoManager != null)
        {
            userInfoManager.LoadUserData();
            Debug.Log("✅ 로비 정보가 갱신되었습니다.");
        }
        else
        {
            Debug.LogError("❌ UserInfoManager를 찾을 수 없습니다!");
        }
    }
}
