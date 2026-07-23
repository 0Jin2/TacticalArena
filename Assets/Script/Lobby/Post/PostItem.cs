using UnityEngine;
using UnityEngine.UI;
using BackEnd;
using TMPro;

public class PostItem : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI contentText;
    public TextMeshProUGUI rewardText;
    public Image postImage;

    public Button receiveButton;

    private string postID;
    public PostType PostType { get; private set; }

    public void Initialize(LitJson.JsonData postData, PostType postType)
    {
        PostType = postType;
        postID = postData["inDate"].ToString();
        titleText.text = postData["title"].ToString();
        contentText.text = postData["content"].ToString();

        // 보상 정보 설정
        if (postData.Keys.Contains("item"))
        {
            var item = postData["item"];
            rewardText.text = $"{item["itemName"]} x{item["itemCount"]}";
        }
        else
        {
            rewardText.text = "보상 없음";
        }

        receiveButton.onClick.AddListener(ReceivePost);
    }

    public void ReceivePost()
    {
        Backend.UPost.ReceivePostItem(PostType, postID, callback =>
        {
            if (callback.IsSuccess())
            {
                Debug.Log($"✅ 우편 {postID} 수령 완료!");
                Destroy(gameObject); // 수령 후 즉시 삭제
            }
            else
            {
                Debug.LogError($"❌ 우편 {postID} 수령 실패: {callback.GetErrorCode()} - {callback.GetMessage()}");
            }
        });
    }
}
