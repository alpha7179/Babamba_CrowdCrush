using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 지하철 씬 전용 제스처 판정 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>서류가방 ABC 자세 — leftBagZone·rightBagZone 동시 진입 + 양 트리거 판정</item>
///   <item>넘어진 ABC 자세 — leftChestZone·rightChestZone 동시 진입 + 양 트리거 판정</item>
///   <item>기둥/난간 잡기 — ClimbHandle.ActiveGrabCount 기반 판정</item>
///   <item>Fail-safe 트리거 강제 발동 지원</item>
/// </list>
/// </remarks>
public class SubwayGestureManager : MonoBehaviour
{
    #region Inspector Settings

    [Header("Gesture Trigger Zones")]
    [Tooltip("가방 핸들링 감지 존 (왼쪽) — 왼손 콜라이더 등록")]
    [SerializeField] private GestureTriggerZone leftBagZone;
    [Tooltip("가방 핸들링 감지 존 (오른쪽) — 오른손 콜라이더 등록")]
    [SerializeField] private GestureTriggerZone rightBagZone;
    [Tooltip("넘어진 ABC 자세 감지 존 (왼쪽) — 왼손 콜라이더 등록")]
    [SerializeField] private GestureTriggerZone leftChestZone;
    [Tooltip("넘어진 ABC 자세 감지 존 (오른쪽) — 오른손 콜라이더 등록")]
    [SerializeField] private GestureTriggerZone rightChestZone;

    [Header("Fail-Safe Settings")]
    [Tooltip("체크 시: 존 진입 없이 트리거만 당겨도 액션을 성공으로 처리함")]
    [SerializeField] private bool useTriggerFailSafe = true;
    [SerializeField] private float triggerThreshold = 0.8f;

    [Header("Feedback Settings")]
    [SerializeField] private SFXType rangeEnterSFX = SFXType.UI_Click;
    [SerializeField] private float rangeEnterHapticIntensity = 0.3f;
    [SerializeField] private float rangeEnterHapticDuration = 0.1f;
    [SerializeField] private float holdingHapticIntensity = 0.05f;

    #endregion

    #region Internal State

    private bool _isBriefcaseInRange = false;
    private bool _isHandsNearHead = false;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // 씬 시작 시 모든 제스처 존 비활성화 — SubwayStepManager가 해당 Stage 진입 시 활성화
        if (leftBagZone != null)   leftBagZone.gameObject.SetActive(false);
        if (rightBagZone != null)  rightBagZone.gameObject.SetActive(false);
        if (leftChestZone != null)  leftChestZone.gameObject.SetActive(false);
        if (rightChestZone != null) rightChestZone.gameObject.SetActive(false);
    }

    private void Update()
    {
        // 서류가방 ABC 범위 진입 피드백
        bool currentBriefcaseCheck = CheckBagZonesOccupied();
        if (currentBriefcaseCheck && !_isBriefcaseInRange)
            PlayRangeEnterFeedback();
        _isBriefcaseInRange = currentBriefcaseCheck;

        // 넘어진 ABC 범위 진입 피드백
        bool currentHandsCheck = CheckChestZonesOccupied();
        if (currentHandsCheck && !_isHandsNearHead)
            PlayRangeEnterFeedback();
        _isHandsNearHead = currentHandsCheck;

        // 자세 유지 중 연속 햅틱
        bool triggersPressed = CheckTriggersPressed();
        if (triggersPressed && (_isBriefcaseInRange || _isHandsNearHead || useTriggerFailSafe))
            TriggerContinuousHaptic(holdingHapticIntensity);
    }

    #endregion

    #region Public API

    /// <summary>서류가방 ABC 자세가 유효한지 반환함</summary>
    /// <remarks>자세 진입 시 통과. 자세 미인식 시 useTriggerFailSafe ON이면 양 트리거로 폴백 통과</remarks>
    public bool IsBriefcaseABCValid()
    {
        bool inZone = CheckBagZonesOccupied();
        if (inZone) return true;

        // 자세 미인식 — Fail-safe 활성 시 양 트리거 동시 누름으로 폴백 통과
        return useTriggerFailSafe && CheckTriggersPressed();
    }

    /// <summary>넘어진 ABC 자세(머리 보호)가 유효한지 반환함</summary>
    /// <remarks>자세 진입 시 통과. 자세 미인식 시 useTriggerFailSafe ON이면 양 트리거로 폴백 통과</remarks>
    public bool IsFallABCValid()
    {
        bool handsNearHead = CheckChestZonesOccupied();
        if (handsNearHead) return true;

        // 자세 미인식 — Fail-safe 활성 시 양 트리거 동시 누름으로 폴백 통과
        return useTriggerFailSafe && CheckTriggersPressed();
    }

    /// <summary>양손으로 ClimbHandle을 잡고 있는 상태인지 반환함</summary>
    public bool IsHoldingClimbHandle()
    {
        return ClimbHandle.ActiveGrabCount > 1;
    }

    /// <summary>가방 ABC 존을 활성/비활성한다 — Stage 1 진입·종료 시 SubwayStepManager에서 호출</summary>
    public void SetBagZonesActive(bool active)
    {
        if (leftBagZone != null)  leftBagZone.gameObject.SetActive(active);
        if (rightBagZone != null) rightBagZone.gameObject.SetActive(active);
    }

    /// <summary>흉부 ABC 존을 활성/비활성한다 — Stage 6 진입·종료 시 SubwayStepManager에서 호출</summary>
    public void SetChestZonesActive(bool active)
    {
        if (leftChestZone != null)  leftChestZone.gameObject.SetActive(active);
        if (rightChestZone != null) rightChestZone.gameObject.SetActive(active);
    }

    #endregion

    #region Internal Logic

    // 양쪽 가방 핸들링 존 모두 점유 상태인지 확인 — 어느 한쪽이 null이면 false
    private bool CheckBagZonesOccupied()
    {
        if (leftBagZone == null || rightBagZone == null) return false;
        return leftBagZone.IsOccupied && rightBagZone.IsOccupied;
    }

    // 양쪽 흉부 존 모두 점유 상태인지 확인 — 어느 한쪽이 null이면 false
    private bool CheckChestZonesOccupied()
    {
        if (leftChestZone == null || rightChestZone == null) return false;
        return leftChestZone.IsOccupied && rightChestZone.IsOccupied;
    }

    private bool CheckTriggersPressed()
    {
        bool leftTrigger = false;
        bool rightTrigger = false;
        var leftDevices = new List<InputDevice>();
        var rightDevices = new List<InputDevice>();

        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, leftDevices);
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, rightDevices);

        if (leftDevices.Count > 0) { leftDevices[0].TryGetFeatureValue(CommonUsages.trigger, out float val); if (val > triggerThreshold) leftTrigger = true; }
        if (rightDevices.Count > 0) { rightDevices[0].TryGetFeatureValue(CommonUsages.trigger, out float val); if (val > triggerThreshold) rightTrigger = true; }

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
        // NOTE: 진동 정규화 — DataManager 설정값 기반 강도 보정
        float finalIntensity = rawIntensity;
        if (DataManager.Instance != null)
            finalIntensity = DataManager.Instance.GetAdjustedHapticStrength(rawIntensity);

        if (finalIntensity <= 0.01f) return;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);

        foreach (var device in devices)
        {
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0, finalIntensity, duration);
        }
    }

    private void TriggerContinuousHaptic(float intensity)
    {
        TriggerImpulseHaptic(intensity, 0.1f);
    }

    #endregion
}
