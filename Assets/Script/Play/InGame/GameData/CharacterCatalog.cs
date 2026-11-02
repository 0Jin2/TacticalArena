using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// CharacterCatalog : 게임에 존재하는 모든 CharacterDef 목록 (ID로 찾기)
//   에셋 1개만 만들어서 PlayerSpawner / CharSelectUI / 결과 UI가 함께 참조한다.
// ─────────────────────────────────────────────────────────────────────────────

[CreateAssetMenu(fileName = "CharacterCatalog", menuName = "TAA/Character Catalog")]
public class CharacterCatalog : ScriptableObject
{
    public List<CharacterDef> characters = new List<CharacterDef>();

    private Dictionary<string, CharacterDef> byId;

    public CharacterDef Get(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        if (byId == null) Rebuild();
        CharacterDef def;
        return byId.TryGetValue(characterId, out def) ? def : null;
    }

    public Sprite GetIcon(string characterId)
    {
        var def = Get(characterId);
        return def != null ? def.icon : null;
    }

    /// <summary>잠기지 않은 캐릭터 중 무작위 1개. isLocked가 null이면 전체에서 고른다.</summary>
    public string RandomId(System.Func<string, bool> isLocked = null)
    {
        var pool = new List<string>();
        foreach (var def in characters)
        {
            if (def == null || string.IsNullOrEmpty(def.characterId)) continue;
            if (isLocked != null && isLocked(def.characterId)) continue;
            pool.Add(def.characterId);
        }
        return pool.Count == 0 ? null : pool[Random.Range(0, pool.Count)];
    }

    private void Rebuild()
    {
        byId = new Dictionary<string, CharacterDef>();
        foreach (var def in characters)
        {
            if (def == null || string.IsNullOrEmpty(def.characterId)) continue;
            byId[def.characterId] = def;
        }
    }

    private void OnValidate() { byId = null; }
}
