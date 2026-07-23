using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FriendRequestItem : MonoBehaviour
{
    [Header("UI 컴포넌트")]
    [SerializeField] private TMP_Text nicknameText;      // 친구 요청자의 닉네임을 표시할 텍스트
    [SerializeField] private TMP_Text levelText;         // 친구 요청자의 레벨을 표시할 텍스트
    [SerializeField] private Button acceptButton;      // 요청 수락 버튼
    [SerializeField] private Button declineButton;     // 요청 거절 버튼

    private FriendData friendData;                   // 현재 항목에 해당하는 친구 데이터
    private FriendRequestManager friendRequestManager; // 요청 수락/거절 처리를 위한 매니저 참조

    public void SetData(FriendData data, FriendRequestManager manager)
    {
        friendData = data;
        friendRequestManager = manager;

        if (nicknameText != null)
            nicknameText.text = data.Nickname;
        if (levelText != null)
            levelText.text = "LV." + data.Level.ToString();

        if (acceptButton != null)
        {
            acceptButton.onClick.RemoveAllListeners();
            acceptButton.onClick.AddListener(OnAccept);
        }
        if (declineButton != null)
        {
            declineButton.onClick.RemoveAllListeners();
            declineButton.onClick.AddListener(OnDecline);
        }
    }

    private void OnAccept()
    {
        Debug.Log($"✅ [DEBUG] 친구 요청 수락 요청: {friendData.Nickname}");

        if (friendRequestManager != null)
        {
            friendRequestManager.AcceptFriendRequest(friendData.InDate);
            FindObjectOfType<FriendListManager>()?.FetchFriendList();  // 친구 목록 갱신
            Destroy(gameObject); // UI에서 제거
        }
    }

    private void OnDecline()
    {
        Debug.Log($"❌ [DEBUG] 친구 요청 거절 요청: {friendData.Nickname}");

        if (friendRequestManager != null)
        {
            friendRequestManager.DeclineFriendRequest(friendData.InDate);
            Destroy(gameObject); // UI에서 제거
        }
    }

}
