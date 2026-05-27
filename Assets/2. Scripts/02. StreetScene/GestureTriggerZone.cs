using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>제스처 감지 존 — 지정된 손 콜라이더의 진입/퇴장을 추적하는 리포터</summary>
public class GestureTriggerZone : MonoBehaviour
{
    #region Enums
    public enum HandSide { Left, Right }
    #endregion

    #region Inspector Settings
    [Tooltip("감지할 손 콜라이더 목록. 컨트롤러·XR 핸드 콜라이더를 모두 등록하면 어느 쪽이든 감지됩니다.")]
    [SerializeField] private List<Collider> handColliders = new();

    [Header("Haptic Settings")]
    [Tooltip("진입 시 햅틱을 줄 손 방향")]
    [SerializeField] private HandSide hapticHand = HandSide.Left;
    [Tooltip("진입 햅틱 강도 (0.0 ~ 1.0)")]
    [SerializeField][Range(0f, 1f)] private float hapticIntensity = 0.3f;
    [Tooltip("진입 햅틱 지속 시간 (초)")]
    [SerializeField] private float hapticDuration = 0.1f;
    #endregion

    #region Public API
    /// <summary>현재 지정 손 콜라이더가 존 안에 있는지 여부</summary>
    public bool IsOccupied { get; private set; }
    #endregion

    #region Internal State
    private Collider _myCollider;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        _myCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        if (_myCollider == null) return;

        // 등록된 콜라이더 중 하나라도 겹치면 점유로 판정
        bool currentOccupied = false;
        foreach (var col in handColliders)
        {
            if (col != null && _myCollider.bounds.Intersects(col.bounds))
            {
                currentOccupied = true;
                break;
            }
        }

        if (currentOccupied && !IsOccupied)
            TriggerHaptic(); // 진입 순간에만 햅틱

        IsOccupied = currentOccupied;
    }

    private void OnDisable()
    {
        // 비활성화 시 상태 초기화 — 씬 리셋 시 잔여 상태 방지
        IsOccupied = false;
    }
    #endregion

    #region Internal Logic
    private void TriggerHaptic()
    {
        float finalIntensity = hapticIntensity;
        if (DataManager.Instance != null)
            finalIntensity = DataManager.Instance.GetAdjustedHapticStrength(hapticIntensity);
        if (finalIntensity <= 0.01f) return;

        InputDeviceCharacteristics side = hapticHand == HandSide.Left
            ? InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller
            : InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(side, devices);
        if (devices.Count > 0 && devices[0].TryGetHapticCapabilities(out var cap) && cap.supportsImpulse)
            devices[0].SendHapticImpulse(0, finalIntensity, hapticDuration);
    }
    #endregion
}
