using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BackEnd;
using LitJson;
using System.Text.RegularExpressions;

public class FriendAddPopupManager : MonoBehaviour
{
    [Header("UI 요소")]
    [SerializeField] private TMP_InputField friendNicknameInput;
    [SerializeField] private Button friendAddButton;
    [SerializeField] private GameObject infoPrefab;
    [SerializeField] private Transform popupParent;

    private string myNickname;
    private string myInDate;

    private void Start()
    {
        if (friendAddButton != null)
            friendAddButton.onClick.AddListener(OnFriendAddButtonClicked);

        LoadMyInfo();
    }

    private void LoadMyInfo()
    {
        BackendReturnObject bro = Backend.BMember.GetUserInfo();
        if (bro.IsSuccess())
        {
            JsonData userInfo = bro.GetReturnValuetoJSON();
            myInDate = userInfo["row"]["inDate"].ToString();
            myNickname = userInfo["row"]["nickname"].ToString();
        }
        else
        {
            Debug.LogError("유저 정보를 불러오는 데 실패했습니다:" + bro.GetMessage());
        }
    }

    private void OnFriendAddButtonClicked()
    {
        string targetNickname = friendNicknameInput.text.Trim();
        Debug.Log($"[DEBUG] 입력된 닉네임: {targetNickname}");

        // 1. 닉네임 유효성 검사 (한글/영문 2~8자, 띄어쓰기/특수기호 불가)
        if (!IsValidNickname(targetNickname))
        {
            ShowInfoMessage("닉네임은 한글/영문 2~8자, 띄어쓰기/기호 없이 입력하세요.");
            return;
        }

        // 2. 내 닉네임과 동일 체크
        if (targetNickname == myNickname)
        {
            ShowInfoMessage("자신에게 친구 요청을 보낼 수 없습니다.");
            return;
        }

        Backend.Social.GetUserInfoByNickName(targetNickname, userInfoCallback =>
        {
            if (userInfoCallback.IsSuccess())
            {
                JsonData userInfo = userInfoCallback.GetReturnValuetoJSON();
                if (userInfo.ContainsKey("row") && userInfo["row"].ContainsKey("inDate"))
                {
                    string friendInDate = userInfo["row"]["inDate"].ToString();
                    SendFriendRequest(friendInDate);
                }
                else
                {
                    ShowInfoMessage("닉네임이 실존하지 않습니다.");
                }
            }
            else
            {
                ShowInfoMessage("닉네임 실존하지 않습니다.");
            }
        });
    }

    private void SendFriendRequest(string friendInDate)
    {
        Backend.Friend.RequestFriend(friendInDate, friendCallback =>
        {
            if (friendCallback.IsSuccess())
            {
                ShowInfoMessage("친구 신청 완료");
                FetchSentRequests();
            }
            else
            {
                ShowInfoMessage("친구 신청 실패");
            }
        });
    }

    private void FetchSentRequests()
    {
        Backend.Friend.GetSentRequestList(sentRequestCallback =>
        {
            if (sentRequestCallback.IsSuccess())
            {
                FindObjectOfType<FriendRequestManager>()?.FetchReceivedRequests();
            }
        });
    }

    private void ShowInfoMessage(string message)
    {
        if (infoPrefab != null && popupParent != null)
        {
            GameObject infoObj = Instantiate(infoPrefab, popupParent);
            ResultPopup resultPopup = infoObj.GetComponent<ResultPopup>();
            if (resultPopup != null)
                resultPopup.SetMessage(message);
        }
    }

    private bool IsValidNickname(string nickname)
    {
        // 2~8자, 한글/영문만, 띄어쓰기X, 기호X
        if (string.IsNullOrEmpty(nickname)) return false;
        if (nickname.Length < 2 || nickname.Length > 8) return false;
        Regex regex = new Regex(@"^[가-힣a-zA-Z]{2,8}$");
        return regex.IsMatch(nickname);
    }
}
