using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using BackEnd;
using LitJson;

public class FriendListManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform friendListContent;         // 스크롤뷰 Content 영역
    [SerializeField] private GameObject friendListItemPrefab;       // 친구 목록 항목 프리팹

    // 친구 목록 데이터
    private List<FriendData> friendList = new List<FriendData>();

    // 중복 방지용 닉네임 set (추가)
    private HashSet<string> nicknameSet = new HashSet<string>();

    public bool IsFriendExists(string nickname)
    {
        return friendList.Any(friend => friend.Nickname == nickname);
    }

    public void AddFriend(FriendData newFriend)
    {
        if (nicknameSet.Contains(newFriend.Nickname))
        {
            Debug.LogWarning($"이미 친구 목록에 있는 닉네임: {newFriend.Nickname}");
            return;
        }
        friendList.Add(newFriend);
        nicknameSet.Add(newFriend.Nickname);
        UpdateFriendListUI();
    }

    private void OnEnable()
    {
        ClearFriendListUI();
        FetchFriendList();
    }

    private void ClearFriendListUI()
    {
        foreach (Transform child in friendListContent)
        {
            if (child.GetComponent<DontDestroyOnRefresh>() == null)
            {
                Destroy(child.gameObject);
            }
        }
        nicknameSet.Clear();
    }

    public void RemoveFriend(string inDate)
    {
        if (string.IsNullOrEmpty(inDate))
        {
            Debug.LogError("❌ [ERROR] 삭제할 친구 inDate가 올바르지 않음.");
            return;
        }

        Debug.Log($"🔍 [DEBUG] Backend.Friend.BreakFriend() 실행 - inDate: {inDate}");

        Backend.Friend.BreakFriend(inDate, callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log($"✅ 친구 삭제 성공: {inDate}");
                friendList.RemoveAll(friend => friend.InDate == inDate);
                UpdateFriendListUI();
            }
            else
            {
                Debug.LogError($"❌ 친구 삭제 실패: {callback.GetErrorCode()} - {callback.GetMessage()}");
            }
        });
    }

    // 친구 데이터 패치 및 중복 방지
    public void FetchFriendList()
    {
        Debug.Log("🔍 [DEBUG] 친구 목록 불러오기 요청");
        Backend.Friend.GetFriendList(friendListCallback =>
        {
            if (friendListCallback.IsSuccess())
            {
                JsonData rows = friendListCallback.GetReturnValuetoJSON()["rows"];
                Debug.Log($"✅ [DEBUG] 친구 목록 응답: {friendListCallback.GetReturnValuetoJSON()}");

                friendList.Clear();
                nicknameSet.Clear();

                foreach (JsonData friend in rows)
                {
                    if (!friend.ContainsKey("inDate")) continue;
                    string inDate = friend["inDate"]["S"].ToString();
                    if (string.IsNullOrEmpty(inDate)) continue;
                    FetchFriendInfo(inDate);
                }
            }
            else
            {
                Debug.LogError($"❌ [ERROR] 친구 목록 불러오기 실패: {friendListCallback.GetMessage()}");
            }
        });
    }

    private void FetchFriendInfo(string inDate)
    {
        if (string.IsNullOrEmpty(inDate) || inDate == "JsonData object")
        {
            Debug.LogError("❌ [ERROR] FetchFriendInfo() 호출 실패 - inDate가 잘못됨.");
            return;
        }

        Debug.Log($"🔍 [DEBUG] FetchFriendInfo() 호출 - inDate: {inDate}");

        Backend.Social.GetUserInfoByInDate(inDate, userInfoCallback =>
        {
            if (userInfoCallback.IsSuccess())
            {
                JsonData userInfo = userInfoCallback.GetReturnValuetoJSON();
                string nickname = userInfo["row"]["nickname"].ToString();
                Debug.Log($"✅ [DEBUG] 닉네임 조회 성공: {nickname}");

                // 🔥 레벨 가져오기 추가
                FetchFriendLevel(nickname, inDate);
            }
            else
            {
                Debug.LogError($"❌ [ERROR] 닉네임 조회 실패: {userInfoCallback.GetErrorCode()} - {userInfoCallback.GetMessage()}");
                Debug.LogError($"❌ [ERROR] 닉네임 조회 실패한 inDate: {inDate}");
            }
        });
    }

    private void FetchFriendLevel(string nickname, string inDate)
    {
        Where where = new Where();
        where.Equal("owner_inDate", inDate);

        Backend.GameData.Get("UserInfo", where, 1, dataCallback =>
        {
            if (dataCallback.IsSuccess())
            {
                JsonData rows = dataCallback.GetReturnValuetoJSON()["rows"];
                Debug.Log($"✅ [DEBUG] 레벨 데이터 응답: {dataCallback.GetReturnValuetoJSON()}");

                if (rows.Count == 0 || !rows[0].ContainsKey("level"))
                {
                    Debug.LogError("❌ [ERROR] UserInfo 테이블에 level 데이터가 없음!");
                    return;
                }

                JsonData levelData = rows[0]["level"];
                Debug.Log($"🔍 [DEBUG] level 값 (원본): {levelData.ToJson()}");

                int level = 1; // 기본값 설정

                // 🔥 level 값이 {"N": "1"} 형태일 경우 변환
                if (levelData.ContainsKey("N"))
                {
                    string levelString = levelData["N"].ToString();
                    if (!int.TryParse(levelString, out level))
                    {
                        Debug.LogError($"❌ [ERROR] level 값을 int로 변환할 수 없음. level: {levelString}");
                        return;
                    }
                }
                else
                {
                    Debug.LogError("❌ [ERROR] level 값이 예상한 형식이 아님.");
                    return;
                }

                Debug.Log($"✅ [DEBUG] 레벨 변환 성공: {level}");
                friendList.Add(new FriendData(nickname, level, inDate));
                UpdateFriendListUI();
            }
            else
            {
                Debug.LogError($"❌ [ERROR] 레벨 조회 실패: {dataCallback.GetErrorCode()} - {dataCallback.GetMessage()}");
            }
        });
    }

    private void UpdateFriendListUI()
    {
        // UI 모두 제거(고정 오브젝트는 유지)
        foreach (Transform child in friendListContent)
        {
            if (child.GetComponent<DontDestroyOnRefresh>() == null)
            {
                Destroy(child.gameObject);
            }
        }
        nicknameSet.Clear();

        // 중복 체크: 이미 있는 닉네임이면 아이템 생성을 패스
        foreach (FriendData friend in friendList)
        {
            if (nicknameSet.Contains(friend.Nickname))
                continue;

            GameObject itemObj = Instantiate(friendListItemPrefab, friendListContent);
            FriendListItem item = itemObj.GetComponent<FriendListItem>();
            if (item != null)
            {
                item.SetData(friend, this);
                nicknameSet.Add(friend.Nickname);
            }
        }
    }
}
