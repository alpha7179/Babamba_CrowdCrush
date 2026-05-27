using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using Unity.XR.CoreUtils;

/// <summary>
/// 플레이어 이동, 상호작용, 멀미 모드를 제어하는 싱글톤 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>LocomotionProvider 일괄 활성화/비활성화를 통한 이동 제어</item>
///   <item>Interactor 오브젝트 활성화/비활성화를 통한 상호작용 제어</item>
///   <item>멀미 방지 비네팅(TunnelingVignette) 토글</item>
/// </list>
/// </remarks>
public class PlayerManager : MonoBehaviour
{
    #region Singleton
    public static PlayerManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    #endregion

    #region Inspector Settings
    [Header("Target Object Names")]
    [Tooltip("플레이어의 최상위 부모 객체 이름 (XR Origin 검색용 키워드)")]
    [SerializeField] private string originKeyword = "XROrigin";

    [Tooltip("멀미 방지 비네팅 오브젝트 이름 (Main Camera의 자식이어야 함)")]
    [SerializeField] private string vignetteKeyword = "TunnelingVignette";
    #endregion

    #region Internal State
    private GameObject currentXROrigin;
    #endregion

    #region Unity Lifecycle
    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (DataManager.Instance != null) DataManager.Instance.OnMotionSicknessChanged += SetComfortMode;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (DataManager.Instance != null) DataManager.Instance.OnMotionSicknessChanged -= SetComfortMode;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        currentXROrigin = FindXROrigin();

        if (currentXROrigin == null)
        {
            Debug.LogWarning($"[PlayerManager] '{scene.name}' 씬에서 '{originKeyword}' 객체를 찾을 수 없습니다.");
            return;
        }

        // GameStepManager가 활성화할 때까지 기능 잠금
        if (scene.name.Equals("Main_Street", System.StringComparison.OrdinalIgnoreCase))
        {
            SetInteraction(false);
            SetLocomotion(false);
            Debug.Log("[PlayerManager] Game Scene: Features Locked");
        }
        else
        {
            SetInteraction(true);
            SetLocomotion(true);
        }

        bool comfortMode = (DataManager.Instance != null) && DataManager.Instance.IsAntiMotionSicknessMode;
        SetComfortMode(comfortMode);
    }
    #endregion

    #region Public API

    /// <summary>이동(Move, Turn, Teleport) 기능 활성화/비활성화</summary>
    /// <param name="isEnabled">true이면 이동 허용</param>
    public void SetLocomotion(bool isEnabled)
    {
        if (!EnsureOriginFound()) return;

        // NOTE: LocomotionProvider 하위 컴포넌트를 일괄 제어하여 이름 의존 없이 동작함
        var providers = currentXROrigin.GetComponentsInChildren<LocomotionProvider>(true);

        foreach (var provider in providers)
        {
            provider.enabled = isEnabled;
        }

        Debug.Log($"[PlayerManager] Locomotion set to: {isEnabled} (Controlled {providers.Length} providers)");
    }

    /// <summary>상호작용(Ray, Direct Interactor) 기능 활성화/비활성화</summary>
    /// <param name="isEnabled">true이면 상호작용 허용</param>
    public void SetInteraction(bool isEnabled)
    {
        if (!EnsureOriginFound()) return;

        // NOTE: Locomotion/Teleport Interactor는 제외 — 이동 제어와 충돌 방지
        // TODO: 텔레포트 Ray 필터링 추가 필요 시 태그/레이어 기반 분리 검토

        string[] keywords = { "Interactor" };
        Transform[] allChildren = currentXROrigin.GetComponentsInChildren<Transform>(true);

        foreach (Transform child in allChildren)
        {
            if (child.name.Contains("Locomotion") || child.name.Contains("Teleport")) continue;

            if (child.name.Contains("Interactor"))
            {
                child.gameObject.SetActive(isEnabled);
            }
        }
    }

    /// <summary>씬 리셋 시 XROrigin을 파괴하여 다음 씬에서 새로 초기화되도록 함</summary>
    public void DestroyXROriginForReset()
    {
        if (currentXROrigin != null)
        {
            // NOTE: XROrigin 자식(vrTarget) 파괴 전 IKTargetFollowVRRig 비활성화 — 동일 프레임 LateUpdate에서 MissingReferenceException 방지
            foreach (var ikRig in FindObjectsByType<IKTargetFollowVRRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (ikRig != null) ikRig.enabled = false;

            Destroy(currentXROrigin);
            currentXROrigin = null;
        }
    }

    /// <summary>멀미 방지 비네팅 활성화/비활성화</summary>
    /// <param name="isEnabled">true이면 비네팅 표시</param>
    public void SetComfortMode(bool isEnabled)
    {
        Camera mainCam = Camera.main;
        if (mainCam == null && EnsureOriginFound()) mainCam = currentXROrigin.GetComponentInChildren<Camera>();

        if (mainCam == null) return;

        Transform vignetteTr = FindChildRecursive(mainCam.transform, vignetteKeyword);
        if (vignetteTr != null)
        {
            vignetteTr.gameObject.SetActive(isEnabled);
        }
    }

    #endregion

    #region Helpers

    private bool EnsureOriginFound()
    {
        if (currentXROrigin == null) currentXROrigin = FindXROrigin();
        return currentXROrigin != null;
    }

    private GameObject FindXROrigin()
    {
        // NOTE: XROrigin 컴포넌트 타입으로 탐색 — 오브젝트 이름("XR Origin (XR Rig)" 등)에 의존하지 않음
        XROrigin xrOriginComponent = FindAnyObjectByType<XROrigin>();
        if (xrOriginComponent != null) return xrOriginComponent.gameObject;

        // NOTE: XROrigin 컴포넌트 없을 시 이름 키워드로 대체 탐색
        var rootObjs = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var obj in rootObjs)
        {
            if (obj.name.IndexOf(originKeyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return obj;
            }
        }

        // NOTE: 키워드 탐색 실패 시 Player 태그로 최종 대체 탐색
        GameObject tagObj = GameObject.FindGameObjectWithTag("Player");
        if (tagObj != null) return tagObj;

        return null;
    }

    private Transform FindChildRecursive(Transform parent, string namePart)
    {
        foreach (Transform child in parent)
        {
            if (child.name.IndexOf(namePart, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return child;
            }
            Transform result = FindChildRecursive(child, namePart);
            if (result != null) return result;
        }
        return null;
    }
    #endregion
}
