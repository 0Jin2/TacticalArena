using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class IAPProductUI : MonoBehaviour
{
    [Header("UI 요소")]
    public TMP_Text titleText;
    public TMP_Text priceText;
    public Image iconImage;
    public Button buyButton;

    /// <summary>
    /// 상품 정보 UI 세팅
    /// </summary>
    public void Setup(string title, Sprite icon, string price, System.Action onBuy)
    {
        if (titleText) titleText.text = title;
        if (priceText) priceText.text = price;
        if (iconImage) iconImage.sprite = icon;

        if (buyButton)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(() => onBuy?.Invoke());
        }
    }
}
