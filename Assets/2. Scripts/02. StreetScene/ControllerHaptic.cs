using UnityEngine;
using System.Collections.Generic;
using UnityEngine.XR;

/// <summary>
/// XR 컨트롤러(양손) 햅틱 진동을 1회 재생하는 싱글톤 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>압력 단계(0~5) 변경 시 단계에 비례한 진동 1회 재생</item>
///   <item>DataManager 햅틱 강도 설정 반영</item>
///   <item>DontDestroyOnLoad 싱글톤 생명주기 관리</item>
/// </list>
/// </remarks>
public class ControllerHaptic : MonoBehaviour
{
    #region Singleton

    /// <summary>전역 싱글톤 인스턴스</summary>
    public static ControllerHaptic Instance { get; private set; }

    #endregion

    #region Inspector Settings

    [Header("Haptic Settings")]
    [Tooltip("압력 단계 진동 지속 시간 (초)")]
    [SerializeField] private float hapticDuration = 0.3f;
    [Header("Debug")]
    [SerializeField] private bool showDebugLog = true;

    #endregion

    #region Internal State

    private int _lastPressureLevel = -1; // 동일 단계 중복 진동 방지

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
    /// 압력 단계 변경 시 단계에 비례한 진동을 1회 재생함.
    /// 동일 단계 재호출은 무시됨.
    /// </summary>
    /// <param name="level">압력 단계 (0=안전, 1=경고, 2=압박, 3=위험, 4=마비, 5=치명)</param>
    public void PlayPressureHaptic(int level)
    {
        if (_lastPressureLevel == level) return; // 동일 단계 중복 무시
        _lastPressureLevel = level;

        if (level <= 0) return; // 안전 단계는 진동 없음

        // NOTE: 진폭 수치는 PressureConfig에서 관리 — 미배치 시 선형 폴백
        float rawAmplitude = PressureConfig.Instance != null
            ? PressureConfig.Instance.GetControllerHapticAmplitude(level)
            : Mathf.Clamp01((float)level / 5f);

        float adjusted = DataManager.Instance != null
            ? DataManager.Instance.GetAdjustedHapticStrength(rawAmplitude)
            : rawAmplitude;

        SendToControllers(adjusted, hapticDuration);

        if (showDebugLog)
            Debug.Log($"[ControllerHaptic] Level {level} → amplitude {adjusted:F2}, duration {hapticDuration:F2}s");
    }

    /// <summary>
    /// 진폭·지속 시간을 직접 지정해 양손 컨트롤러에 1회 진동을 전송함
    /// </summary>
    /// <param name="amplitude">진동 세기 (0.0~1.0)</param>
    /// <param name="duration">지속 시간 (초)</param>
    public void PlayHaptic(float amplitude, float duration)
    {
        float adjusted = DataManager.Instance != null
            ? DataManager.Instance.GetAdjustedHapticStrength(amplitude)
            : amplitude;

        SendToControllers(adjusted, duration);
    }

    /// <summary>
    /// 마지막 압력 단계 기록을 초기화함 (씬 전환·게임 리셋 시 호출)
    /// </summary>
    public void ResetLevel()
    {
        _lastPressureLevel = -1;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// 연결된 양손 XR 컨트롤러에 햅틱 임펄스를 전송함
    /// </summary>
    private void SendToControllers(float amplitude, float duration)
    {
        var devices = new List<InputDevice>();

        // 오른손 컨트롤러
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right, devices);
        foreach (var device in devices)
            device.SendHapticImpulse(0, amplitude, duration);

        devices.Clear();

        // 왼손 컨트롤러
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left, devices);
        foreach (var device in devices)
            device.SendHapticImpulse(0, amplitude, duration);
    }

    #endregion
}
