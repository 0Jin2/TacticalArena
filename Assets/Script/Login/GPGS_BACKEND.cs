using UnityEngine;
using UnityEngine.UI;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using BackEnd;
using GoogleMobileAds.Api;
using TMPro;
using System.Text.RegularExpressions;
using UnityEngine.Purchasing;

public class GPGS_BACKEND : MonoBehaviour
{
    public GameObject loginButton;       // GPGS 로그인 버튼
    public GameObject nicknameUI;       // 닉네임 설정 UI
    public TMP_InputField nicknameInput;    // 닉네임 입력 필드
    public Button setNicknameButton;    // 닉네임 설정 버튼
    public GameObject tapToPlayUI;      // Tap to Play UI

    [SerializeField] private GameObject infoPrefab;  // infoPrefab 연결 (추가)
    [SerializeField] private Transform popupParent;  // infoPrefab을 붙일 부모(예: Canvas)

    private bool isBackendInitialized = false;
    public static string MyNickname { get; private set; }

    void Awake()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            Debug.LogError("인터넷 연결 상태를 확인하세요.");
            return;
        }

        Application.runInBackground = true;
        DontDestroyOnLoad(this.gameObject); // 이동 간 유지
    }

    void Start()
    {
        // Google Mobile Ads 초기화
        MobileAds.Initialize((InitializationStatus initStatus) =>
        {
            Debug.Log("구글 애드몹 초기화 완료");
        });

        var bro = Backend.Initialize();
        if (bro.IsSuccess())
        {
            isBackendInitialized = true;
            Debug.Log("뒤끝 초기화 성공");

#if !UNITY_EDITOR
            // 에디터에서는 GPGS를 쓰지 않음 (EditorGuestLogin 참고)
            PlayGamesClientConfiguration config = new PlayGamesClientConfiguration
                .Builder()
                .RequestServerAuthCode(false)
                .RequestEmail()
                .RequestIdToken()
                .Build();
            PlayGamesPlatform.InitializeInstance(config);
            PlayGamesPlatform.DebugLogEnabled = false;
            PlayGamesPlatform.Activate();
#endif
        }
        else
        {
            Debug.LogError($"뒤끝 초기화 실패: {bro.GetErrorCode()} - {bro.GetMessage()}");
        }
    }

    public void GPGSLogin()
    {
        loginButton.SetActive(false);
#if UNITY_EDITOR
        EditorGuestLogin();
#else
        if (Social.localUser.authenticated)
        {
            Debug.Log("이미 로그인된 사용자입니다.");
            HandleBackendLogin();
        }
        else
        {
            Social.localUser.Authenticate(success =>
            {
                if (success)
                {
                    Debug.Log("GPGS 로그인 성공");
                    HandleBackendLogin();
                }
                else
                {
                    Debug.LogError("GPGS 로그인 실패: 사용자 인증 실패");
                    loginButton.SetActive(true); // 실패 시 버튼 재활성화
                }
            });
        }
#endif
    }

#if UNITY_EDITOR
    // 에디터 테스트용: 구글 로그인 없이 뒤끝 게스트 계정으로 로그인
    private void EditorGuestLogin()
    {
        if (!isBackendInitialized)
        {
            Debug.LogError("[에디터] 뒤끝이 초기화되지 않아 로그인할 수 없습니다.");
            loginButton.SetActive(true);
            return;
        }

        BackendReturnObject BRO = EditorLogin();
        if (BRO.IsSuccess())
        {
            Debug.Log("[에디터] 뒤끝 게스트 로그인 성공");
            OnBackendLoginSuccess();
        }
        else
        {
            Debug.LogError($"[에디터] 뒤끝 게스트 로그인 실패: {BRO.GetErrorCode()} - {BRO.GetMessage()}");
            loginButton.SetActive(true); // 실패 시 버튼 재활성화
        }
    }

    private const string LoginSceneName = "LoginPage";

    // MPPM 추가 에디터면 "mppm978fa806" 같은 인스턴스 폴더 이름, 메인 에디터면 null
    public static string AdditionalEditorId
    {
        get
        {
            const string marker = "/Library/VP/";
            string path = Application.dataPath.Replace('\\', '/');
            int index = path.IndexOf(marker);
            if (index < 0) return null;

            string rest = path.Substring(index + marker.Length);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : rest;
        }
    }

    // 메인 에디터는 게스트 계정, MPPM 추가 에디터는 창마다 별도 테스트 계정
    // (같은 게스트 계정으로 여러 창이 로그인하면 먼저 로그인한 창의 토큰이 끊김)
    private static BackendReturnObject EditorLogin()
    {
        string editorId = AdditionalEditorId;
        if (editorId == null)
            return Backend.BMember.GuestLogin("Unity Editor 테스트");

        string id = $"editor_{editorId}";
        var bro = Backend.BMember.CustomLogin(id, id);
        return bro.IsSuccess() ? bro : Backend.BMember.CustomSignUp(id, id);
    }

    // 테스트 모드에서 게임 내 표시 닉네임 지정 (뒤끝 닉네임은 변경하지 않음)
    public static void SetTestNickname(string nickname) => MyNickname = nickname;

    // 에디터에서 로그인 씬을 거치지 않고 바로 Play할 때 뒤끝 초기화 + 게스트 로그인을 자동 처리
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EditorAutoLogin()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == LoginSceneName) return;

        var initBro = Backend.Initialize();
        if (!initBro.IsSuccess())
        {
            Debug.LogError($"[에디터] 뒤끝 초기화 실패: {initBro.GetErrorCode()} - {initBro.GetMessage()}");
            return;
        }

        var loginBro = EditorLogin();
        if (!loginBro.IsSuccess())
        {
            Debug.LogError($"[에디터] 뒤끝 게스트 로그인 실패: {loginBro.GetErrorCode()} - {loginBro.GetMessage()}");
            return;
        }

        string nickname = Backend.BMember.GetUserInfo().GetReturnValuetoJSON()["row"]["nickname"]?.ToString();
        if (string.IsNullOrEmpty(nickname))
        {
            // 게스트 계정에 닉네임이 없으면 테스트용 닉네임 자동 생성
            nickname = "Test" + Random.Range(1000, 10000);
            var nickBro = Backend.BMember.UpdateNickname(nickname);
            if (!nickBro.IsSuccess())
            {
                Debug.LogWarning($"[에디터] 테스트 닉네임 설정 실패: {nickBro.GetErrorCode()} - {nickBro.GetMessage()}");
                nickname = null;
            }
        }

        MyNickname = nickname;
        Debug.Log($"[에디터] 자동 게스트 로그인 완료 (닉네임: {nickname})");
    }
#endif

    private void HandleBackendLogin()
    {
        BackendReturnObject BRO = Backend.BMember.AuthorizeFederation(GetTokens(), FederationType.Google, "gpgs");
        if (BRO.IsSuccess())
        {
            Debug.Log("뒤끝 로그인 성공");
            OnBackendLoginSuccess();
        }
        else
        {
            Debug.LogError($"뒤끝 로그인 실패: {BRO.GetErrorCode()} - {BRO.GetMessage()}");
            loginButton.SetActive(true); // 실패 시 버튼 재활성화
        }
    }

    private void OnBackendLoginSuccess()
    {
        string nickname = Backend.BMember.GetUserInfo().GetReturnValuetoJSON()["row"]["nickname"]?.ToString();

        if (string.IsNullOrEmpty(nickname))
        {
            ActivateNicknameUI(); // 닉네임 설정 UI 활성화
        }
        else
        {
            Debug.Log($"이미 설정된 닉네임: {nickname}");
            MyNickname = nickname;
            ActivateTapToPlayUI(); // "Tap to Play" UI 활성화
        }
    }

    private void ActivateNicknameUI()
    {
        nicknameUI.SetActive(true); // 닉네임 설정 UI 활성화
        setNicknameButton.onClick.AddListener(SetNickname);
    }

    private void SetNickname()
    {
        string newNickname = nicknameInput.text.Trim();

        // 닉네임 조건: 한글/영문만, 2~8자, 띄어쓰기X, 기호X
        if (!IsValidNickname(newNickname))
        {
            ShowInfoMessage("닉네임은 한글/영문 2~8자, 띄어쓰기/기호 없이 입력하세요.");
            return;
        }

        BackendReturnObject BRO = Backend.BMember.UpdateNickname(newNickname);
        if (BRO.IsSuccess())
        {
            Debug.Log($"닉네임 설정 성공: {newNickname}");
            MyNickname = newNickname;
            nicknameUI.SetActive(false); // 닉네임 설정 UI 비활성화
            ActivateTapToPlayUI();
            ShowInfoMessage("닉네임 생성 완료");
        }
        else
        {
            Debug.LogError($"닉네임 설정 실패: {BRO.GetErrorCode()} - {BRO.GetMessage()}");
            ShowInfoMessage("닉네임 설정 실패: " + BRO.GetMessage());
        }
    }

    private bool IsValidNickname(string nickname)
    {
        // 2~8자, 한글/영문/숫자만 허용, 띄어쓰기/기호 불가
        if (string.IsNullOrEmpty(nickname)) return false;
        if (nickname.Length < 2 || nickname.Length > 8) return false;
        // 한글, 영문, 숫자만 허용 (띄어쓰기/특수문자 불가)
        Regex regex = new Regex(@"^[가-힣a-zA-Z0-9]{2,8}$");
        return regex.IsMatch(nickname);
    }

    private void ShowInfoMessage(string message)
    {
        if (infoPrefab != null && popupParent != null)
        {
            GameObject infoObj = Instantiate(infoPrefab, popupParent);
            ResultPopup resultPopup = infoObj.GetComponent<ResultPopup>();
            if (resultPopup != null)
                resultPopup.SetMessage(message);
        }
    }

    private void ActivateTapToPlayUI()
    {
        tapToPlayUI.SetActive(true); // "Tap to Play" UI 활성화
    }

    public string GetTokens()
    {
        if (PlayGamesPlatform.Instance.localUser.authenticated)
        {
            string token = PlayGamesPlatform.Instance.GetIdToken();
            Debug.Log("ID 토큰을 성공적으로 가져왔습니다.");
            return token;
        }
        else
        {
            Debug.LogError("토큰 가져오기 실패: 사용자가 인증되지 않았습니다.");
            return null;
        }
    }
}
