using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
// CameraZoomController : 시네머신 카메라가 내 캐릭터를 따라가며 줌인  (기존 CameraZoomController 대체)
//
//  - PlayerSpawner 가 캐릭터를 만들면 ZoomInToTarget(내 캐릭터) 를 부른다. (재접속이면 SnapToTarget)
//  - 줌인이 끝나면 OnZoomInCompleted 이벤트 → GameHud 가 조작 UI/스킬 아이콘/상태 UI 를 켠다.
//    (기존에는 이 스크립트가 직접 UI 를 만들었음. 이제는 이벤트만 알리고 UI 쪽이 알아서 반응)
//  - 튜토리얼 씬에서 튜토리얼을 아직 안 끝냈다면 TutorialUI 를 켠다. (기존 동작)
// ─────────────────────────────────────────────────────────────────────────────

public class CameraZoomController : MonoBehaviour
{
    public static CameraZoomController Instance { get; private set; }

    [Header("Cinemachine 설정")]
    [SerializeField] private CinemachineCamera virtualCamera;
    [SerializeField] private float zoomSpeed = 1.5f;
    [SerializeField] private float zoomTargetSize = 6f;

    [Header("화면 경계 (선택)")]
    [SerializeField] private CinemachineConfiner2D confiner;
    [SerializeField] private Collider2D confinerBounds;

    [Header("튜토리얼")]
    [Tooltip("튜토리얼 미완료일 때 줌인 후 켜 줄 오브젝트. 비워 두면 이름이 TutorialUI 인 오브젝트를 찾습니다.")]
    [SerializeField] private GameObject tutorialUI;
    [SerializeField] private string tutorialSceneKeyword = "Tutorial";

    /// <summary>줌인이 끝났을 때 (내 캐릭터 기준으로 UI 를 켤 시점)</summary>
    public event Action OnZoomInCompleted;
    public bool ZoomInDone { get; private set; }

    private float originalSize;
    private Coroutine zoomCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (virtualCamera == null) virtualCamera = FindFirstObjectByType<CinemachineCamera>();
        if (virtualCamera == null) { NetLog.Error("CameraZoomController: CinemachineCamera 를 찾지 못했습니다."); return; }

        if (confiner == null) confiner = virtualCamera.GetComponent<CinemachineConfiner2D>();
        if (confiner != null && confinerBounds != null)
        {
            confiner.BoundingShape2D = confinerBounds;
            confiner.enabled = false;
            confiner.enabled = true;
        }

        originalSize = virtualCamera.Lens.OrthographicSize;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>천천히 줌인하면서 대상을 따라간다. (게임 시작 연출)</summary>
    public void ZoomInToTarget(Transform target)
    {
        if (virtualCamera == null) return;
        Follow(target);

        if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
        zoomCoroutine = StartCoroutine(ZoomCoroutine(zoomTargetSize, true));
    }

    /// <summary>연출 없이 바로 줌인 상태로 대상을 따라간다. (재접속 복구)</summary>
    public void SnapToTarget(Transform target)
    {
        if (virtualCamera == null) return;
        Follow(target);

        if (zoomCoroutine != null) { StopCoroutine(zoomCoroutine); zoomCoroutine = null; }
        var lens = virtualCamera.Lens;
        lens.OrthographicSize = zoomTargetSize;
        virtualCamera.Lens = lens;
        FinishZoomIn();
    }

    public void ZoomOutToInitial()
    {
        if (virtualCamera == null) return;
        if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
        zoomCoroutine = StartCoroutine(ZoomCoroutine(originalSize, false));
    }

    /// <summary>카메라가 따라갈 대상만 바꾼다. (크기 변화 없음)</summary>
    public void Follow(Transform target)
    {
        if (virtualCamera == null) return;
        virtualCamera.Follow = target;
        virtualCamera.LookAt = target;
    }

    private IEnumerator ZoomCoroutine(float targetSize, bool notifyWhenDone)
    {
        while (!Mathf.Approximately(virtualCamera.Lens.OrthographicSize, targetSize))
        {
            var lens = virtualCamera.Lens;
            lens.OrthographicSize = Mathf.MoveTowards(lens.OrthographicSize, targetSize, zoomSpeed * Time.deltaTime);
            virtualCamera.Lens = lens;
            yield return null;
        }

        zoomCoroutine = null;
        if (notifyWhenDone) FinishZoomIn();
    }

    private void FinishZoomIn()
    {
        ZoomInDone = true;
        if (OnZoomInCompleted != null) OnZoomInCompleted();
        TryActivateTutorialUi();
    }

    private void TryActivateTutorialUi()
    {
        if (!SceneManager.GetActiveScene().name.Contains(tutorialSceneKeyword)) return;
        if (UserInfoManager.Instance == null || UserInfoManager.Instance.IsTutorialCompleted) return;

        var target = tutorialUI;
        if (target == null)
        {
            // 비활성 오브젝트까지 포함해서 이름으로 찾기
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "TutorialUI" && all[i].scene.IsValid()) { target = all[i]; break; }
            }
        }

        if (target != null) target.SetActive(true);
        else NetLog.Warn("CameraZoomController: TutorialUI 를 찾지 못했습니다. 이름 확인 필요!");
    }
}
