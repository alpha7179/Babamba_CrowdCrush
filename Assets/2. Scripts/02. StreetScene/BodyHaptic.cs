using UnityEngine;
using System.Collections;
using Bhaptics.SDK2;

/// <summary>
/// bHaptics 8방향 반복 진동 패턴을 제어하는 싱글톤 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>레벨(1~6)별 8방향 햅틱 이벤트 반복 재생</item>
///   <item>진동 루프 시작·정지 및 SDK 잔여 진동 제거</item>
///   <item>DontDestroyOnLoad 싱글톤 생명주기 관리</item>
/// </list>
/// </remarks>
public class BodyHaptic : MonoBehaviour
{
    #region Singleton

    /// <summary>전역 싱글톤 인스턴스</summary>
    public static BodyHaptic Instance { get; private set; }

    #endregion

    #region Inspector Settings

    [Header("Settings")]
    [Tooltip("진동 반복 간격 (초). 0.5 ~ 1.0초 사이로 짧게 설정해보세요.")]
    [SerializeField] private float loopInterval = 1.0f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    #endregion

    #region Internal State

    /// <summary>8방향 bHaptics 이벤트 ID 접두사 배열</summary>
    private readonly string[] directionPrefixes = new string[]
    {
        "b_right", "b_left", "f_right", "f_left",
        "front", "back", "right", "left"
    };

    private Coroutine _hapticCoroutine;
    private int _currentLevel = -1;

    #endregion

    #region Unity Lifecycle

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

    #region Public API

    /// <summary>
    /// 지정 레벨의 햅틱을 반복 재생함
    /// </summary>
    /// <param name="level">재생할 햅틱 레벨 (1~6)</param>
    public void PlayBodyHaptics(int level)
    {
        if (_currentLevel == level) return; // 동일 레벨 중복 요청 무시

        if (showDebugLog) Debug.Log($"[BodyHaptic] Start Loop Level: {level}");

        StopBodyHaptics(false); // 로그 중복 출력 방지

        if (level < 1 || level > 6)
        {
            return;
        }

        _currentLevel = level;
        _hapticCoroutine = StartCoroutine(HapticLoopRoutine(level));
    }

    /// <summary>
    /// 햅틱 재생을 즉시 중단함
    /// </summary>
    /// <param name="log">true이면 디버그 로그 출력</param>
    public void StopBodyHaptics(bool log = true)
    {
        if (log && showDebugLog) Debug.Log("[BodyHaptic] Stop Loop");

        if (_hapticCoroutine != null)
        {
            StopCoroutine(_hapticCoroutine);
            _hapticCoroutine = null;
        }

        BhapticsLibrary.StopAll(); // NOTE: SDK 잔여 진동 제거

        _currentLevel = -1;
    }

    [Header("Button에서 사용할 기본 레벨")]
    [Range(1, 6)]
    public int defaultLevel = 1;

    /// <summary>
    /// defaultLevel 값으로 햅틱을 재생함
    /// </summary>
    public void PlayDefaultLevel()
    {
        PlayBodyHaptics(defaultLevel);
    }

    #endregion

    #region Coroutines

    /// <summary>
    /// 8방향 햅틱 이벤트를 loopInterval 간격으로 무한 반복 재생함
    /// 외부에서 StopBodyHaptics 호출 시 종료됨
    /// </summary>
    private IEnumerator HapticLoopRoutine(int level)
    {
        while (true)
        {
            if (showDebugLog) Debug.Log($"[BodyHaptic] Playing Pulse... (Level {level})");

            // NOTE: PressureConfig 배율(0~1)을 SDK 요구 int(0~100)로 변환 — 미배치 시 100(원본 그대로)
            float rawIntensity = PressureConfig.Instance != null
                ? PressureConfig.Instance.GetBodyHapticIntensity(level)
                : 1.0f;
            int intensityInt = Mathf.RoundToInt(rawIntensity * 100f);

            foreach (var prefix in directionPrefixes)
            {
                string eventId = $"{prefix}_{level}";
                BhapticsLibrary.Play(eventId, intensityInt, 1.0f, 0f, 0f);
            }

            float waitTime = Mathf.Max(0.1f, loopInterval); // 0초 무한루프 방지
            yield return new WaitForSeconds(waitTime);
        }
    }

    #endregion
}
