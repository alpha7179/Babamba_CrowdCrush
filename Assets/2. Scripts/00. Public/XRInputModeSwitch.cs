using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>
/// XR 핸드 트래킹 활성화 여부에 따라 게임 오브젝트를 자동 전환하는 스위처
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>핸드 트래킹 감지 시: HandObjects 활성화, ControllerObjects 비활성화</item>
///   <item>핸드 트래킹 소실 시: HandObjects 비활성화, ControllerObjects 활성화</item>
///   <item>시작 시 기본 상태는 컨트롤러 모드 (Inspector에서 변경 가능)</item>
/// </list>
/// </remarks>
public class XRInputModeSwitch : MonoBehaviour
{
    #region Inspector Settings

    [Header("핸드 트래킹 활성화 시 켜질 오브젝트")]
    [Tooltip("XR 핸드가 트래킹되면 활성화됩니다. (예: 손 모델, 핸드 Ray Interactor)")]
    [SerializeField] private GameObject[] _handObjects;

    [Header("핸드 트래킹 활성화 시 꺼질 오브젝트")]
    [Tooltip("XR 핸드가 트래킹되면 비활성화됩니다. (예: 컨트롤러 모델, 컨트롤러 Ray Interactor)")]
    [SerializeField] private GameObject[] _controllerObjects;

    [Header("설정")]
    [Tooltip("체크 시: 시작 시 컨트롤러 모드로 초기화 (권장)")]
    [SerializeField] private bool _startAsControllerMode = true;
    [SerializeField] private bool _isDebug = false;

    #endregion

    #region Internal State

    private XRHandSubsystem _subsystem;
    private bool _isHandTracking = false;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // NOTE: 서브시스템 초기화 전에 Inspector 설정값 기준으로 초기 상태 적용
        if (_startAsControllerMode)
            ApplyMode(isHandMode: false);
    }

    private void Update()
    {
        if (_subsystem == null)
        {
            var list = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            if (list.Count > 0)
                _subsystem = list[0];
            return;
        }

        // NOTE: isTracked는 이벤트 콜백 밖에서 폴링해도 안전함 (NativeArray 미사용)
        bool nowTracking = _subsystem.leftHand.isTracked || _subsystem.rightHand.isTracked;
        if (nowTracking != _isHandTracking)
        {
            _isHandTracking = nowTracking;
            ApplyMode(isHandMode: _isHandTracking);
        }
    }

    #endregion

    #region Internal Logic

    private void ApplyMode(bool isHandMode)
    {
        if (_isDebug)
            Debug.Log($"[XRInputModeSwitch] {(isHandMode ? "핸드 트래킹" : "컨트롤러")} 모드 전환");

        foreach (var obj in _handObjects)
            if (obj != null) obj.SetActive(isHandMode);

        foreach (var obj in _controllerObjects)
            if (obj != null) obj.SetActive(!isHandMode);
    }

    #endregion
}
