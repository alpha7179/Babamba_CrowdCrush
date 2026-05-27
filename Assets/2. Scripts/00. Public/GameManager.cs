using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;

/// <summary>
/// 게임의 전체 생명주기, 씬 전환, 전역 상태(일시정지 등)를 관리하는 최상위 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>일시정지 및 재개 제어, 상태 변경 이벤트 발행</item>
///   <item>SceneTransitionManager를 통한 씬 전환 요청</item>
///   <item>게임 클리어 및 게임 오버 상태 관리, 이벤트 전파</item>
/// </list>
/// </remarks>
public class GameManager : MonoBehaviour
{
    #region Singleton

    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        // 중복 생성 방지 및 씬 전환 시 파괴 방지
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null; // DontDestroyOnLoad 대상은 최상위 계층이어야 함
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region Inspector Settings

    [Header("Debug Settings")]
    [Tooltip("디버그 로그 출력 여부를 설정합니다.")]
    [SerializeField] private bool isDebug = true;

    #endregion

    #region Events

    /// <summary>일시정지 상태 변경 시 발행 (bool: isPaused)</summary>
    public event Action<bool> OnPauseStateChanged;

    /// <summary>씬 로드 완료 시 발행 (string: sceneName)</summary>
    public event Action<string> OnSceneLoaded;

    /// <summary>게임 클리어(목표 달성) 시 발행</summary>
    public event Action OnGameClear;

    /// <summary>게임 오버(실패) 시 발행</summary>
    public event Action OnGameOver;

    #endregion

    #region Global State

    [Header("Game State Info")]
    /// <summary>현재 게임 일시정지 상태 여부</summary>
    public bool IsPaused = false;

    /// <summary>현재 활성화된 씬 이름</summary>
    public string CurrentSceneName;
    [SerializeField] private GameScene currentScene;
    /// <summary>게임 씬 종류</summary>
    public enum GameScene { Menu, Simulator }

    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void Start()
    {
        CurrentSceneName = SceneManager.GetActiveScene().name;
        currentScene = GameScene.Menu;
    }

    private void GameSceneChange(string value)
    {
        if (CurrentSceneName != value)
        {
            currentScene = (currentScene == GameScene.Menu) ? GameScene.Simulator : GameScene.Menu;
            CurrentSceneName = value;
        }
    }

    #endregion

    #region Public API

    /// <summary>게임 일시정지 상태를 토글함</summary>
    public void TogglePause()
    {
        // 인트로 씬 등 일시정지 불필요 씬 예외 처리
        if (CurrentSceneName.Equals("Main_Intro ", StringComparison.OrdinalIgnoreCase)) return;

        IsPaused = !IsPaused;

        Time.timeScale = IsPaused ? 0f : 1f;

        OnPauseStateChanged?.Invoke(IsPaused);

        if (isDebug) Debug.Log($"[GameManager] Pause State Changed: {IsPaused}");
    }

    /// <summary>지정된 이름의 씬을 로드함</summary>
    /// <param name="sceneName">이동할 씬 이름</param>
    public void LoadScene(string sceneName)
    {
        if (SceneTransitionManager.Instance != null)
        {
            if (isDebug) Debug.Log($"[GameManager] Requesting Fade Transition to: {sceneName}");
            SceneTransitionManager.Instance.LoadScene(sceneName);
        }
        else
        {
            // NOTE: SceneTransitionManager 부재 시 비상용 직접 로드
            if (isDebug) Debug.LogWarning("[GameManager] SceneTransitionManager not found. Loading directly.");
            StartCoroutine(LoadSceneRoutine(sceneName));
        }
    }

    /// <summary>게임 클리어(미션 성공) 이벤트를 발행함</summary>
    public void TriggerGameClear()
    {
        if (isDebug) Debug.Log("[GameManager] Mission Clear!");
        OnGameClear?.Invoke();
    }

    /// <summary>게임 오버(미션 실패) 이벤트를 발행함</summary>
    public void TriggerGameOver()
    {
        if (isDebug) Debug.Log("[GameManager] Game Over!");
        OnGameOver?.Invoke();
    }

    /// <summary>애플리케이션을 종료함 (에디터에서는 플레이 모드 중단)</summary>
    public void QuitGame()
    {
        if (isDebug) Debug.Log("[GameManager] Quitting Application...");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    #endregion

    #region Internal Logic

    /// <summary>씬 로드 완료 콜백 — 게임 상태 초기화</summary>
    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        GameSceneChange(scene.name);

        // 씬 전환 시 일시정지 해제 및 시간 정상화
        Time.timeScale = 1f;
        IsPaused = false;

        OnSceneLoaded?.Invoke(scene.name);

        if (isDebug) Debug.Log($"[GameManager] Scene Loaded & State Reset: {scene.name}");
    }

    /// <summary>SceneTransitionManager 부재 시 비상용 씬 로드 코루틴</summary>
    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
    }

    #endregion
}
