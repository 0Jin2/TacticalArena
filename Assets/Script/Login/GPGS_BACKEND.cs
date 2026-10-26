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
    private bool isLoginRequested = false; // loginButton 클릭으로 시작된 로그인만 진행하기 위한 플래그
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
            // GPGS V2 (플러그인 v11): PlayGamesClientConfiguration / InitializeInstance 는 제거됨.
            // 설정(Web Client ID 등)은 Window > Google Play Games > Setup 에서 처리.
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
        if (isLoginRequested) return; // 이미 로그인 진행 중
        isLoginRequested = true;
        loginButton.SetActive(false);
#if UNITY_EDITOR
        EditorGuestLogin();
#else
        if (PlayGamesPlatform.Instance.IsAuthenticated())
        {
            Debug.Log("이미 로그인된 사용자입니다.");
            HandleBackendLogin();
        }
        else
        {
            // 1차: 자동(silent) 로그인 시도
            PlayGamesPlatform.Instance.Authenticate(status =>
            {
                if (status == SignInStatus.Success)
                {
                    Debug.Log("GPGS 로그인 성공");
                    HandleBackendLogin();
                }
                else
                {
                    // 2차: 사용자에게 로그인 창을 띄우는 수동 로그인
                    PlayGamesPlatform.Instance.ManuallyAuthenticate(manualStatus =>
                    {
                        if (manualStatus == SignInStatus.Success)
                        {
                            Debug.Log("GPGS 로그인 성공");
                            HandleBackendLogin();
                        }
                        else
                        {
                            Debug.LogError($"GPGS 로그인 실패: 사용자 인증 실패 ({manualStatus})");
                            ResetLoginButton(); // 실패 시 버튼 재활성화
                        }
                    });
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
            ResetLoginButton();
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
            ResetLoginButton(); // 실패 시 버튼 재활성화
        }
    }

    private const string LoginSceneName = "LoginPage";
    private const string EditorGuestEtc = "Unity Editor 테스트";

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
            return GuestLoginWithRecovery();

        string id = $"editor_{editorId}";
        var bro = Backend.BMember.CustomLogin(id, id);
        return bro.IsSuccess() ? bro : Backend.BMember.CustomSignUp(id, id);
    }

    // 게스트 로그인 + 자동 복구
    // 뒤끝 콘솔에서 유저를 삭제하면 로컬에 저장된 게스트 정보만 남아
    // GuestLogin 이 401(bad customId)로 계속 실패한다.
    // 이 경우 로컬 게스트 정보를 지우고 다시 GuestLogin 하면 신규 가입이 진행된다.
    private static BackendReturnObject GuestLoginWithRecovery()
    {
        var bro = Backend.BMember.GuestLogin(EditorGuestEtc);

        if (bro.IsSuccess())
        {
            // 로그인은 됐지만 유저 정보 조회가 안 되면 서버에 없는 계정으로 판단
            if (Backend.BMember.GetUserInfo().IsSuccess())
                return bro;

            Debug.LogWarning("[에디터] 유저 정보 조회 실패 → 로컬 게스트 정보 삭제 후 재가입");
        }
        else if (IsStaleGuestError(bro))
        {
            Debug.LogWarning($"[에디터] 서버에 없는 게스트 계정({bro.GetErrorCode()}) → 로컬 게스트 정보 삭제 후 재가입");
        }
        else
        {
            // 차단/네트워크 오류 등은 로컬 계정을 지우지 않고 그대로 반환
            return bro;
        }

        Backend.BMember.DeleteGuestInfo();
        return Backend.BMember.GuestLogin(EditorGuestEtc);
    }

    // 서버에서 삭제됐거나 탈퇴 처리된 게스트 계정인지 판별
    private static bool IsStaleGuestError(BackendReturnObject bro)
    {
        string code = bro.GetErrorCode();
        return code == "BadUnauthorizedException" || code == "GoneResourceException";
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

    // GPGS V2: 인증 코드 → 뒤끝 액세스 토큰 → 페더레이션 로그인
    private void HandleBackendLogin()
    {
        if (!isLoginRequested) return; // 버튼을 누르지 않았다면 뒤끝 로그인 진행 안 함

        PlayGamesPlatform.Instance.RequestServerSideAccess(false, authCode =>
        {
            if (string.IsNullOrEmpty(authCode))
            {
                Debug.LogError("GPGS 인증 코드(Server Auth Code)를 가져오지 못했습니다. (Web Client ID 설정 확인)");
                ResetLoginButton();
                return;
            }

            BackendReturnObject tokenBro = Backend.BMember.GetGPGS2AccessToken(authCode);
            if (!tokenBro.IsSuccess())
            {
                Debug.LogError($"GPGS2 액세스 토큰 발급 실패: {tokenBro.GetErrorCode()} - {tokenBro.GetMessage()}");
                ResetLoginButton(); // 실패 시 버튼 재활성화
                return;
            }

            string accessToken = tokenBro.GetReturnValuetoJSON()["access_token"].ToString();

            BackendReturnObject BRO = Backend.BMember.AuthorizeFederation(accessToken, FederationType.GPGS2, "gpgs");
            if (BRO.IsSuccess())
            {
                Debug.Log("뒤끝 로그인 성공");
                OnBackendLoginSuccess();
            }
            else
            {
                Debug.LogError($"뒤끝 로그인 실패: {BRO.GetErrorCode()} - {BRO.GetMessage()}");
                ResetLoginButton(); // 실패 시 버튼 재활성화
            }
        });
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

    // 로그인 실패 시: 플래그 초기화 + 로그인 버튼 재활성화
    private void ResetLoginButton()
    {
        isLoginRequested = false;
        loginButton.SetActive(true);
    }

    private void ActivateTapToPlayUI()
    {
        tapToPlayUI.SetActive(true); // "Tap to Play" UI 활성화
    }
}