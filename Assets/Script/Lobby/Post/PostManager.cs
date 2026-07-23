using UnityEngine;
using System.Collections.Generic;
using BackEnd;
using UnityEngine.UI;

public class PostManager : MonoBehaviour
{
    public static PostManager Instance;

    public Transform postContent; // 우편 목록을 표시할 부모 오브젝트
    public GameObject adminPostPrefab;
    public GameObject couponPostPrefab;

    public Button receiveAllButton;

    private List<PostItem> currentPosts = new List<PostItem>();
    private bool isInitialized = false; // ✅ 중복 실행 방지 플래그

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        FetchPostList();
    }

    private void Start()
    {
        receiveAllButton.onClick.AddListener(ReceiveAllPosts);
    }

    public void FetchPostList()
    {
        ClearPostList();
        bool hasNewPost = false;

        // 관리자 우편 불러오기
        Backend.UPost.GetPostList(PostType.Admin, 100, callback =>
        {
            if (callback.IsSuccess())
            {
                var posts = callback.GetReturnValuetoJSON()["postList"];
                foreach (LitJson.JsonData post in posts)
                {
                    CreatePostItem(post, PostType.Admin);
                    hasNewPost = true;
                }
            }
        });

        // 쿠폰 우편 불러오기
        Backend.UPost.GetPostList(PostType.Coupon, 100, callback =>
        {
            if (callback.IsSuccess())
            {
                var posts = callback.GetReturnValuetoJSON()["postList"];
                foreach (LitJson.JsonData post in posts)
                {
                    CreatePostItem(post, PostType.Coupon);
                    hasNewPost = true;
                }
            }
        });
    }

    private void ClearPostList()
    {
        foreach (Transform child in postContent)
        {
            Destroy(child.gameObject);
        }
    }

    private void CreatePostItem(LitJson.JsonData postData, PostType postType)
    {
        GameObject postPrefab = null;

        switch (postType)
        {
            case PostType.Admin:
                postPrefab = adminPostPrefab;
                break;
            case PostType.Coupon:
                postPrefab = couponPostPrefab;
                break;
        }

        if (postPrefab != null)
        {
            GameObject newPost = Instantiate(postPrefab, postContent);
            PostItem postItem = newPost.GetComponent<PostItem>();
            postItem.Initialize(postData, postType);
            currentPosts.Add(postItem);
        }
    }

    public void ReceiveAllPosts()
    {
        ReceivePostsByType(PostType.Admin);
        ReceivePostsByType(PostType.Coupon);
    }

    private void ReceivePostsByType(PostType postType)
    {
        Backend.UPost.ReceivePostItemAll(postType, callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log($"{postType} 우편 모두 수령 및 삭제");
                FetchPostList();
            }
            else
            {
                Debug.LogError($"{postType} 우편 모두 수령 실패: {callback.GetErrorCode()} - {callback.GetMessage()}");
            }
        });
    }
}
