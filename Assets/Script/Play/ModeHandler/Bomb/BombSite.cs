using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BombSite : NetworkBehaviour
{
    [Networked] public bool IsActive { get; private set; }
    [Networked] public bool IsPlantedHere { get; private set; }

    [Header("설치/해제 공용")]
    [SerializeField] private Canvas bombProgressCanvas;
    [SerializeField] private Image bombProgressFill;
    [SerializeField] private TMP_Text bombProgressLabel;
    public Transform worldAnchor;
    public GameObject bombVisual;          // 설치되면 활성화
    public float interactRange = 2.0f;     // 상호작용 가능 반경

    [Header("시각효과")]
    [Range(0f, 1f)] public float eligibleAlpha = 0.5f; // 설치 가능 시 반투명
    [Range(0f, 1f)] public float defaultAlpha = 1.0f;

    [Header("인덱스 (스포너가 지정)")]
    public int siteIndex = -1;

    private CanvasGroup _visualGroup;
    private SpriteRenderer[] _spriteRenderers;
    private bool _localEligible;     // 설치 가능 상태(로컬)
    private bool _localInstalling;   // 로컬에서 이미 설치 시도 중(중복 탭 방지)
    private Coroutine installCoroutine;

    [Networked(OnChanged = nameof(OnInstallProgressChanged))]
    private float installProgress { get; set; }

    [Networked(OnChanged = nameof(OnIsInstallingChanged))]
    private bool isInstalling { get; set; }

    private void Awake()
    {
        if (bombVisual != null)
        {
            _visualGroup = bombVisual.GetComponent<CanvasGroup>();
            if (_visualGroup == null) _visualGroup = bombVisual.AddComponent<CanvasGroup>();
            _visualGroup.alpha = defaultAlpha;

            // 🆕 하위 스프라이트들도 함께 제어
            _spriteRenderers = bombVisual.GetComponentsInChildren<SpriteRenderer>(true);
            if (_spriteRenderers != null)
            {
                foreach (var sr in _spriteRenderers)
                {
                    var c = sr.color;
                    c.a = defaultAlpha;
                    sr.color = c;
                }
            }
        }
    }

    public override void Spawned()
    {
        base.Spawned();
        ResetForRound();
    }

    private void SetVisualAlpha(float a)
    {
        if (_visualGroup != null) _visualGroup.alpha = a;

        // 🆕 스프라이트 렌더러에도 적용
        if (_spriteRenderers != null)
        {
            for (int i = 0; i < _spriteRenderers.Length; i++)
            {
                var sr = _spriteRenderers[i];
                if (sr == null) continue;
                var c = sr.color;
                c.a = a;
                sr.color = c;
            }
        }
    }

    private bool IsLocalDefuseTeam()
    {
        if (Runner == null) return false;
        var local = Runner.LocalPlayer;
        if (!local.IsValid) return false;
        if (TeamAssigner.Instance == null) return false;

        return TeamAssigner.Instance.GetTeam(local) == TeamAssigner.Instance.GetDefuseTeam();
    }

    /// <summary>라운드 시작 시 사이트 활성/초기화</summary>
    public void ActivateForRound()
    {
        IsActive = true;
        IsPlantedHere = false;
        if (bombVisual != null) bombVisual.SetActive(false);
    }

    /// <summary>라운드 종료 또는 시작 전 초기화</summary>
    public void ResetForRound()
    {
        IsActive = false;
        IsPlantedHere = false;
        if (bombVisual != null) bombVisual.SetActive(false);
    }

    /// <summary>서버가 설치 확정 시 호출(시각효과 포함)</summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_OnPlanted()
    {
        IsPlantedHere = true;
        if (bombVisual != null) bombVisual.SetActive(true);
    }

    /// <summary>로컬 입력: 설치 시도(설치팀만)</summary>
    public void TryInteractPlant(PlayerRef requester, Transform requesterTf)
    {
        if (!IsActive || IsPlantedHere) return;
        if (requesterTf == null) return;
        if (Vector3.Distance(requesterTf.position, worldAnchor.position) > interactRange) return;

        // ⛏️ GetSingleton은 없음 → 씬 내 인스턴스 검색으로 교체
        var mode = FindObjectOfType<BombModeHandler>();
        if (mode == null) return;
        mode.RPC_RequestPlant(siteIndex, requester);
    }

    /// <summary>로컬 입력: 해제 시도(해제팀만)</summary>
    public void TryInteractDefuse(PlayerRef requester, Transform requesterTf)
    {
        if (!IsActive || !IsPlantedHere) return;
        if (requesterTf == null) return;
        if (Vector3.Distance(requesterTf.position, worldAnchor.position) > interactRange) return;

        var mode = FindObjectOfType<BombModeHandler>();
        if (mode == null) return;
        mode.RPC_RequestDefuse(siteIndex, requester);
    }

    private bool IsLocalInstallerTeam()
    {
        if (Runner == null) return false;
        var local = Runner.LocalPlayer;
        if (!local.IsValid) return false;
        if (TeamAssigner.Instance == null) return false;

        return TeamAssigner.Instance.GetTeam(local) == TeamAssigner.Instance.GetInstallerTeam();
    }

    private bool IsLocalPlayerCollider(Collider other)
    {
        var nob = other.GetComponentInParent<NetworkObject>();
        return (Runner != null && nob != null && nob.InputAuthority == Runner.LocalPlayer);
    }
    private bool IsLocalPlayerCollider(Collider2D other)
    {
        var nob = other.GetComponentInParent<NetworkObject>();
        return (Runner != null && nob != null && nob.InputAuthority == Runner.LocalPlayer);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsActive) return;
        if (!IsLocalPlayerCollider(other)) return;

        bool showAlpha = false;

        if (!IsPlantedHere && IsLocalInstallerTeam())
        {
            showAlpha = true;
            GamePlayUIController.Instance?.InitBombInteractionButton(this, true);
        }
        else if (IsPlantedHere && IsLocalDefuseTeam())
        {
            showAlpha = true;
            GamePlayUIController.Instance?.InitBombInteractionButton(this, false);
        }

        if (showAlpha)
        {
            _localEligible = true;

            // 🔥 폭탄 비주얼 강제 활성화
            if (bombVisual != null && !bombVisual.activeSelf)
                bombVisual.SetActive(true);

            SetVisualAlpha(eligibleAlpha);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!IsLocalPlayerCollider(other)) return;

        _localEligible = false;
        SetVisualAlpha(defaultAlpha);

        // 설치 전이라면 다시 bombVisual을 비활성화
        if (!IsPlantedHere && bombVisual != null)
            bombVisual.SetActive(false);

        GamePlayUIController.Instance?.HideBombInteraction();
    }

    private IEnumerator ReleaseLocalInstallingAfter(float wait)
    {
        yield return new WaitForSeconds(wait);
        _localInstalling = false;
    }

    private void UpdateProgressUI()
    {
        if (bombProgressCanvas == null || bombProgressFill == null || bombProgressLabel == null)
            return;

        // 설치 중이면 표시
        if (isInstalling && !IsPlantedHere)
        {
            bombProgressCanvas.enabled = true;
            bombProgressFill.fillAmount = installProgress;
            bombProgressLabel.text = $"설치 중... ({Mathf.RoundToInt(installProgress * 100)}%)";
        }
        else
        {
            bombProgressCanvas.enabled = false;
        }
    }

    private static void OnInstallProgressChanged(Changed<BombSite> changed)
    {
        changed.Behaviour.UpdateProgressUI();
    }

    private static void OnIsInstallingChanged(Changed<BombSite> changed)
    {
        changed.Behaviour.UpdateProgressUI();
    }

    public void StartInstallation()
    {
        if (installCoroutine != null) return;

        isInstalling = true;
        installCoroutine = StartCoroutine(InstallRoutine());
    }

    private IEnumerator InstallRoutine()
    {
        float duration = 5f;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            installProgress = timer / duration;
            yield return null;
        }

        installProgress = 1f;
        isInstalling = false;
        installCoroutine = null;

    }

    public void Server_BeginInstall()
    {
        if (!Object.HasStateAuthority) return;
        isInstalling = true;
        installProgress = 0f;
        UpdateProgressUI();
    }

    public void Server_UpdateInstall(float normalized01)
    {
        if (!Object.HasStateAuthority) return;
        installProgress = Mathf.Clamp01(normalized01);
        // OnChanged 훅에 의해 각 클라 UI가 자동 갱신됨
    }

    public void Server_EndInstall()
    {
        if (!Object.HasStateAuthority) return;
        isInstalling = false;
        // 설치 취소 시에는 installProgress를 유지하거나 0으로 내릴 수 있음.
        UpdateProgressUI();
    }
}