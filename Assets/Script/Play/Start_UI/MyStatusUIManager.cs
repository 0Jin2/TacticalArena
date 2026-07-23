using UnityEngine;
using Fusion;

public class MyStatusUIManager : MonoBehaviour
{
    public static MyStatusUIManager Instance;

    private void Awake()
    {
        Instance = this;
    }

    public void CreateMyStatusUI()
    {
        Debug.Log("🟡 CreateMyStatusUI 호출됨");

        if (GamePlayUIController.Instance == null)
        {
            Debug.LogWarning("❌ GamePlayUIController.Instance 없음");
            return;
        }

        var runner = FindObjectOfType<NetworkRunner>();
        if (runner == null || !runner.TryGetPlayerObject(runner.LocalPlayer, out var myObj))
        {
            Debug.LogWarning("❌ 내 캐릭터 객체 찾기 실패");
            return;
        }

        var health = myObj.GetComponent<Health>();
        var controller = myObj.GetComponent<PlayerNetworkController>();
        if (health == null || controller == null)
            return;

        var characterId = CharacterSelectionNetworkData.Instance?.SelectedCharacters[runner.LocalPlayer];
        var characterInfo = CharacterSelectionManager.Instance?.GetCharacterInfoById(characterId);
        if (characterInfo == null) return;

        // 기존 UI 제거
        var parent = GamePlayUIController.Instance.myStatusParent;
        foreach (Transform child in parent.transform)
        {
            Destroy(child.gameObject);
        }

        // 새 UI 생성
        var item = Instantiate(GamePlayUIController.Instance.myStatusItemPrefab, parent.transform);
        var script = item.GetComponent<MyStatusItem>();
        script.Initialize(
            TeamAssigner.Instance.Nicknames[runner.LocalPlayer],
            characterInfo.characterIcon,
            health
        );
    }
}
