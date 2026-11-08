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
    [Tooltip("줌인이 끝났을 때의 렌즈(Orthographic Size) 값. 작을수록 캐릭터가 크게 보입니다.")]
    [SerializeField] private float zoomTargetSize = 0.5f;

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
    private Transform currentTarget;          // 지금 따라가야 하는 캐릭터

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
        EnsureBodyComponent();
    }

    /// <summary>
    /// Cinemachine 은 Tracking Target 만 지정해서는 카메라가 움직이지 않는다.
    /// "Position Control(Body)" 역할의 컴포넌트(Position Composer 등)가 있어야 대상을 따라간다.
    /// 인스펙터에서 빠뜨렸다면 여기서 자동으로 붙여 준다.
    /// </summary>
    private void EnsureBodyComponent()
    {
        if (virtualCamera == null) return;
        if (virtualCamera.GetComponent<CinemachinePositionComposer>() != null) return;
        if (virtualCamera.GetComponent<CinemachineFollow>() != null) return;

        virtualCamera.gameObject.AddComponent<CinemachinePositionComposer>();
        Debug.LogWarning("[Camera] CM PlayerCamera 에 Position Composer 가 없어 자동으로 추가했습니다. " +
                         "인스펙터에서 Add Component > Cinemachine Position Composer 로 직접 붙여 두면 값을 조절할 수 있습니다.");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>천천히 줌인하면서 대상을 따라간다. (게임 시작 연출)</summary>
    public void ZoomInToTarget(Transform target)
    {
        if (virtualCamera == null) { NetLog.Error("CameraZoomController: CinemachineCamera 가 연결되지 않았습니다."); return; }
        if (target == null) { NetLog.Warn("CameraZoomController: 따라갈 캐릭터가 없습니다."); return; }

        ZoomInDone = false;
        JumpCameraTo(target);               // 카메라를 먼저 내 캐릭터 위치로 옮긴 뒤
        Follow(target);                     // 계속 따라가게 한다.

        if (zoomCoroutine != null) StopCoroutine(zoomCoroutine);
        zoomCoroutine = StartCoroutine(ZoomCoroutine(zoomTargetSize, true, target));
    }

    /// <summary>카메라를 대상 위치로 즉시 옮긴다. (부드러운 이동을 기다리지 않고 캐릭터가 화면 중앙에 오게)</summary>
    private void JumpCameraTo(Transform target)
    {
        var t = virtualCamera.transform;
        t.position = new Vector3(target.position.x, target.position.y, t.position.z);
        virtualCamera.PreviousStateIsValid = false;     // 이전 위치로부터의 감속 이동을 건너뛴다
    }

    /// <summary>연출 없이 바로 줌인 상태로 대상을 따라간다. (재접속 복구)</summary>
    public void SnapToTarget(Transform target)
    {
        if (virtualCamera == null) return;
        if (target != null) JumpCameraTo(target);
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
        zoomCoroutine = StartCoroutine(ZoomCoroutine(originalSize, false, null));
    }

    /// <summary>카메라가 따라갈 대상만 바꾼다. (크기 변화 없음)</summary>
    public void Follow(Transform target)
    {
        if (virtualCamera == null) return;
        currentTarget = target;
        virtualCamera.Follow = target;
        virtualCamera.LookAt = target;
        if (target != null) Debug.Log("[Camera] CameraZoomController: 카메라 Follow 대상 = " + target.name + " (카메라 오브젝트: " + virtualCamera.name + ")");
    }

    // 다른 곳에서 Follow 를 비우거나 바꿔 버려도 내 캐릭터로 되돌려 놓는다. (안전장치)
    private void LateUpdate()
    {
        if (virtualCamera == null || currentTarget == null) return;
        if (virtualCamera.Follow != currentTarget)
        {
            NetLog.Warn("CameraZoomController: 누군가 카메라 Follow 를 바꿔서 내 캐릭터로 되돌립니다. 현재=" + (virtualCamera.Follow != null ? virtualCamera.Follow.name : "(없음)"));
            virtualCamera.Follow = currentTarget;
            virtualCamera.LookAt = currentTarget;
        }
    }

    private IEnumerator ZoomCoroutine(float targetSize, bool notifyWhenDone, Transform target)
    {
        while (!Mathf.Approximately(virtualCamera.Lens.OrthographicSize, targetSize))
        {
            var lens = virtualCamera.Lens;
            lens.OrthographicSize = Mathf.MoveTowards(lens.OrthographicSize, targetSize, zoomSpeed * Time.deltaTime);
            virtualCamera.Lens = lens;
            yield return null;
        }

        // 렌즈가 다 줄어든 뒤에도 카메라가 캐릭터에 닿을 때까지 잠깐 기다린다. (최대 3초)
        if (target != null)
        {
            float wait = 3f;
            while (wait > 0f && target != null)
            {
                Vector2 delta = (Vector2)virtualCamera.transform.position - (Vector2)target.position;
                if (delta.sqrMagnitude < 0.01f) break;
                wait -= Time.deltaTime;
                yield return null;
            }
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
