using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CharacterDetailPanelUI : MonoBehaviour
{
    [Header("총기/스킬/스탯 UI")]
    public Image weaponImage;
    public TMP_Text weaponDescriptionText;
    public Image skillIconImage;
    public TMP_Text skillDescriptionText;
    public TMP_Text moveSpeedText;
    public TMP_Text maxHPText;
    public TMP_Text goldPriceText;
    public TMP_Text gemPriceText;
    public TMP_Text characterNameText;
    public GameObject buyButtonObj;

    public GameObject buyCompletePopupObj;
    public Button goldBuyButton; // 골드 구매 버튼
    public Button jemBuyButton;  // 젬 구매 버튼
    public TMP_Text goldBuyPriceText; // 골드 구매 버튼 내 텍스트
    public TMP_Text jemBuyPriceText;  // 젬 구매 버튼 내 텍스트

    private CharacterDisplayData currentData;
    private int userGold = 0;
    private int userJem = 0;

    private void Start()
    {
        if (goldBuyButton != null) goldBuyButton.onClick.AddListener(OnClickGoldBuy);
        if (jemBuyButton != null) jemBuyButton.onClick.AddListener(OnClickJemBuy);

        // 재화 변경 시 항상 버튼 갱신
        UserInfoManager.Instance.onCurrencyChanged += (g, j) =>
        {
            RefreshBuyButtonUI();
        };
    }

    public void SetCurrency(int gold, int jem)
    {
        userGold = gold;
        userJem = jem;
        RefreshBuyButtonUI();
    }

    public void ApplyDisplayData(CharacterDisplayData data)
    {
        currentData = data;
        if (characterNameText) characterNameText.text = data.characterName;
        if (weaponImage) weaponImage.sprite = data.weaponImage;
        if (weaponDescriptionText) weaponDescriptionText.text = data.weaponDescription;
        if (skillIconImage) skillIconImage.sprite = data.skillIcon;
        if (skillDescriptionText) skillDescriptionText.text = data.skillDescription;
        if (moveSpeedText) moveSpeedText.text = $"{data.moveSpeed}";
        if (maxHPText) maxHPText.text = $"{data.maxHP}";
        if (goldPriceText) goldPriceText.text = $"{data.goldPrice}";
        if (gemPriceText) gemPriceText.text = $"{data.gemPrice}";
        if (goldBuyPriceText) goldBuyPriceText.text = data.goldPrice.ToString();
        if (jemBuyPriceText) jemBuyPriceText.text = data.gemPrice.ToString();

        // 캐릭터 바꿀 때마다 버튼 상태 즉시 갱신
        RefreshBuyButtonUI();
    }

    private void RefreshBuyButtonUI()
    {
        if (currentData == null)
        {
            if (goldBuyButton != null) goldBuyButton.interactable = false;
            if (jemBuyButton != null) jemBuyButton.interactable = false;
            return;
        }

        bool isLocked = UserCharacterDataManager.Instance.IsCharacterLocked(currentData.characterDataKey);

        // 이미 잠금해제 상태면 버튼 비활성화
        if (!isLocked)
        {
            if (goldBuyButton != null) goldBuyButton.interactable = false;
            if (jemBuyButton != null) jemBuyButton.interactable = false;
            if (buyButtonObj != null) buyButtonObj.SetActive(false);
            return;
        }
        else
        {
            if (buyButtonObj != null) buyButtonObj.SetActive(true);
        }

        // **여기서 항상 최신 재화 상태를 UserInfoManager에서 직접 가져옴**
        int userGold = UserInfoManager.Instance.GetGold();
        int userJem = UserInfoManager.Instance.GetJem();

        if (goldBuyButton != null)
            goldBuyButton.interactable = (userGold >= currentData.goldPrice);

        if (jemBuyButton != null)
            jemBuyButton.interactable = (userJem >= currentData.gemPrice);
    }

    public void OnClickGoldBuy()
    {
        if (currentData == null) return;
        int userGold = UserInfoManager.Instance.GetGold();
        if (userGold < currentData.goldPrice) return;

        UserInfoManager.Instance.ConsumeCurrency(currentData.goldPrice, 0, () =>
        {
            UserCharacterDataManager.Instance.UnlockCharacter(currentData.characterDataKey);
            RefreshBuyButtonUI();

            if (buyCompletePopupObj != null)
                buyCompletePopupObj.SetActive(false);
        });

        var selectButtons = FindObjectsOfType<CharacterSelectButtonUI>();
        foreach (var btn in selectButtons)
        {
            if (btn.displayData != null && btn.displayData.characterDataKey == currentData.characterDataKey)
            {
                btn.UnlockAndHideLockIcon();
                break;
            }
        }
    }

    public void OnClickJemBuy()
    {
        if (currentData == null) return;
        int userJem = UserInfoManager.Instance.GetJem();
        if (userJem < currentData.gemPrice) return;

        UserInfoManager.Instance.ConsumeCurrency(0, currentData.gemPrice, () =>
        {
            UserCharacterDataManager.Instance.UnlockCharacter(currentData.characterDataKey);
            RefreshBuyButtonUI();

            if (buyCompletePopupObj != null)
                buyCompletePopupObj.SetActive(false);
        });

        var selectButtons = FindObjectsOfType<CharacterSelectButtonUI>();
        foreach (var btn in selectButtons)
        {
            if (btn.displayData != null && btn.displayData.characterDataKey == currentData.characterDataKey)
            {
                btn.UnlockAndHideLockIcon();
                break;
            }
        }
    }

    public void ApplyLockState(bool isLocked)
    {
        if (buyButtonObj != null)
            buyButtonObj.SetActive(isLocked);
    }
}