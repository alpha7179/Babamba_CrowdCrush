using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;

/// <summary>구역 진입 감지 (Goal / Danger) 및 GameStepManager 신호 전달 컴포넌트</summary>
public class ZoneTrigger : MonoBehaviour
{
    #region Inspector Settings

    [Header("Trigger Settings")]
    [SerializeField] private bool isGoal = true;
    [SerializeField] private bool isDanger = false;
    [Header("Target Settings")]
    [SerializeField] private string playerTag = "Player";
    [Header("Haptic Settings")]
    [SerializeField][Range(0, 1)] private float hapticIntensity = 0.5f;
    [SerializeField] private float hapticDuration = 0.2f;
    [Header("Debug")]
    [SerializeField] private bool isDebug = true;

    #endregion

    #region Unity Lifecycle

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag) || (other.transform.root != null && other.transform.root.CompareTag(playerTag)))
        {
            if (isDebug) Debug.Log($"[ZoneTrigger] Player entered: {gameObject.name}");
            HandlePlayerEnter();
        }
    }

    #endregion

    #region Internal Logic

    private void HandlePlayerEnter()
    {
        TriggerZoneHaptic();

        GameStepManager gsm = FindAnyObjectByType<GameStepManager>();
        if (gsm != null)
        {
            if (isGoal)
            {
                if (isDebug) Debug.Log($"[ZoneTrigger] Goal: {gameObject.name}");
                gsm.SetZoneReached(true);
            }
            else if (isDanger)
            {
                if (isDebug) Debug.Log($"[ZoneTrigger] Danger: {gameObject.name}");
                if (DataManager.Instance != null) DataManager.Instance.AddMistakeCount();
                gsm.ReturnToSavedPosition();
            }
            return;
        }

        // NOTE: 서브웨이 씬 폴백 — GameStepManager 없을 때 SubwayStepManager로 신호 전달
        SubwayStepManager ssm = FindAnyObjectByType<SubwayStepManager>();
        if (ssm != null)
        {
            if (isGoal)
            {
                if (isDebug) Debug.Log($"[ZoneTrigger] Goal(Subway): {gameObject.name}");
                ssm.SetZoneReached(true);
            }
            else if (isDanger)
            {
                if (isDebug) Debug.Log($"[ZoneTrigger] Danger(Subway): {gameObject.name}");
                if (DataManager.Instance != null) DataManager.Instance.AddMistakeCount();
                ssm.ReturnToSavedPosition();
            }
        }
    }

    #endregion

    #region Helpers

    private void TriggerZoneHaptic()
    {
        // NOTE: DataManager의 진동 정규화 적용
        float finalIntensity = hapticIntensity;
        if (DataManager.Instance != null)
        {
            finalIntensity = DataManager.Instance.GetAdjustedHapticStrength(hapticIntensity);
        }

        if (finalIntensity <= 0.01f) return;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);

        foreach (var device in devices)
        {
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0, finalIntensity, hapticDuration);
            }
        }
    }

    #endregion
}
