using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using BackEnd;
using GoogleMobileAds.Api;
using TMPro;
using System.Text.RegularExpressions;
using UnityEngine.Purchasing;
#if UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
#endif

// ─────────────────────────────────────────────────────────────────────────────
// GPGS_BACKEND : 로그인 화면 (GPGS v2 + 뒤끝 GPGS2 연동 로그인)
//
//  [첫 실행]   로그인 방식 선택 화면만 보여 준다. (구글 배너 없음)
//              → 유저가 구글 버튼을 누르면 그때 Play 게임즈 시작 → 환영 배너 → 뒤끝 로그인 (처음이면 자동 회원가입)
//              → 성공한 로그인 방식을 기기에 저장
//  [다음 실행]  저장된 방식이 구글이면 앱 시작과 동시에 Play 게임즈 시작 → 환영 배너 → 자동 로그인 → Tap to Start
//  [로그아웃]   Logout() 호출 → 저장된 방식 삭제 → 다음 실행부터 다시 로그인 방식 선택 화면
//  [데이터 삭제/재설치] 저장된 방식이 사라짐 (백업에서 제외되는 곳에 저장) → 다시 로그인 방식 선택 화면
//  [에디터]    로그인 버튼 → 뒤끝 게스트 로그인 (Play 게임즈는 에디터에서 동작하지 않음)
//
//  함께 동작하는 안드로이드 파일 (Assets/Plugins/Android)
//   - AndroidManifest.xml          : Play 게임즈 원래 자동 시작 부품(PlayGamesInitProvider) 제거 + 아래 부품 등록
//   - TaaLoginStore.java           : 마지막 로그인 방식 저장 (백업 제외 폴더)
//   - TaaPlayGamesInitProvider.java: 지난번 구글 로그인 기록이 있을 때만 앱 시작과 동시에 Play 게임즈 시작
//   - TaaResumeKickActivity.java   : 버튼으로 Play 게임즈를 늦게 시작했을 때 게임 화면을 인식시키는 투명 화면
//  나중에 애플/게스트 로그인을 추가할 때: 로그인 방식 이름 상수 + 버튼 함수 + Start() 의 자동 로그인 분기를 추가한다.
// ─────────────────────────────────────────────────────────────────────────────

public class GPGS_BACKEND : MonoBehaviour
{
    public GameObject loginButton;       // 로그인 방식 선택 UI (지금은 구글 로그인 버튼)
    public GameObject nicknameUI;       // 닉네임 설정 UI
    public TMP_InputField nicknameInput;    // 닉네임 입력 필드
    public Button setNicknameButton;    // 닉네임 설정 버튼
    public GameObject tapToPlayUI;      // Tap to Play UI

    [SerializeField] private GameObject infoPrefab;  // infoPrefab 연결 (추가)
    [SerializeField] private Transform popupParent;  // infoPrefab을 붙일 부모(예: Canvas)

    // 마지막으로 로그인에 성공한 방식 (다음 실행부터 이 방식으로 자동 로그인). 비어 있으면 첫 실행 → 로그인 방식 선택
    private const string LoginTypeGoogle = "GPGS2";   // 구글(Play 게임즈) 로그인 (TaaPlayGamesInitProvider.java 의 값과 같아야 함)

    // 안드로이드 쪽 도우미 (Assets/Plugins/Android/*.java)
    private const string LoginStoreClass = "com.loopstudio.taa.login.TaaLoginStore";
    private const string ResumeKickClass = "com.loopstudio.taa.login.TaaResumeKickActivity";
    private const float ResumeKickWaitSeconds = 0.5f;  // 투명 화면을 열었다 닫은 뒤 기다리는 시간(초)

    private bool playGamesStartedAtLaunch = false;  // 지난번 구글 로그인 기록이 있어서 앱 시작과 동시에 Play 게임즈가 켜졌는가
    private bool playGamesKicked = false;           // 늦게 시작한 Play 게임즈에 게임 화면을 인식시켰는가

    private bool isBackendInitialized = false;
    private bool isLoginRequested = false; // 로그인 진행 중 플래그 (버튼 클릭 또는 자동 로그인)
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
        MobileAds.Initialize((InitializationStatus initStatus) =>
        {
            Debug.Log("구글 애드몹 초기화 완료");
        });

#if UNITY_EDITOR
        EnsureInitialized();
#else
        // 지난번에 로그인했던 방식이 있으면 그 방식으로 자동 로그인한다.
        // 없으면(첫 실행) 아무것도 하지 않고 로그인 방식 선택 화면을 그대로 둔다. → 구글 배너도 뜨지 않음
        string lastLoginType = LoadLastLoginType();

        // 구글이면 TaaPlayGamesInitProvider 가 앱 시작과 동시에 Play 게임즈를 이미 시작해 두었다.
        playGamesStartedAtLaunch = lastLoginType == LoginTypeGoogle;

        if (lastLoginType == LoginTypeGoogle)
        {
            Debug.Log("지난번 로그인 방식(구글)으로 자동 로그인");
            StartLogin(false);
        }
        // 나중에 애플/게스트 로그인을 추가하면 여기에 else if 로 이어 붙인다.
#endif
    }

    // 구글 로그인 버튼 (인스펙터 Button OnClick 에 연결)
    public void GPGSLogin()
    {
        StartLogin(true);
    }

    private bool EnsureInitialized()
    {
        if (!isBackendInitialized)
        {
            var bro = Backend.Initialize();
            if (!bro.IsSuccess())
            {
                Debug.LogError($"뒤끝 초기화 실패: {bro.GetErrorCode()} - {bro.GetMessage()}");
                return false;
            }
            isBackendInitialized = true;
            Debug.Log("뒤끝 초기화 성공");
        }

        return true;
    }

    /// <param name="manual">true = 버튼으로 시작(필요하면 Play 게임즈 로그인 창 띄움) / false = 저장된 방식으로 자동 로그인</param>
    private void StartLogin(bool manual)
    {
        if (isLoginRequested) return; // 이미 로그인 진행 중
        isLoginRequested = true;
        loginButton.SetActive(false);

        if (!EnsureInitialized())
        {
            ResetLoginButton();
            return;
        }

#if UNITY_EDITOR
        EditorGuestLogin();
#elif UNITY_ANDROID
        StartCoroutine(Co_PlayGamesLogin(manual));
#else
        Debug.LogError("이 플랫폼에서는 구글 로그인을 지원하지 않습니다.");
        ResetLoginButton();
#endif
    }

#if UNITY_ANDROID
    // 0) Play 게임즈 시작 + 로그인 확인
    private IEnumerator Co_PlayGamesLogin(bool manual)
    {
        // 앱 시작 때 켜지지 않았다면 Play 게임즈는 여기서 처음 시작된다. (PlayGamesPlatform.Instance 를 처음 쓰는 순간)
        var platform = PlayGamesPlatform.Instance;

        if (!playGamesStartedAtLaunch && !playGamesKicked)
        {
            // 늦게 시작한 Play 게임즈는 게임 화면을 바로 인식하지 못해서 환영 배너/로그인 창이 안 뜬다. (구글 쪽 알려진 문제)
            // 투명 화면을 한 번 열었다 닫아 "게임 화면이 다시 켜진 것"처럼 만들어 인식시킨다.
            playGamesKicked = true;
            KickActivityResume();
            yield return new WaitForSecondsRealtime(ResumeKickWaitSeconds);
        }

        // 먼저 조용히 로그인되는지 확인하고, 버튼으로 시작했는데 안 되면 로그인 창을 띄운다.
        platform.Authenticate(status => OnPlayGamesAutoSignIn(status, manual));
    }

    // 1-a) 조용한 로그인 결과 (이전에 이 게임에서 구글 로그인을 한 적이 있으면 창 없이 성공)
    private void OnPlayGamesAutoSignIn(SignInStatus status, bool openWindowIfNeeded)
    {
        if (status == SignInStatus.Success)
        {
            OnPlayGamesSignedIn();
            return;
        }

        if (openWindowIfNeeded)
        {
            // 버튼을 눌렀는데 아직 로그인 전 → Play 게임즈 로그인 창 띄우기
            PlayGamesPlatform.Instance.ManuallyAuthenticate(OnPlayGamesManualSignIn);
            return;
        }

        // 자동 로그인 실패 → 로그인 방식 선택 화면 다시 표시
        Debug.Log($"Play 게임즈 자동 로그인 안 됨: {status}");
        ResetLoginButton();
    }

    // 1-b) 로그인 창 결과
    private void OnPlayGamesManualSignIn(SignInStatus status)
    {
        if (status == SignInStatus.Success)
        {
            OnPlayGamesSignedIn();
            return;
        }

        // 로그인 창을 취소했거나 실패 → 로그인 방식 선택 화면 다시 표시
        Debug.Log($"Play 게임즈 로그인 안 됨: {status}");
        ResetLoginButton();
    }

    // 2) Play 게임즈 로그인 성공 → 뒤끝 서버에 넘길 "서버용 인증 코드" 받기
    private void OnPlayGamesSignedIn()
    {
        Debug.Log("Play 게임즈 로그인 성공");
        PlayGamesPlatform.Instance.RequestServerSideAccess(false, OnServerAuthCode);
    }

    // 3) 인증 코드 → 뒤끝 GPGS2 액세스 토큰 → 뒤끝 로그인
    private void OnServerAuthCode(string authCode)
    {
        if (string.IsNullOrEmpty(authCode))
        {
            Debug.LogError("구글 서버 인증 코드를 받지 못했습니다. (Play Console 의 '게임 서버' 사용자 인증 정보와 웹 클라이언트 ID 를 확인하세요)");
            ResetLoginButton();
            return;
        }

        BackendReturnObject tokenBro = Backend.BMember.GetGPGS2AccessToken(authCode);
        if (!tokenBro.IsSuccess())
        {
            Debug.LogError($"뒤끝 GPGS2 토큰 발급 실패: {tokenBro.GetErrorCode()} - {tokenBro.GetMessage()}");
            ResetLoginButton();
            return;
        }

        string accessToken = tokenBro.GetReturnValuetoJSON()["access_token"].ToString();
        HandleBackendLogin(accessToken);
    }

    private static AndroidJavaObject GetCurrentActivity()
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
    }

    private static void KickActivityResume()
    {
        try
        {
            using (var kick = new AndroidJavaClass(ResumeKickClass))
            using (var activity = GetCurrentActivity())
                kick.CallStatic("kick", activity);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"투명 화면 열기 실패: {e.Message}");
        }
    }
#endif

    // 마지막 로그인 방식 읽기 (기기에만 저장. 에디터는 항상 빈 값)
    private static string LoadLastLoginType()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var store = new AndroidJavaClass(LoginStoreClass))
            using (var activity = GetCurrentActivity())
                return store.CallStatic<string>("get", activity) ?? "";
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"로그인 방식 읽기 실패: {e.Message}");
            return "";
        }
#else
        return "";
#endif
    }

    // 마지막 로그인 방식 저장 (빈 문자열 = 삭제)
    private static void SaveLastLoginType(string loginType)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var store = new AndroidJavaClass(LoginStoreClass))
            using (var activity = GetCurrentActivity())
                store.CallStatic("set", activity, loginType ?? "");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"로그인 방식 저장 실패: {e.Message}");
        }
#endif
    }

    /// <summary>
    /// 로그아웃: 뒤끝 로그아웃 + 저장된 로그인 방식 삭제 → 다음 실행부터 로그인 방식 선택 화면부터 시작한다.
    /// 로비의 로그아웃 버튼에서 GPGS_BACKEND.Logout() 을 부른 뒤 로그인 씬으로 이동하면 된다.
    /// (Play 게임즈 v2 에는 로그아웃 기능이 없어서, 다시 구글 버튼을 누르면 같은 구글 계정으로 바로 로그인된다)
    /// </summary>
    public static void Logout()
    {
        BackendReturnObject bro = Backend.BMember.Logout();
        if (!bro.IsSuccess())
            Debug.LogWarning($"뒤끝 로그아웃 실패: {bro.GetErrorCode()} - {bro.GetMessage()}");

        SaveLastLoginType("");
        MyNickname = null;
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

    // GPGS2 액세스 토큰 → 뒤끝 페더레이션 로그인 (처음이면 자동 회원가입 → 뒤끝 콘솔 회원 목록에 표시)
    private void HandleBackendLogin(string gpgs2AccessToken)
    {
        if (!isLoginRequested) return; // 로그인 흐름이 시작되지 않았다면 뒤끝 로그인 진행 안 함

        BackendReturnObject BRO = Backend.BMember.AuthorizeFederation(gpgs2AccessToken, FederationType.GPGS2, "gpgs2");
        if (BRO.IsSuccess())
        {
            Debug.Log("뒤끝 로그인 성공");

            // 로그인 방식 저장 → 다음 실행부터 앱 시작과 동시에 구글로 자동 로그인
            SaveLastLoginType(LoginTypeGoogle);

            OnBackendLoginSuccess();
        }
        else
        {
            Debug.LogError($"뒤끝 로그인 실패: {BRO.GetErrorCode()} - {BRO.GetMessage()}");
            ResetLoginButton(); // 실패 시 버튼 재활성화
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
        setNicknameButton.onClick.RemoveListener(SetNickname); // 중복 등록 방지
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