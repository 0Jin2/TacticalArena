using UnityEngine;
using Cinemachine;
using System.Collections;
using Fusion;
using System.Linq;
using UnityEngine.SceneManagement;

public class CameraZoomController : MonoBehaviour
{
    public static CameraZoomController Instance;

    [Header("Cinemachine 설정")]
    [SerializeField] private CinemachineVirtualCamera virtualCamera;
    [SerializeField] private float zoomSpeed = 1.5f;
    [SerializeField] private float zoomTargetSize = 6f;

    [SerializeField] private CinemachineConfiner2D confiner;
    [SerializeField] private Collider2D confinerBounds;

    private float originalSize;
    private Coroutine zoomCoroutine;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (virtualCamera == null)
            virtualCamera = FindObjectOfType<CinemachineVirtualCamera>();

        if (confiner == null)
            confiner = virtualCamera.GetComponent<CinemachineConfiner2D>();

        if (confiner != null && confinerBounds != null)
        {
            confiner.m_BoundingShape2D = confinerBounds;

            confiner.enabled = false;
            confiner.enabled = true;
        }

        originalSize = virtualCamera.m_Lens.OrthographicSize;
    }

    public void ZoomInToTarget(Transform target)
    {
        virtualCamera.Follow = target;
        virtualCamera.LookAt = target;

        if (zoomCoroutine != null)
            StopCoroutine(zoomCoroutine);

        zoomCoroutine = StartCoroutine(ZoomCoroutine(zoomTargetSize));
    }

    public void ZoomOutToInitial()
    {
        if (zoomCoroutine != null)
            StopCoroutine(zoomCoroutine);

        zoomCoroutine = StartCoroutine(ZoomCoroutine(originalSize));
    }

    private IEnumerator ZoomCoroutine(float targetSize)
    {
        while (!Mathf.Approximately(virtualCamera.m_Lens.OrthographicSize, targetSize))
        {
            virtualCamera.m_Lens.OrthographicSize = Mathf.MoveTowards(
                virtualCamera.m_Lens.OrthographicSize,
                targetSize,
                zoomSpeed * Time.deltaTime
            );
            yield return null;
        }

        zoomCoroutine = null;

        if (Mathf.Approximately(targetSize, zoomTargetSize))
        {
            Debug.Log("✅ 줌인 완료, UI 표시");

            // 🔒 스킬 아이콘 재지정 안전 처리
            NetworkRunner runner = FindObjectOfType<NetworkRunner>();
            if (runner != null && runner.TryGetPlayerObject(runner.LocalPlayer, out var obj))
            {
                // 실행용 핸들러 (SkillHandler_XXX)
                var skillHandlerExec = obj.GetComponents<SkillHandler>()
                    .FirstOrDefault(h => h.GetType() != typeof(SkillHandler));

                // 아이콘용 핸들러 (SkillHandler - 베이스)
                var skillHandlerBase = obj.GetComponent<SkillHandler>();

                if (skillHandlerExec != null)
                {
                    // 아이콘은 base에서, 실행은 파생에서 가져옴
                    if (skillHandlerBase != null)
                        skillHandlerExec.skillIcon = skillHandlerBase.skillIcon;

                    GamePlayUIController.Instance.InitSkillButton(skillHandlerExec);
                }
            }

            MatchStatusUIManager.Instance?.CreateAllPlayerStatusItems();
            MyStatusUIManager.Instance?.CreateMyStatusUI();
            GamePlayUIController.Instance.ShowGameplayUI();
        }

        // ✅ 튜토리얼 데이터가 false라면 Tutorial UI 활성화
        if (SceneManager.GetActiveScene().name.Contains("Tutorial"))
        {
            if (UserInfoManager.Instance != null && !UserInfoManager.Instance.IsTutorialCompleted)
            {
                Debug.Log("🎓 튜토리얼 미완료 → Tutorial UI 활성화 시도");

                // ✅ 비활성 오브젝트 포함하여 탐색
                GameObject tutorialUI = null;

                // 1️⃣ 먼저 활성 상태에서 찾기
                tutorialUI = GameObject.Find("TutorialUI");

                // 2️⃣ 못 찾았으면 비활성 포함 전체 검색
                if (tutorialUI == null)
                {
                    var all = Resources.FindObjectsOfTypeAll<GameObject>();
                    tutorialUI = all.FirstOrDefault(obj => obj.name == "TutorialUI");
                }

                // 3️⃣ 찾아서 활성화
                if (tutorialUI != null)
                {
                    tutorialUI.SetActive(true);
                    Debug.Log("✅ TutorialUI 활성화 완료!");
                }
                else
                {
                    Debug.LogWarning("⚠️ TutorialUI를 찾지 못했습니다. 이름 확인 필요!");
                }
            }
        }
    }
}