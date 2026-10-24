using UnityEngine;
using UnityEngine.UI;
using BackEnd;
using System;
using System.Collections;
using TMPro;
using GoogleMobileAds.Api;

public class AdRewardManager : MonoBehaviour
{
    public static AdRewardManager Instance { get; private set; }

    public TMP_Text goldCountText; // "2/3"
    public TMP_Text jemCountText;  // "0/1"
    public Button goldAdButton;
    public Button jemAdButton;
    public GameObject resultPopupPrefab;
    public Transform popupParent;

    private const string TABLE = "AdRewardCount";
    private int goldCount = 0;
    private int jemCount = 0;
    private string today = "";

    private string adUnitIdGold = "ca-app-pub-2641468964282714/4743495482";
    private string adUnitIdJem = "ca-app-pub-2641468964282714/2376530054";

    private bool goldAdLoading = false;
    private bool jemAdLoading = false;

    private RewardedAd rewardedAdGold = null;
    private RewardedAd rewardedAdJem = null;

    private float adCooldownUntil = 0f;

    private void Awake()
    {
        if (Instance == null) { Instance = this; DontDestroyOnLoad(gameObject); }
        else { Destroy(gameObject); }
    }

    private void Start()
    {
        today = DateTime.Now.ToString("yyyyMMdd");
        StartCoroutine(LoadAdRewardData());
    }

    public IEnumerator LoadAdRewardData()
    {
        bool done = false;
        Backend.GameData.GetMyData(TABLE, new Where(), 1, bro =>
        {
            if (bro.IsSuccess())
            {
                var rows = bro.FlattenRows();
                if (rows.Count > 0 && rows[0].ContainsKey("date") && rows[0]["date"].ToString() == today)
                {
                    goldCount = int.Parse(rows[0]["goldCount"].ToString());
                    jemCount = int.Parse(rows[0]["jemCount"].ToString());
                }
                else
                {
                    ResetToday();
                }
            }
            else
            {
                ResetToday();
            }
            done = true;
        });
        yield return new WaitUntil(() => done);
        UpdateUI();
    }

    private void ResetToday()
    {
        goldCount = 0;
        jemCount = 0;
        Param param = new Param();
        param.Add("date", today);
        param.Add("goldCount", goldCount);
        param.Add("jemCount", jemCount);
        Backend.GameData.Insert(TABLE, param);
    }

    public void UpdateUI()
    {
        bool isAnyLoading = goldAdLoading || jemAdLoading;
        if (goldCountText != null) goldCountText.text = $"{3 - goldCount}/3";
        if (jemCountText != null) jemCountText.text = $"{1 - jemCount}/1";
        if (goldAdButton != null) goldAdButton.interactable = (goldCount < 3 && !isAnyLoading && Time.time > adCooldownUntil);
        if (jemAdButton != null) jemAdButton.interactable = (jemCount < 1 && !isAnyLoading && Time.time > adCooldownUntil);
    }

    // 골드 광고 버튼
    public void OnClickGoldAdReward()
    {
        if (Time.time < adCooldownUntil)
        {
            ShowPopup("잠시 후 다시 시도해주세요.");
            return;
        }
        if (goldCount >= 3)
        {
            ShowPopup("오늘 받을 수 있는 골드 광고 보상을 모두 받았습니다.");
            return;
        }
        if (goldAdLoading || jemAdLoading)
        {
            ShowPopup("이미 광고를 진행 중입니다.");
            return;
        }
        goldAdLoading = true;
        UpdateUI();
        ShowPopup("광고를 불러오는 중입니다...");
        LoadAndShowGoldRewardAd();
    }

    // 잼 광고 버튼
    public void OnClickJemAdReward()
    {
        if (Time.time < adCooldownUntil)
        {
            ShowPopup("잠시 후 다시 시도해주세요.");
            return;
        }
        if (jemCount >= 1)
        {
            ShowPopup("오늘 받을 수 있는 잼 광고 보상을 모두 받았습니다.");
            return;
        }
        if (goldAdLoading || jemAdLoading)
        {
            ShowPopup("이미 광고를 진행 중입니다.");
            return;
        }
        jemAdLoading = true;
        UpdateUI();
        ShowPopup("광고를 불러오는 중입니다...");
        LoadAndShowJemRewardAd();
    }

    // 최신 방식: 골드 보상형 광고 로딩 & 재생
    private void LoadAndShowGoldRewardAd()
    {
        if (rewardedAdGold != null) rewardedAdGold = null;
        AdRequest request = new AdRequest();

        RewardedAd.Load(adUnitIdGold, request, (RewardedAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                goldAdLoading = false;
                adCooldownUntil = Time.time + 5f; // 쿨타임 7초
                UpdateUI();
                ShowPopup("광고 로딩 실패: 잠시 후 다시 시도해주세요.");
                Debug.LogWarning("광고 로딩 실패(골드): " + (error != null ? error.GetMessage() : "Unknown error"));
                return;
            }
            rewardedAdGold = ad;

            rewardedAdGold.OnAdFullScreenContentClosed += () =>
            {
                goldAdLoading = false;
                UpdateUI();
                rewardedAdGold = null;
            };
            rewardedAdGold.OnAdFullScreenContentFailed += (AdError adError) =>
            {
                goldAdLoading = false;
                adCooldownUntil = Time.time + 7f;
                UpdateUI();
                ShowPopup("광고 재생 실패: 잠시 후 다시 시도해주세요.");
                rewardedAdGold = null;
            };

            rewardedAdGold.Show(
                (Reward reward) =>
                {
                    OnGoldAdWatched();
                }
            );
        });
    }

    private void LoadAndShowJemRewardAd()
    {
        if (rewardedAdJem != null) rewardedAdJem = null;
        AdRequest request = new AdRequest();

        RewardedAd.Load(adUnitIdJem, request, (RewardedAd ad, LoadAdError error) =>
        {
            if (error != null || ad == null)
            {
                jemAdLoading = false;
                adCooldownUntil = Time.time + 7f;
                UpdateUI();
                ShowPopup("광고 로딩 실패: 잠시 후 다시 시도해주세요.");
                Debug.LogWarning("광고 로딩 실패(잼): " + (error != null ? error.GetMessage() : "Unknown error"));
                return;
            }
            rewardedAdJem = ad;

            rewardedAdJem.OnAdFullScreenContentClosed += () =>
            {
                jemAdLoading = false;
                UpdateUI();
                rewardedAdJem = null;
            };
            rewardedAdJem.OnAdFullScreenContentFailed += (AdError adError) =>
            {
                jemAdLoading = false;
                adCooldownUntil = Time.time + 7f;
                UpdateUI();
                ShowPopup("광고 재생 실패: 잠시 후 다시 시도해주세요.");
                rewardedAdJem = null;
            };

            rewardedAdJem.Show(
                (Reward reward) =>
                {
                    OnJemAdWatched();
                }
            );
        });
    }

    // AdMob 광고 시청 완료 후 호출됨(골드)
    public void OnGoldAdWatched()
    {
        if (goldCount >= 3) return;
        goldCount++;
        goldAdLoading = false;
        UserInfoManager.Instance.AddGold(50, () =>
        {
            SaveAdRewardData();
            UpdateUI();
            ShowPopup("50골드가 지급되었습니다!");
        });
    }
    public void OnJemAdWatched()
    {
        if (jemCount >= 1) return;
        jemCount++;
        jemAdLoading = false;
        UserInfoManager.Instance.AddJem(10, () =>
        {
            SaveAdRewardData();
            UpdateUI();
            ShowPopup("10젬이 지급되었습니다!");
        });
    }

    private void ShowPopup(string message)
    {
        if (resultPopupPrefab != null && popupParent != null)
        {
            GameObject go = Instantiate(resultPopupPrefab, popupParent);
            ResultPopup popup = go.GetComponent<ResultPopup>();
            if (popup != null)
                popup.SetMessage(message);
        }
    }

    private void SaveAdRewardData()
    {
        Param param = new Param();
        param.Add("date", today);
        param.Add("goldCount", goldCount);
        param.Add("jemCount", jemCount);
        Backend.GameData.Update(TABLE, new Where(), param);
    }
}
