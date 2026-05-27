using UnityEngine;

/// <summary>
/// 압력 단계(0~5)별 비네팅·사운드·햅틱 수치를 통합 관리하는 싱글톤 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>레벨 0 = 안전, 1 = 경고, 2 = 압박, 3 = 위험, 4 = 마비, 5 = 치명</item>
///   <item>씬에 GameObject로 배치하면 자동으로 DontDestroyOnLoad 적용</item>
///   <item>IngameUIManager, ControllerHaptic 에서 PressureConfig.Instance 로 접근</item>
/// </list>
/// </remarks>
public class PressureConfig : MonoBehaviour
{
    #region Constants

    /// <summary>압력 단계 수 (0 포함 6단계)</summary>
    public const int LEVEL_COUNT = 6;

    #endregion

    #region Singleton

    /// <summary>전역 싱글톤 인스턴스</summary>
    public static PressureConfig Instance { get; private set; }

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

    // 설계 기준: 0~2단계 약하게 유지, 3단계부터 급격히 강화
    // 0=안전, 1=경고, 2=압박, 3=위험, 4=마비, 5=치명

    [Header("비네팅 강도 (레벨 0~5)")]
    [Tooltip("레벨별 비네팅 강도. 0=없음 ~ 1=최대(시야 거의 차단). 배열 크기는 반드시 6이어야 함.")]
    // 0~2: 거의 안 보이는 수준 / 3: 시야에 거슬림 / 4: 새빨갛게 거의 가림 / 5: 완전히 가림
    public float[] VignetteIntensity = { 0f, 0.14f, 0.32f, 0.50f, 0.82f, 1.0f };

    [Header("사운드 볼륨 (레벨 0~5)")]
    [Tooltip("레벨별 심장박동 SFX 볼륨 스케일.")]
    // 3단계부터 급상승 — 위협감 전달
    public float[] HeartbeatVolume  = { 0f, 0.10f, 0.20f, 0.65f, 0.85f, 1.00f };

    [Tooltip("레벨별 호흡 SFX 볼륨 스케일.")]
    // 3단계부터 공황 호흡 표현
    public float[] BreathVolume     = { 0f, 0.08f, 0.18f, 0.60f, 0.82f, 1.00f };

    [Tooltip("레벨별 이명(삐) SFX 볼륨 스케일. 4단계부터 현기증 느낌으로 급상승.")]
    // 0~3: 거의 없음 / 4~5: 삐 소리로 극한 압박감 표현
    public float[] EarRingingVolume = { 0f, 0.00f, 0.00f, 0.05f, 0.55f, 1.00f };

    [Header("햅틱 진폭 강도 (레벨 0~5)")]
    [Tooltip("레벨별 컨트롤러 진동 원시 진폭 (0~1). DataManager 보정 전 값.")]
    // 3단계부터 확실히 느껴지는 수준으로 급상승
    public float[] ControllerHapticAmplitude = { 0f, 0.06f, 0.14f, 0.55f, 0.80f, 1.0f };

    [Tooltip("레벨별 bHaptics 이벤트 강도 배율 (0~1). Studio 등록 이벤트에 곱해지는 스케일.")]
    // 기존 이벤트(front_1 ~ front_6)는 유지하되 추가 배율로 미세 조정
    // 3단계부터 확실히 느껴지는 수준으로 급상승
    public float[] BodyHapticIntensity = { 0f, 0.10f, 0.20f, 0.55f, 0.80f, 1.0f };

    #endregion

    #region Public API

    /// <summary>레벨에 해당하는 비네팅 강도를 반환함. 범위 초과 시 0 반환.</summary>
    public float GetVignetteIntensity(int level) => GetValue(VignetteIntensity, level);

    /// <summary>레벨에 해당하는 심장박동 볼륨을 반환함.</summary>
    public float GetHeartbeatVolume(int level) => GetValue(HeartbeatVolume, level);

    /// <summary>레벨에 해당하는 호흡 볼륨을 반환함.</summary>
    public float GetBreathVolume(int level) => GetValue(BreathVolume, level);

    /// <summary>레벨에 해당하는 이명 볼륨을 반환함.</summary>
    public float GetEarRingingVolume(int level) => GetValue(EarRingingVolume, level);

    /// <summary>레벨에 해당하는 컨트롤러 햅틱 원시 진폭을 반환함.</summary>
    public float GetControllerHapticAmplitude(int level) => GetValue(ControllerHapticAmplitude, level);

    /// <summary>레벨에 해당하는 bHaptics 바디 햅틱 강도 배율을 반환함.</summary>
    public float GetBodyHapticIntensity(int level) => GetValue(BodyHapticIntensity, level);

    #endregion

    #region Helpers

    // 배열 범위 안전 접근 — 배열 미설정 또는 범위 초과 시 0 반환
    private float GetValue(float[] array, int level)
    {
        if (array == null || level < 0 || level >= array.Length) return 0f;
        return array[level];
    }

    #endregion

    #region Validation

    private void OnValidate()
    {
        ValidateArray(ref VignetteIntensity,         nameof(VignetteIntensity));
        ValidateArray(ref HeartbeatVolume,           nameof(HeartbeatVolume));
        ValidateArray(ref BreathVolume,              nameof(BreathVolume));
        ValidateArray(ref EarRingingVolume,          nameof(EarRingingVolume));
        ValidateArray(ref ControllerHapticAmplitude, nameof(ControllerHapticAmplitude));
        ValidateArray(ref BodyHapticIntensity,        nameof(BodyHapticIntensity));
    }

    // 배열 크기가 LEVEL_COUNT와 다를 경우 Editor 경고 출력
    private void ValidateArray(ref float[] array, string fieldName)
    {
        if (array == null || array.Length != LEVEL_COUNT)
            Debug.LogWarning($"[PressureConfig] '{fieldName}' 배열 크기는 {LEVEL_COUNT}이어야 함. 현재: {array?.Length ?? 0}");
    }

    #endregion
}
