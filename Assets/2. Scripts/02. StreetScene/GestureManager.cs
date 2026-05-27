using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// ABC 방어 자세 트리거존 판정 및 등반 상태 판별 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>GestureTriggerZone 기반 양손 위치 감지</item>
///   <item>컨트롤러 트리거를 이용한 강제 발동(Fail-safe) 지원</item>
///   <item>등반(Climbing) 상태 판별</item>
/// </list>
/// </remarks>
public class GestureManager : MonoBehaviour
{
    #region Inspector Settings
    [Header("Gesture Trigger Zones")]
    [Tooltip("Chest > Left Trigger 오브젝트에 붙은 GestureTriggerZone")]
    [SerializeField] private GestureTriggerZone leftZone;
    [Tooltip("Chest > Right Trigger 오브젝트에 붙은 GestureTriggerZone")]
    [SerializeField] private GestureTriggerZone rightZone;

    [Header("Fail-Safe Settings")]
    [Tooltip("체크 시: 존 진입 없이 트리거 버튼만 눌러도 액션을 성공으로 처리합니다.")]
    [SerializeField] private bool useTriggerFailSafe = false;
    [SerializeField] private float triggerThreshold = 0.8f;

    [Header("Feedback Settings")]
    [SerializeField] private SFXType rangeEnterSFX = SFXType.UI_Click;
    [SerializeField] private float rangeEnterHapticIntensity = 0.3f;
    [SerializeField] private float rangeEnterHapticDuration = 0.1f;
    [SerializeField] private float holdingHapticIntensity = 0.05f;
    #endregion

    #region Internal State
    private bool _isInRange = false;
    private bool _isActionValid = false;
    #endregion

    #region Unity Lifecycle
    private void Update()
    {
        bool currentRangeCheck = CheckZonesOccupied();

        // NOTE: 범위에 새로 진입한 프레임에서만 피드백 재생
        if (currentRangeCheck && !_isInRange)
            PlayRangeEnterFeedback();

        _isInRange = currentRangeCheck;

        // 존 진입 OR Fail-safe(트리거 버튼) → 액션 유효
        bool failSafeActive = useTriggerFailSafe && CheckTriggersPressed();
        if (_isInRange || failSafeActive)
        {
            _isActionValid = true;
            TriggerContinuousHaptic(holdingHapticIntensity);
        }
        else
        {
            _isActionValid = false;
        }
    }
    #endregion

    #region Public API
    /// <summary>현재 프레임에서 ABC 방어 자세가 유효한지 반환함</summary>
    public bool IsActionValid() => _isActionValid;

    /// <summary>양손으로 ClimbHandle을 잡고 있는 상태인지 반환함</summary>
    public bool IsHoldingClimbHandle() => ClimbHandle.ActiveGrabCount > 1;

    /// <summary>가슴 트리거 존을 활성화/비활성화한다. ABC 자세 체크 구간 진입·퇴장 시 호출.</summary>
    public void SetZonesActive(bool active)
    {
        if (leftZone != null) leftZone.enabled = active;
        if (rightZone != null) rightZone.enabled = active;
    }
    #endregion

    #region Internal Logic
    // 양쪽 존 모두 점유 상태인지 확인 — 어느 한쪽이 null이면 false
    private bool CheckZonesOccupied()
    {
        if (leftZone == null || rightZone == null) return false;
        return leftZone.IsOccupied && rightZone.IsOccupied;
    }

    // Fail-safe용 트리거 버튼 확인 — 양쪽 모두 눌렸을 때만 true
    private bool CheckTriggersPressed()
    {
        var leftDevices = new List<InputDevice>();
        var rightDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, leftDevices);
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rightDevices);

        bool leftTrigger = leftDevices.Count > 0 && leftDevices[0].TryGetFeatureValue(CommonUsages.trigger, out float lv) && lv > triggerThreshold;
        bool rightTrigger = rightDevices.Count > 0 && rightDevices[0].TryGetFeatureValue(CommonUsages.trigger, out float rv) && rv > triggerThreshold;
        return leftTrigger && rightTrigger;
    }
    #endregion

    #region Helpers
    private void PlayRangeEnterFeedback()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(rangeEnterSFX);
        TriggerImpulseHaptic(rangeEnterHapticIntensity, rangeEnterHapticDuration);
    }

    private void TriggerImpulseHaptic(float rawIntensity, float duration)
    {
        float finalIntensity = rawIntensity;
        if (DataManager.Instance != null)
            finalIntensity = DataManager.Instance.GetAdjustedHapticStrength(rawIntensity);

        if (finalIntensity <= 0.01f) return;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);
        foreach (var device in devices)
            if (device.TryGetHapticCapabilities(out var cap) && cap.supportsImpulse)
                device.SendHapticImpulse(0, finalIntensity, duration);
    }

    private void TriggerContinuousHaptic(float intensity)
    {
        TriggerImpulseHaptic(intensity, 0.1f);
    }
    #endregion
}
