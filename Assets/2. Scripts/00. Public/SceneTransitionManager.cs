using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.Collections;

/// <summary>
/// 페이드 기반 씬 전환 및 매니저 리셋을 담당하는 싱글톤 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>페이드 인/아웃 연출을 통한 씬 전환 처리</item>
///   <item>카메라 동기화 기반 월드 스페이스 페이드 캔버스 위치 갱신</item>
///   <item>씬 전환 시 기존 싱글톤 매니저 인스턴스 리셋</item>
/// </list>
/// </remarks>
public class SceneTransitionManager : MonoBehaviour
{
    #region Singleton
    public static SceneTransitionManager Instance;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // NOTE: DontDestroyOnLoad는 최상위 오브젝트에만 동작 — 부모(XROrigin 등)가 통째로 영속화되는 것을 방지
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

    [Header("Components")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private Canvas fadeCanvas;

    [Header("Settings")]
    [SerializeField] private float fadeDuration = 1.0f;
    [SerializeField] private float distFromCamera = 0.2f;

    [Header("Camera Settings")]
    [Tooltip("UI 카메라가 있다면 이 태그를 가진 카메라를 우선적으로 찾습니다.")]
    [SerializeField] private string uiCameraTag = "UICamera";

    #endregion

    #region Internal State
    private bool isFading = false;
    private Camera cachedCamera;
    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        Camera.onPreCull += HandleCameraPreCull;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Camera.onPreCull -= HandleCameraPreCull;
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private void OnApplicationQuit()
    {
        // NOTE: 에디터 Stop·앱 종료 시 파괴 순서 비결정적 — IKTargetFollowVRRig LateUpdate가 vrTarget 파괴 후 실행되면 MissingReferenceException 발생
        DisableAllIKRigs();
    }

    private void Start()
    {
        if (fadeCanvas != null)
        {
            fadeCanvas.sortingOrder = 32767;
            // NOTE: 캔버스 레이어는 에디터 설정을 따름
        }

        ForceUpdateCanvasPosition();
        StartCoroutine(FadeRoutine(1f, 0f));
    }

    private void LateUpdate()
    {
        if (isFading || (fadeCanvasGroup != null && fadeCanvasGroup.alpha > 0.01f))
        {
            ForceUpdateCanvasPosition();
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// 지정된 씬으로 페이드 전환을 수행한다.
    /// </summary>
    /// <param name="sceneName">전환할 씬 이름</param>
    public void LoadScene(string sceneName)
    {
        if (isFading) return;
        StartCoroutine(TransitionRoutine(sceneName));
    }

    /// <summary>
    /// 화면을 페이드 아웃(검게)한다. 씬 전환 없이 순간이동 연출 등에 사용.
    /// </summary>
    public IEnumerator FadeOut()
    {
        isFading = true;
        ForceUpdateCanvasPosition();
        yield return StartCoroutine(FadeRoutine(0f, 1f));
    }

    /// <summary>
    /// 화면을 페이드 인(밝게)한다. FadeOut 호출 이후 복귀 연출에 사용.
    /// </summary>
    public IEnumerator FadeIn()
    {
        ForceUpdateCanvasPosition();
        yield return StartCoroutine(FadeRoutine(1f, 0f));
        isFading = false;
    }

    #endregion

    #region Internal Logic

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        cachedCamera = null;
        ForceUpdateCanvasPosition();
    }

    private void HandleCameraPreCull(Camera cam)
    {
        SyncCanvasToCamera(cam);
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        SyncCanvasToCamera(cam);
    }

    // NOTE: MainCamera보다 UICamera(오버레이)를 우선 탐색
    private void ForceUpdateCanvasPosition()
    {
        if (cachedCamera != null && cachedCamera.gameObject.activeInHierarchy)
        {
            SyncCanvasToCamera(cachedCamera);
            return;
        }

        GameObject uiCamObj = GameObject.FindGameObjectWithTag(uiCameraTag);
        if (uiCamObj != null)
        {
            cachedCamera = uiCamObj.GetComponent<Camera>();
        }

        if (cachedCamera == null)
        {
            cachedCamera = Camera.main;
        }

        if (cachedCamera == null)
        {
            cachedCamera = FindAnyObjectByType<Camera>();
        }

        if (cachedCamera != null)
        {
            SyncCanvasToCamera(cachedCamera);
        }
    }

    private void SyncCanvasToCamera(Camera cam)
    {
        // NOTE: cachedCamera 외 카메라는 위치 갱신 무시
        if (cachedCamera != null && cam != cachedCamera) return;

        if (cam == null || fadeCanvas == null) return;
        if (!isFading && fadeCanvasGroup.alpha <= 0.01f) return;

        if (fadeCanvas.worldCamera != cam)
        {
            fadeCanvas.worldCamera = cam;
        }

        Transform camTr = cam.transform;
        Transform canvasTr = fadeCanvas.transform;

        canvasTr.position = camTr.position + (camTr.forward * distFromCamera);
        canvasTr.rotation = camTr.rotation;

        // NOTE: Orthographic 카메라일 경우 거리·스케일 조정 필요
    }

    #endregion

    #region Coroutines

    private IEnumerator TransitionRoutine(string sceneName)
    {
        isFading = true;

        // NOTE: 씬 언로드 전 IKTargetFollowVRRig 비활성화 — vrTarget 파괴 타이밍 불일치로 인한 MissingReferenceException 방지
        DisableAllIKRigs();

        ForceUpdateCanvasPosition();
        yield return StartCoroutine(FadeRoutine(0f, 1f));

        // NOTE: 화면이 완전히 검어진 후 매니저 리셋 — 페이드 전 동기 실행 시 발생하던 프레임 히치 방지
        if (sceneName == "Main_Intro")
        {
            // NOTE: 시나리오·UI 코루틴 선행 종료 — ResetGameManagers()로 매니저 파괴 후
            //       잔여 코루틴이 null 인스턴스에 접근하는 MissingReferenceException 방지
            var gsm = FindAnyObjectByType<GameStepManager>();
            if (gsm != null) gsm.StopAllCoroutines();
            var ssm = FindAnyObjectByType<SubwayStepManager>();
            if (ssm != null) ssm.StopAllCoroutines();
            var sgm = FindAnyObjectByType<SubwayGestureManager>();
            if (sgm != null) sgm.StopAllCoroutines();
            var ingameUI = FindAnyObjectByType<IngameUIManager>();
            if (ingameUI != null) ingameUI.StopAllCoroutines();
            var outtroUI = FindAnyObjectByType<OuttroUIManager>();
            if (outtroUI != null) outtroUI.StopAllCoroutines();

            ResetGameManagers();
        }

        // NOTE: Main_Subway_Exit 진입 시 DDOL XROrigin 파괴 — 지하철씬 XROrigin이 아웃트로 씬으로 유입되는 것 방지
        //       페이드 아웃 완료(화면 검음) 후 파괴 — 카메라 소실로 인한 렌더링 깜빡임 방지
        if (sceneName == "Main_Subway_Exit")
        {
            // NOTE: ElevatorSimple은 XROrigin CharacterController를 _characters에 캐시 — 파괴 후 FixedUpdate의 Move() 호출 시 MissingReferenceException 방지
            foreach (var elevator in FindObjectsByType<ElevatorSimple>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (elevator != null) elevator.enabled = false;

            if (PlayerManager.Instance != null) PlayerManager.Instance.DestroyXROriginForReset();
        }

        // NOTE: 매니저 파괴 후 GC 강제 실행 및 미사용 에셋 언로드 — 씬 활성화 시점의 GC 스파이크 분산
        System.GC.Collect();
        yield return Resources.UnloadUnusedAssets();

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            ForceUpdateCanvasPosition();
            yield return null;
        }

        op.allowSceneActivation = true;

        while (!op.isDone)
        {
            ForceUpdateCanvasPosition();
            yield return null;
        }

        // NOTE: 씬 로드 직후 깜빡임 방지 대기
        cachedCamera = null;
        ForceUpdateCanvasPosition();

        // HACK: 5프레임 대기로 깜빡임 방지
        for (int i = 0; i < 5; i++)
        {
            ForceUpdateCanvasPosition();
            yield return null;
        }

        yield return StartCoroutine(FadeRoutine(1f, 0f));

        isFading = false;
    }

    private IEnumerator FadeRoutine(float startAlpha, float endAlpha)
    {
        float elapsedTime = 0f;
        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.blocksRaycasts = true;
            fadeCanvasGroup.alpha = startAlpha;
        }

        while (elapsedTime < fadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float newAlpha = Mathf.Lerp(startAlpha, endAlpha, elapsedTime / fadeDuration);

            if (fadeCanvasGroup != null)
            {
                fadeCanvasGroup.alpha = newAlpha;
                ForceUpdateCanvasPosition();
            }
            yield return null;
        }

        if (fadeCanvasGroup != null)
        {
            fadeCanvasGroup.alpha = endAlpha;
            fadeCanvasGroup.blocksRaycasts = (endAlpha > 0.9f);
        }
    }

    #endregion

    #region Helpers

    /// <summary>씬에 존재하는 모든 IKTargetFollowVRRig 컴포넌트를 비활성화한다.</summary>
    private void DisableAllIKRigs()
    {
        // NOTE: FindObjectsInactive.Include — VR Body 오브젝트 비활성 상태에서도 탐색 보장
        //       FindObjectsByType — 복수 인스턴스(씬별 다중 VR Body) 모두 처리
        foreach (var ikRig in FindObjectsByType<IKTargetFollowVRRig>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ikRig != null) ikRig.enabled = false;
        }
    }

    /// <summary>기존 싱글톤 매니저 인스턴스를 파괴한다.</summary>
    private void ResetGameManagers()
    {
        // NOTE: AudioManager는 파괴하지 않고 상태만 초기화 — 씬 간 오디오 잔존 방지
        if (AudioManager.Instance != null) AudioManager.Instance.ResetForSceneTransition();
        // NOTE: BodyHaptic은 파괴하지 않고 코루틴·진동 정지만 수행 — bHaptics 잔여 진동 방지
        if (BodyHaptic.Instance != null) BodyHaptic.Instance.StopBodyHaptics();
        // NOTE: ControllerHaptic은 파괴하지 않고 레벨 기록만 초기화 — 재진입 시 동일 레벨 무시 방지
        if (ControllerHaptic.Instance != null) ControllerHaptic.Instance.ResetLevel();
        if (DataManager.Instance != null) Destroy(DataManager.Instance.gameObject);
        if (GameManager.Instance != null) Destroy(GameManager.Instance.gameObject);
        if (ControllerInputManager.Instance != null) Destroy(ControllerInputManager.Instance.gameObject);
        if (PlayerManager.Instance != null)
        {
            // NOTE: XROrigin을 먼저 파괴 — DDOL에 잔존하던 XROrigin이 Main_Intro에 그대로 유입되는 것을 방지
            PlayerManager.Instance.DestroyXROriginForReset();
            Destroy(PlayerManager.Instance.gameObject);
        }
    }

    #endregion
}
