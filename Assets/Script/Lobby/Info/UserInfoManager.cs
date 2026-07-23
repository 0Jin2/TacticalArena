using UnityEngine;
using TMPro;
using BackEnd;
using LitJson;
using System;
using UnityEngine.SceneManagement;

public class UserInfoManager : MonoBehaviour
{
    public static UserInfoManager Instance { get; private set; }

    public TMP_Text nicknameText;
    public TMP_Text levelText;
    public TMP_Text goldText;
    public TMP_Text jemText;

    private const string USER_CURRENCY_TABLE = "UserCurrency";
    private const string USER_INFO_TABLE = "UserInfo";

    public Action<int, int> onCurrencyChanged;

    private int gold;
    private int jem;

    public int GetGold() { return gold; }
    public int GetJem() { return jem; }

    public bool IsTutorialCompleted { get; private set; } = false;

    private const string USER_TUTORIAL_KEY = "Tutorial";
    private bool tutorialInitialized = false;

    public Action OnTutorialDataLoaded;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // ✅ 매 씬 로드마다 튜토리얼 상태는 항상 최신값으로 강제 갱신
        LoadOrInitializeTutorial(true);

        // ✅ 로비 씬에서는 기존처럼 UI 참조 재설정 + 전체 유저 데이터 갱신
        if (scene.name == "LobbyPage")
        {
            Debug.Log("🏠 로비 씬 재진입 → 유저 정보 새로고침");

            ReassignUIReferencesIfNeeded();
            LoadUserData();
        }
    }

    //private void Start()
    //{
    //    ReassignUIReferencesIfNeeded();
    //    LoadUserData();
    //}

    private void ReassignUIReferencesIfNeeded()
    {
        if (nicknameText == null)
            nicknameText = GameObject.Find("NicknameText")?.GetComponent<TMP_Text>();

        if (levelText == null)
            levelText = GameObject.Find("LevelText")?.GetComponent<TMP_Text>();

        if (goldText == null)
            goldText = GameObject.Find("GoldText")?.GetComponent<TMP_Text>();

        if (jemText == null)
            jemText = GameObject.Find("JemText")?.GetComponent<TMP_Text>();
    }

    public void LoadUserData()
    {
        LoadOrInitializeNicknameAndLevel();
        LoadOrInitializeCurrency();
        LoadOrInitializeTutorial(true);
    }

    private void LoadOrInitializeTutorial(bool forceReload)
    {
        if (!forceReload && tutorialInitialized) return;
        tutorialInitialized = true;

        var result = Backend.GameData.GetMyData(USER_INFO_TABLE, new Where(), 1);
        if (result.IsSuccess())
        {
            var rows = result.FlattenRows();
            if (rows.Count > 0)
            {
                if (rows[0].ContainsKey(USER_TUTORIAL_KEY))
                {
                    string raw = rows[0][USER_TUTORIAL_KEY].ToString();

                    bool parsed;
                    if (!bool.TryParse(raw, out parsed))
                    {
                        // 혹시 "1"/"0" 등으로 들어오는 경우까지 방어
                        parsed = (raw == "1");
                    }

                    IsTutorialCompleted = parsed;
                }
                else
                {
                    Param p = new Param();
                    p.Add(USER_TUTORIAL_KEY, false);
                    Backend.GameData.Update(USER_INFO_TABLE, new Where(), p);
                    IsTutorialCompleted = false;
                }
            }
            else
            {
                Param p = new Param();
                p.Add("level", 1);
                p.Add(USER_TUTORIAL_KEY, false);

                var insertResult = Backend.GameData.Insert(USER_INFO_TABLE, p);
                if (!insertResult.IsSuccess())
                    Debug.LogError($"❌ Tutorial/Level 초기 데이터 생성 실패: {insertResult.GetErrorCode()} / {insertResult.GetMessage()}");

                IsTutorialCompleted = false;
            }
        }
        else
        {
            Debug.LogError($"❌ Tutorial 데이터 로드 실패: {result.GetErrorCode()} / {result.GetMessage()}");
            IsTutorialCompleted = false;
        }

        OnTutorialDataLoaded?.Invoke();
    }

    public void SetTutorialCompleted()
    {
        Param p = new Param();
        p.Add(USER_TUTORIAL_KEY, true);
        Backend.GameData.Update(USER_INFO_TABLE, new Where(), p);

        IsTutorialCompleted = true;

        // ✅ 완료 처리 후에도 UI들이 즉시 갱신되도록 이벤트 호출
        OnTutorialDataLoaded?.Invoke();
    }

    private void LoadOrInitializeNicknameAndLevel()
    {
        var userInfo = Backend.BMember.GetUserInfo().GetReturnValuetoJSON();
        string nickname = userInfo["row"]["nickname"].ToString();
        nicknameText.text = !string.IsNullOrEmpty(nickname) ? nickname : "닉네임 없음";

        Backend.GameData.GetMyData(USER_INFO_TABLE, new Where(), 1, result =>
        {
            if (result.IsSuccess())
            {
                var rows = result.FlattenRows();
                if (rows.Count > 0 && rows[0].ContainsKey("level"))
                {
                    int level = int.Parse(rows[0]["level"].ToString());
                    levelText.text = level.ToString();
                }
                else
                {
                    Debug.LogWarning("레벨 데이터가 없어 초기화합니다.");
                    InitializeLevel();
                }
            }
            else
            {
                Debug.LogError($"레벨 데이터 로드 실패: {result.GetErrorCode()} - {result.GetMessage()}");
            }
        });
    }

    private void InitializeLevel()
    {
        Param param = new Param();
        param.Add("level", 1);

        Backend.GameData.Update(USER_INFO_TABLE, new Where(), param);
        levelText.text = "1";
    }

    private void LoadOrInitializeCurrency()
    {
        Backend.GameData.GetMyData(USER_CURRENCY_TABLE, new Where(), 1, result =>
        {
            if (result.IsSuccess())
            {
                var rows = result.FlattenRows();
                if (rows.Count > 0)
                {
                    gold = rows[0].ContainsKey("gold") ? int.Parse(rows[0]["gold"].ToString()) : InitializeCurrency("gold", 1000);
                    jem = rows[0].ContainsKey("jem") ? int.Parse(rows[0]["jem"].ToString()) : InitializeCurrency("jem", 0);
                    UpdateCurrencyUI(gold, jem);
                    onCurrencyChanged?.Invoke(gold, jem);
                }
                else
                {
                    InitializeCurrency();
                    UpdateCurrencyUI(1000, 0);
                    onCurrencyChanged?.Invoke(1000, 0);
                }
            }
            else
            {
                InitializeCurrency();
                UpdateCurrencyUI(1000, 0);
                onCurrencyChanged?.Invoke(1000, 0);
            }
        });
    }

    private int InitializeCurrency(string key = null, int value = 0)
    {
        Param param = new Param();

        if (key != null)
        {
            param.Add(key, value);
        }
        else
        {
            param.Add("gold", 1000);
            param.Add("jem", 0);
        }

        // 중복 삽입 방지를 위해 Get → 없으면 Insert
        Backend.GameData.GetMyData(USER_CURRENCY_TABLE, new Where(), 1, result =>
        {
            if (result.IsSuccess())
            {
                var rows = result.FlattenRows();
                if (rows.Count > 0)
                {
                    // 이미 있으므로 업데이트
                    Backend.GameData.Update(USER_CURRENCY_TABLE, new Where(), param);
                }
                else
                {
                    Backend.GameData.Insert(USER_CURRENCY_TABLE, param);
                }
            }
            else
            {
                Backend.GameData.Insert(USER_CURRENCY_TABLE, param);
            }
        });

        if (key == "gold") return value;
        if (key == "jem") return value;
        return 0;
    }

    private void UpdateCurrencyUI(int gold, int jem)
    {
        if (goldText != null && goldText.text != gold.ToString())
            goldText.text = gold.ToString();

        if (jemText != null && jemText.text != jem.ToString())
            jemText.text = jem.ToString();
    }

    // ★ 재화 소비 및 서버 동기화, 완료시 콜백
    public void ConsumeCurrency(int goldToSpend, int jemToSpend, Action onSuccess)
    {
        // 충분한지 검사
        if (gold < goldToSpend || jem < jemToSpend)
        {
            Debug.LogWarning("재화 부족, 구매 불가!");
            return;
        }

        int newGold = gold - goldToSpend;
        int newJem = jem - jemToSpend;

        Param param = new Param();
        param.Add("gold", newGold);
        param.Add("jem", newJem);

        Backend.GameData.Update(USER_CURRENCY_TABLE, new Where(), param, result =>
        {
            if (result.IsSuccess())
            {
                gold = newGold;
                jem = newJem;
                UpdateCurrencyUI(gold, jem);
                onCurrencyChanged?.Invoke(gold, jem);
                onSuccess?.Invoke();
            }
            else
            {
                Debug.LogError("재화 차감 실패: " + result.GetErrorCode() + " / " + result.GetMessage());
            }
        });
    }

    public void AddGold(int amount, Action onSuccess = null)
    {
        Backend.GameData.GetMyData(USER_CURRENCY_TABLE, new Where(), 1, result =>
        {
            if (result.IsSuccess())
            {
                var rows = result.FlattenRows();
                int currentGold = 0, currentJem = 0;

                if (rows.Count > 0)
                {
                    int.TryParse(rows[0]["gold"].ToString(), out currentGold);
                    int.TryParse(rows[0]["jem"].ToString(), out currentJem);
                }

                gold = currentGold + amount;
                jem = currentJem;

                Param param = new Param();
                param.Add("gold", gold);
                param.Add("jem", jem);

                Backend.GameData.Update(USER_CURRENCY_TABLE, new Where(), param, updateResult =>
                {
                    if (updateResult.IsSuccess())
                    {
                        UpdateCurrencyUI(gold, jem);
                        onCurrencyChanged?.Invoke(gold, jem);
                        onSuccess?.Invoke();
                    }
                    else
                    {
                        Debug.LogError("골드 업데이트 실패: " + updateResult.GetErrorCode());
                    }
                });
            }
        });
    }

    // 잼 추가
    public void AddJem(int amount, Action onSuccess = null)
    {
        Backend.GameData.GetMyData(USER_CURRENCY_TABLE, new Where(), 1, result =>
        {
            if (result.IsSuccess())
            {
                var rows = result.FlattenRows();
                int currentGold = 0, currentJem = 0;

                if (rows.Count > 0)
                {
                    int.TryParse(rows[0]["gold"].ToString(), out currentGold);
                    int.TryParse(rows[0]["jem"].ToString(), out currentJem);
                }

                gold = currentGold;
                jem = currentJem + amount;

                Param param = new Param();
                param.Add("gold", gold);
                param.Add("jem", jem);

                Backend.GameData.Update(USER_CURRENCY_TABLE, new Where(), param, updateResult =>
                {
                    if (updateResult.IsSuccess())
                    {
                        UpdateCurrencyUI(gold, jem);
                        onCurrencyChanged?.Invoke(gold, jem);
                        onSuccess?.Invoke();
                    }
                    else
                    {
                        Debug.LogError("잼 업데이트 실패: " + updateResult.GetErrorCode());
                    }
                });
            }
        });
    }
}