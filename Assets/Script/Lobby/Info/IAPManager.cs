using UnityEngine;
using UnityEngine.Purchasing;
using System.Collections.Generic;

public class IAPManager : MonoBehaviour, IStoreListener
{
    public static IAPManager Instance;

    private static IStoreController storeController;
    private static IExtensionProvider storeExtensionProvider;

    [Header("UI Prefab")]
    public GameObject productUIPrefab; // IAPProductUI 프리팹
    public Transform contentRoot;       // 상품 UI가 배치될 부모 (ScrollView Content 등)
    public GameObject resultPopupPrefab; // ResultPopup 프리팹
    public Transform popupParent;        // 팝업이 표시될 Canvas Transform

    [System.Serializable]
    public class ProductInfo
    {
        public string id;       // 콘솔 Product ID
        public string title;    // 상품 이름
        public Sprite icon;     // 아이콘
        public int rewardJem;   // 지급할 젬 수량
    }

    [Header("상품 목록")]
    public List<ProductInfo> products = new List<ProductInfo>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        InitializeIAP();
    }

    private void InitializeIAP()
    {
        if (storeController != null) return;

        var builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());

        foreach (var p in products)
        {
            builder.AddProduct(p.id, ProductType.Consumable);
        }

        UnityPurchasing.Initialize(this, builder);
        Debug.Log("🛒 IAP 초기화 요청됨");
    }

    public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
    {
        Debug.Log("✅ IAP 초기화 성공");

        storeController = controller;
        storeExtensionProvider = extensions;

        // UI 생성
        foreach (var p in products)
        {
            var obj = Instantiate(productUIPrefab, contentRoot);
            var ui = obj.GetComponent<IAPProductUI>();

            Product product = storeController.products.WithID(p.id);
            string localizedPrice = product != null ? product.metadata.localizedPriceString : "???";

            ui.Setup(p.title, p.icon, localizedPrice, () =>
            {
                BuyProduct(p.id);
            });
        }
    }

    public void OnInitializeFailed(InitializationFailureReason error)
    {
        Debug.LogWarning($"❌ IAP 초기화 실패: {error}");
        ShowPopup($"구매 시스템 초기화 실패: {error}");
    }

    public void OnInitializeFailed(InitializationFailureReason error, string message)
    {
        Debug.LogWarning($"❌ IAP 초기화 실패: {error}, {message}");
        ShowPopup($"구매 시스템 초기화 실패: {message}");
    }

    public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs e)
    {
        Debug.Log($"🎉 구매 성공: {e.purchasedProduct.definition.id}");

        var info = products.Find(p => p.id == e.purchasedProduct.definition.id);
        if (info != null)
        {
            int amount = info.rewardJem;
            UserInfoManager.Instance.AddJem(amount, () =>
            {
                ShowPopup($"💎 젬 {amount}개가 성공적으로 지급되었습니다!");
            });
        }
        else
        {
            Debug.LogWarning("⚠️ 등록되지 않은 상품 ID");
            ShowPopup("⚠️ 등록되지 않은 상품입니다.");
        }

        return PurchaseProcessingResult.Complete;
    }

    public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
    {
        Debug.LogWarning($"❌ 구매 실패: {product.definition.id}, 이유={failureReason}");
        ShowPopup($"구매 실패: {failureReason}");
    }

    public void BuyProduct(string productId)
    {
        if (storeController == null)
        {
            Debug.LogWarning("❌ IAP 미초기화 상태에서 구매 시도");
            ShowPopup("구매가 불가능합니다. 잠시 후 다시 시도해주세요.");
            return;
        }

        Product product = storeController.products.WithID(productId);
        if (product != null && product.availableToPurchase)
        {
            storeController.InitiatePurchase(productId);
        }
        else
        {
            Debug.LogWarning("❌ 상품이 구매 불가 상태");
            ShowPopup("이 상품은 현재 구매할 수 없습니다.");
        }
    }

    private void ShowPopup(string message)
    {
        if (resultPopupPrefab != null && popupParent != null)
        {
            var popup = Instantiate(resultPopupPrefab, popupParent);
            var script = popup.GetComponent<ResultPopup>();
            if (script != null)
            {
                script.SetMessage(message);
            }
        }
        else
        {
            Debug.Log($"[Popup] {message}");
        }
    }
}
