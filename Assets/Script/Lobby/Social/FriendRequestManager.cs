using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using BackEnd;
using LitJson;

public class FriendRequestManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Transform friendRequestContent;         // 친구 요청 스크롤뷰 Content
    [SerializeField] private GameObject friendRequestItemPrefab;       // 친구 요청 항목 프리팹

    private List<FriendData> friendRequestList = new List<FriendData>();
    private HashSet<string> nicknameSet = new HashSet<string>();

    // 중복 검사
    public bool IsFriendRequestExists(string nickname)
    {
        return nicknameSet.Contains(nickname);
    }

    public void AddFriendRequest(FriendData newRequest)
    {
        if (nicknameSet.Contains(newRequest.Nickname))
        {
            Debug.LogWarning($"이미 요청 목록에 있는 유저:{newRequest.Nickname}");
            return;
        }
        friendRequestList.Add(newRequest);
        nicknameSet.Add(newRequest.Nickname);
        UpdateFriendRequestUI();
    }

    private void UpdateFriendRequestUI()
    {
        foreach (Transform child in friendRequestContent)
        {
            if (child.GetComponent<DontDestroyOnRefresh>() == null)
            {
                Destroy(child.gameObject);
            }
        }
        nicknameSet.Clear();

        foreach (FriendData request in friendRequestList)
        {
            if (nicknameSet.Contains(request.Nickname))
                continue;

            GameObject itemObj = Instantiate(friendRequestItemPrefab, friendRequestContent);
            FriendRequestItem item = itemObj.GetComponent<FriendRequestItem>();
            if (item != null)
            {
                item.SetData(request, this);
                nicknameSet.Add(request.Nickname);
            }
        }
    }

    public void AcceptFriendRequest(string inDate)
    {
        Backend.Friend.AcceptFriend(inDate, result =>
        {
            if (result.IsSuccess())
            {
                Debug.Log($"✅ 친구 요청 수락 성공: {inDate}");
                RemoveFriendRequest(inDate);

                // ✅ 친구 목록 갱신을 위해 FetchFriendList() 강제 호출
                Debug.Log("✅ 친구 목록 갱신 요청");
                FindObjectOfType<FriendListManager>()?.FetchFriendList();
            }
            else
            {
                Debug.LogError($"❌ 친구 요청 수락 실패: {result.GetErrorCode()} - {result.GetMessage()}");
            }
        });
    }


    public void DeclineFriendRequest(string inDate)
    {
        Backend.Friend.RejectFriend(inDate, callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log($"❌ 친구 요청 거절 성공: {inDate}");
                RemoveFriendRequest(inDate);

                // ✅ 친구 요청 목록 갱신 강제 호출
                Debug.Log("✅ 친구 요청 목록 갱신 요청");
                FetchReceivedRequests();
            }
            else
            {
                Debug.LogError($"❌ 친구 요청 거절 실패: {callback.GetErrorCode()} - {callback.GetMessage()}");
            }
        });
    }


    private void RemoveFriendRequest(string inDate)
    {
        friendRequestList.RemoveAll(req => req.InDate == inDate);
        UpdateFriendRequestUI();
    }

    public void FetchReceivedRequests()
    {
        Debug.Log("🔍 [DEBUG] 친구 요청 목록 불러오기 요청");
        Backend.Friend.GetReceivedRequestList(receivedRequestCallback =>
        {
            if (receivedRequestCallback.IsSuccess())
            {
                JsonData rows = receivedRequestCallback.GetReturnValuetoJSON()["rows"];
                Debug.Log($"✅ [DEBUG] 받은 친구 요청 목록 응답: {receivedRequestCallback.GetReturnValuetoJSON()}");

                friendRequestList.Clear();
                nicknameSet.Clear();

                foreach (JsonData request in rows)
                {
                    if (!request.ContainsKey("inDate")) continue;
                    string inDate = request["inDate"]["S"].ToString();
                    if (string.IsNullOrEmpty(inDate)) continue;
                    FetchFriendInfo(inDate);
                }
            }
            else
            {
                Debug.LogError($"❌ [ERROR] 친구 요청 목록 불러오기 실패: {receivedRequestCallback.GetMessage()}");
            }
        });
    }

    // ✅ 닉네임 + 레벨 가져오기
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
                friendRequestList.Add(new FriendData(nickname, level, inDate));
                UpdateFriendRequestUI();
            }
            else
            {
                Debug.LogError($"❌ [ERROR] 레벨 조회 실패: {dataCallback.GetErrorCode()} - {dataCallback.GetMessage()}");
            }
        });
    }
}
