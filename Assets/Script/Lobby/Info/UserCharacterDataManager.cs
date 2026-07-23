using System.Collections.Generic;
using UnityEngine;
using BackEnd;
using LitJson;

// 싱글톤으로 접근
public class UserCharacterDataManager : MonoBehaviour
{
    public static UserCharacterDataManager Instance { get; private set; }
    private const string CHARACTER_TABLE = "Character";
    // ↓ 각 캐릭터 데이터 (key : bool)
    private Dictionary<string, bool> characterUnlockDict = new Dictionary<string, bool>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadOrInitCharacterData();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // ★ 최초 유저 데이터 없으면 생성, 있으면 로딩
    public void LoadOrInitCharacterData()
    {
        Backend.GameData.GetMyData(CHARACTER_TABLE, new Where(), 1, result =>
        {
            if (result.IsSuccess())
            {
                var rows = result.FlattenRows();
                if (rows.Count > 0)
                {
                    var dict = rows[0];
                    // 캐릭터 기본 7명만 체크
                    string[] keys = { "Rubell", "ChulWoo", "Jairo", "Leon", "JinHyuk", "Mirei", "SeoHee" };
                    foreach (var k in keys)
                    {
                        if (dict.ContainsKey(k))
                        {
                            characterUnlockDict[k] = bool.Parse(dict[k].ToString());
                        }
                        else
                        {
                            characterUnlockDict[k] = GetDefaultUnlockValue(k);
                        }
                    }
                }
                else
                {
                    InsertDefaultData();
                }
            }
            else
            {
                Debug.LogError($"캐릭터 데이터 로드 실패: {result.GetErrorCode()} - {result.GetMessage()}");
                InsertDefaultData();
            }
        });
    }

    // ★ 최초 생성
    private void InsertDefaultData()
    {
        Param param = new Param();
        param.Add("Rubell", false);
        param.Add("ChulWoo", true);
        param.Add("Jairo", false);
        param.Add("Leon", false);
        param.Add("JinHyuk", true);
        param.Add("Mirei", false);
        param.Add("SeoHee", true);

        Backend.GameData.Insert(CHARACTER_TABLE, param, result =>
        {
            if (result.IsSuccess())
            {
                characterUnlockDict.Clear();
                characterUnlockDict.Add("Rubell", false);
                characterUnlockDict.Add("ChulWoo", true);
                characterUnlockDict.Add("Jairo", false);
                characterUnlockDict.Add("Leon", false);
                characterUnlockDict.Add("JinHyuk", true);
                characterUnlockDict.Add("Mirei", false);
                characterUnlockDict.Add("SeoHee", true);
            }
            else
            {
                Debug.LogError($"캐릭터 기본 데이터 생성 실패: {result.GetErrorCode()} - {result.GetMessage()}");
            }
        });
    }

    // ★ 외부에서 잠금 여부 조회
    public bool IsCharacterLocked(string key)
    {
        if (characterUnlockDict.TryGetValue(key, out var unlocked))
            return !unlocked; // true면 '해제됨', false면 '잠금'
        // 키 없으면 기본값 사용
        return !GetDefaultUnlockValue(key);
    }

    // ★ 기본값 정의
    private bool GetDefaultUnlockValue(string key)
    {
        if (key == "ChulWoo" || key == "JinHyuk" || key == "SeoHee")
            return true;
        return false;
    }

    // ★ 캐릭터 잠금 해제(구매 등)
    public void UnlockCharacter(string key)
    {
        if (!characterUnlockDict.ContainsKey(key))
            characterUnlockDict.Add(key, true);
        else
            characterUnlockDict[key] = true;

        Param param = new Param();
        param.Add(key, true);

        Backend.GameData.Update(CHARACTER_TABLE, new Where(), param, result =>
        {
            if (!result.IsSuccess())
            {
                Debug.LogError($"캐릭터 잠금 해제 실패: {result.GetErrorCode()} - {result.GetMessage()}");
            }
        });
    }
}
