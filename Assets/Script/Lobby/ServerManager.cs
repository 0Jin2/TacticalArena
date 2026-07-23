using UnityEngine;
using BackEnd;
using System;
using System.Collections.Generic;
using UnityEngine.UI;

public class ServerManager : MonoBehaviour
{
    private static ServerManager instance;

    public static ServerManager Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("ServerManager");
                instance = obj.AddComponent<ServerManager>();
                DontDestroyOnLoad(obj);
            }
            return instance;
        }
    }

    public GameObject postNotification;
    public Button postCloseButton; // ✅ Post UI 닫기 버튼 참조

    private Queue<Action> mainThreadQueue = new Queue<Action>();

    private void Start()
    {
        ConnectToNotificationServer();
        CheckForNewPosts();

        if (postCloseButton != null)
        {
            postCloseButton.onClick.AddListener(OnPostClose); // ✅ 버튼 클릭 시 실행되도록 설정
        }
    }

    private void Update()
    {
        if (mainThreadQueue.Count > 0)
        {
            mainThreadQueue.Dequeue().Invoke();
        }
    }

    private void OnPostClose()
    {
        Debug.Log("📩 Post UI 닫기 버튼 클릭됨 - 최신 우편 상태 확인 시작");
        CheckForNewPosts();
    }

    private void ConnectToNotificationServer()
    {
        Backend.Notification.Connect();
        Backend.Notification.OnAuthorize = (result, reason) =>
        {
            if (result)
            {
                Debug.Log("✅ 실시간 알림 서버 연결 성공");
            }
            else
            {
                Debug.LogError($"❌ 실시간 알림 서버 연결 실패: {reason}");
            }
        };

        Backend.Notification.OnReceivedFriendRequest = () =>
        {
            Debug.Log("🔔 친구 요청 도착! UI 갱신 요청");
            mainThreadQueue.Enqueue(() => FindObjectOfType<FriendRequestManager>()?.FetchReceivedRequests());
        };

        Backend.Notification.OnAcceptedFriendRequest = () =>
        {
            Debug.Log("✅ 친구 요청 수락! 친구 목록 갱신 요청");
            mainThreadQueue.Enqueue(() => FindObjectOfType<FriendListManager>()?.FetchFriendList());
        };

        Backend.Notification.OnRejectedFriendRequest = () =>
        {
            Debug.Log("❌ 친구 요청 거절됨");
            mainThreadQueue.Enqueue(() => FindObjectOfType<FriendRequestManager>()?.FetchReceivedRequests());
        };

        Backend.Notification.OnReceivedUserPost = () =>
        {
            Debug.Log("📨 새로운 우편이 도착했습니다!");
            postNotification.SetActive(true); // 우편 알림 활성화
            mainThreadQueue.Enqueue(() => CheckForNewPosts()); // 우편 리스트 갱신
        };
    }

    private void CheckForNewPosts()
    {
        Debug.Log("📩 CheckForNewPosts() 실행 - 우편 목록 확인 시작");

        bool hasNewPost = false;

        // 관리자 우편 조회
        Backend.UPost.GetPostList(PostType.Admin, 100, callback =>
        {
            if (callback.IsSuccess())
            {
                var posts = callback.GetReturnValuetoJSON()["postList"];
                hasNewPost |= posts.Count > 0;
                Debug.Log($"📩 관리자 우편 개수: {posts.Count}개");
            }
            else
            {
                Debug.LogError($"❌ 관리자 우편 조회 실패: {callback.GetErrorCode()} - {callback.GetMessage()}");
            }

            // ✅ 즉시 알림 상태 반영
            UpdateNotificationStatus(hasNewPost);
        });

        // 쿠폰 우편 조회
        Backend.UPost.GetPostList(PostType.Coupon, 100, callback =>
        {
            if (callback.IsSuccess())
            {
                var posts = callback.GetReturnValuetoJSON()["postList"];
                hasNewPost |= posts.Count > 0;
                Debug.Log($"📩 쿠폰 우편 개수: {posts.Count}개");
            }
            else
            {
                Debug.LogError($"❌ 쿠폰 우편 조회 실패: {callback.GetErrorCode()} - {callback.GetMessage()}");
            }

            // ✅ 즉시 알림 상태 반영
            UpdateNotificationStatus(hasNewPost);
        });
    }

    public void UpdateNotificationStatus(bool hasNewPost)
    {
        if (postNotification != null)
        {
            postNotification.SetActive(hasNewPost);
            Debug.Log($"🔔 postNotification 상태 업데이트: {(hasNewPost ? "활성화" : "비활성화")}");
        }
        else
        {
            Debug.LogError("❌ postNotification이 null입니다. Unity Inspector에서 설정되어 있는지 확인하세요.");
        }
    }

    private void OnDestroy()
    {
        Backend.Notification.OnReceivedFriendRequest = null;
        Backend.Notification.OnAcceptedFriendRequest = null;
        Backend.Notification.OnRejectedFriendRequest = null;
        Backend.Notification.OnReceivedUserPost = null;
    }

    private void OnApplicationQuit()
    {
        // 실시간 알림 서버 해제
        Backend.Notification.DisConnect();
    }
}

