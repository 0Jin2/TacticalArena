using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FriendListItem : MonoBehaviour
{
    [Header("UI 컴포넌트")]
    [SerializeField] private TMP_Text nicknameText;       // 친구의 닉네임을 표시할 텍스트
    [SerializeField] private TMP_Text levelText;          // 친구의 레벨을 표시할 텍스트
    [SerializeField] private Button deleteFriendButton; // 친구 삭제 버튼

    private FriendData friendData;                // 현재 항목에 해당하는 친구 데이터
    private FriendListManager friendListManager;  // 삭제 요청 등 처리를 위한 매니저 참조

    // 외부에서 데이터를 주입받아 UI를 업데이트하고, 버튼 이벤트를 등록합니다.
    public void SetData(FriendData data, FriendListManager manager)
    {
        friendData = data;
        friendListManager = manager;

        if (nicknameText != null)
            nicknameText.text = data.Nickname;
        if (levelText != null)
            levelText.text = "LV." + data.Level.ToString();

        if (deleteFriendButton != null)
        {
            deleteFriendButton.onClick.RemoveAllListeners();
            deleteFriendButton.onClick.AddListener(OnDeleteFriend);
        }
    }

    public void OnDeleteFriend()
    {
        Debug.Log($"🔍 [DEBUG] 친구 삭제 요청 - 닉네임: {friendData.Nickname}, inDate: {friendData.InDate}");

        if (string.IsNullOrEmpty(friendData.InDate))
        {
            Debug.LogError("❌ [ERROR] 삭제할 친구의 inDate가 비어 있음.");
            return;
        }

        friendListManager.RemoveFriend(friendData.InDate);
    }

}
