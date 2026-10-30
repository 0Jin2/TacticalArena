using System;
using TMPro;
using UnityEngine;

// 기존 캐릭터 프리팹은 Fusion/Netcode 전용 컴포넌트가 붙어 있어 러너 없이 Instantiate하면 동작하지 않는다.
// 뒤끝 매치용 전용 프리팹이 생기기 전까지 외형(SPUM)만 남기고 네트워크 의존 컴포넌트를 제거해 생성한다.
public static class BackendTDMCharacterFactory
{
    public static GameObject CreateLocalCharacter(GameObject prefab, Vector3 position)
    {
        // 비활성 부모 아래에서 생성하면 Awake가 호출되지 않아 네트워크 컴포넌트가 초기화되기 전에 제거할 수 있다.
        var holder = new GameObject("SpawnHolder");
        holder.SetActive(false);

        var instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity, holder.transform);
        StripNetworkComponents(instance);

        instance.transform.SetParent(null, true);
        UnityEngine.Object.Destroy(holder);

        var body = instance.GetComponent<Rigidbody2D>();
        if (body != null) body.bodyType = RigidbodyType2D.Kinematic;

        instance.SetActive(true);
        return instance;
    }

    public static void SetNameTag(GameObject character, string nickname, Color color)
    {
        foreach (var text in character.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!text.name.StartsWith("Nickname", StringComparison.OrdinalIgnoreCase)) continue;
            text.text = nickname;
            text.color = color;
        }
    }

    private static void StripNetworkComponents(GameObject root)
    {
        // RequireComponent 의존 관계 때문에 한 번에 지워지지 않을 수 있어 여러 번 반복한다.
        for (int pass = 0; pass < 4; pass++)
        {
            bool removedAny = false;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || !ShouldStrip(component)) continue;
                if (HasDependents(component)) continue;
                UnityEngine.Object.DestroyImmediate(component);
                removedAny = true;
            }
            if (!removedAny) break;
        }
    }

    private static bool ShouldStrip(Component component)
    {
        var type = component.GetType();
        string assembly = type.Assembly.GetName().Name;
        if (assembly.StartsWith("Fusion") || assembly.StartsWith("Unity.Netcode")) return true;

        // 게임 로직 스크립트는 Fusion 러너를 전제로 하므로 외형용(SPUM) 스크립트만 남긴다.
        if (component is MonoBehaviour && assembly == "Assembly-CSharp")
            return !type.Name.StartsWith("SPUM") && type.Name != "SpritePos";
        return false;
    }

    private static bool HasDependents(Component target)
    {
        foreach (var other in target.GetComponents<Component>())
        {
            if (other == null || other == target) continue;
            foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (IsRequired(require.m_Type0, target) || IsRequired(require.m_Type1, target) || IsRequired(require.m_Type2, target))
                    return true;
            }
        }
        return false;
    }

    private static bool IsRequired(Type requiredType, Component target)
    {
        return requiredType != null && requiredType.IsInstanceOfType(target);
    }
}
