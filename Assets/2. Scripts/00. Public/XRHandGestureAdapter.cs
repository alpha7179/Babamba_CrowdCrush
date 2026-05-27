using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

/// <summary>
/// XR 핸드 트래킹 제스처를 감지하여 ControllerInputManager 이벤트로 변환하는 어댑터
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>오른손 핀치: 오른손 트리거 이벤트 발행 (팝업 넘기기 / UI 선택)</item>
///   <item>왼손 핀치: Y버튼 이벤트 발행 (일시정지)</item>
///   <item>양손 주먹: 그립 이벤트 발행 (ControllerInputManager 구독자 대상)</item>
///   <item>핸드 트래킹 활성화 시 씬 내 ClimbHandle 근접 감지 자동 전환</item>
/// </list>
/// </remarks>
public class XRHandGestureAdapter : MonoBehaviour
{
    #region Inspector Settings

    [Header("핀치 감지")]
    [Tooltip("핀치 진입 임계 거리 (m) — 엄지 끝과 검지 끝이 이 거리 이하이면 핀치로 인식")]
    [SerializeField] private float _pinchEnterDistance = 0.03f;
    [Tooltip("핀치 해제 임계 거리 (m) — 이 거리 이상으로 벌어지면 핀치 해제")]
    [SerializeField] private float _pinchExitDistance = 0.05f;

    [Header("주먹 감지")]
    [Tooltip("주먹 진입 임계 거리 (m) — 중지·약지·소지 끝의 손바닥 평균 거리")]
    [SerializeField] private float _fistEnterDistance = 0.06f;
    [Tooltip("주먹 해제 임계 거리 (m)")]
    [SerializeField] private float _fistExitDistance = 0.09f;

    [Header("왼손 핀치 홀드 설정")]
    [Tooltip("왼손 핀치를 이 시간(초) 이상 유지해야 일시정지로 인식")]
    [SerializeField] private float _leftPinchHoldDuration = 0.8f;

    [Header("디버그")]
    [SerializeField] private bool _isDebug = false;

    #endregion

    #region Internal State

    private XRHandSubsystem _subsystem;

    private bool _isRightPinching = false;
    private bool _isLeftPinching  = false;
    private bool _isRightFist     = false;
    private bool _isLeftFist      = false;
    private bool _isHandTracking  = false;

    // ClimbHandle 등 외부에서 현재 핸드 트래킹 상태를 즉시 조회하기 위한 전역 상태
    public static bool IsHandTrackingActive { get; private set; } = false;

    // NOTE: 홀드 타이머는 BeforeRender 콜백 밖 Update에서 처리 — deltaTime 누적 대신 시작 시각 비교
    private float _leftPinchStartTime = -1f; // 핀치 시작 시각 (-1 = 비활성)
    private bool _isLeftPinchFired    = false;

    // NOTE: 주먹 판별에 사용할 손끝 조인트 — 검지는 핀치와 중복되므로 제외
    private static readonly XRHandJointID[] FistTipJoints =
    {
        XRHandJointID.MiddleTip,
        XRHandJointID.RingTip,
        XRHandJointID.LittleTip,
    };

    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        if (_subsystem != null)
            _subsystem.updatedHands += OnUpdatedHands;
    }

    private void OnDisable()
    {
        if (_subsystem != null)
            _subsystem.updatedHands -= OnUpdatedHands;

        // 어댑터 비활성화 시 ClimbHandle 근접 감지 해제
        SetClimbHandleProximity(false);
    }

    private void Update()
    {
        // 서브시스템 초기화 및 이벤트 구독
        if (_subsystem == null)
        {
            var list = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            if (list.Count > 0)
            {
                _subsystem = list[0];
                _subsystem.updatedHands += OnUpdatedHands;
            }
            return;
        }

        // 핸드 트래킹 상태 변화 감지 — 양손 중 하나라도 트래킹되면 활성으로 판단
        bool nowTracking = _subsystem.leftHand.isTracked || _subsystem.rightHand.isTracked;
        if (nowTracking != _isHandTracking)
        {
            _isHandTracking = nowTracking;
            OnHandTrackingChanged(_isHandTracking);
        }

        // 왼손 핀치 홀드 타이머 — Update에서 unscaledTime으로 체크 (BeforeRender 밖)
        if (_isLeftPinching && !_isLeftPinchFired && _leftPinchStartTime >= 0f)
        {
            if (Time.unscaledTime - _leftPinchStartTime >= _leftPinchHoldDuration)
            {
                _isLeftPinchFired = true;
                FireLeftPinchHold();
            }
        }
    }

    #endregion

    #region Interaction Events

    // NOTE: updatedHands 콜백 내에서만 XRHandJoint NativeArray가 유효함
    private void OnUpdatedHands(XRHandSubsystem subsystem,
        XRHandSubsystem.UpdateSuccessFlags flags, XRHandSubsystem.UpdateType updateType)
    {
        if (updateType != XRHandSubsystem.UpdateType.BeforeRender) return;

        if ((flags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != 0)
            ProcessHand(subsystem.rightHand, isRight: true);

        if ((flags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != 0)
            ProcessHand(subsystem.leftHand, isRight: false);
    }

    #endregion

    #region Internal Logic

    private void ProcessHand(XRHand hand, bool isRight)
    {
        if (!hand.isTracked) return;

        if (isRight)
        {
            UpdatePinch(hand, ref _isRightPinching, isRight: true);
            // NOTE: 핀치 중에는 주먹 감지 스킵 — 검지 굽힘으로 인한 오발동 방지
            if (!_isRightPinching)
                UpdateFist(hand, ref _isRightFist, isRight: true);
        }
        else
        {
            UpdatePinch(hand, ref _isLeftPinching, isRight: false);
            if (!_isLeftPinching)
                UpdateFist(hand, ref _isLeftFist, isRight: false);
        }
    }

    private void UpdatePinch(XRHand hand, ref bool isPinching, bool isRight)
    {
        var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
        var index = hand.GetJoint(XRHandJointID.IndexTip);

        if (!thumb.TryGetPose(out Pose tPose) || !index.TryGetPose(out Pose iPose)) return;

        float dist = Vector3.Distance(tPose.position, iPose.position);

        if (!isPinching && dist <= _pinchEnterDistance)
        {
            isPinching = true;
            OnPinchStart(isRight);
        }
        else if (isPinching && dist >= _pinchExitDistance)
        {
            isPinching = false;
            OnPinchEnd(isRight);
        }
    }

    private void UpdateFist(XRHand hand, ref bool isFist, bool isRight)
    {
        var palm = hand.GetJoint(XRHandJointID.Palm);
        if (!palm.TryGetPose(out Pose palmPose)) return;

        float total = 0f;
        int count = 0;
        foreach (var id in FistTipJoints)
        {
            var joint = hand.GetJoint(id);
            if (joint.TryGetPose(out Pose tipPose))
            {
                total += Vector3.Distance(tipPose.position, palmPose.position);
                count++;
            }
        }
        if (count == 0) return;

        float avg = total / count;

        if (!isFist && avg <= _fistEnterDistance)
        {
            isFist = true;
            OnFistStart(isRight);
        }
        else if (isFist && avg >= _fistExitDistance)
        {
            isFist = false;
            OnFistEnd(isRight);
        }
    }

    private void OnPinchStart(bool isRight)
    {
        if (_isDebug) Debug.Log($"[XRHandGestureAdapter] {(isRight ? "오른" : "왼")}손 핀치 시작");

        if (isRight)
        {
            if (ControllerInputManager.Instance != null)
                ControllerInputManager.Instance.InvokeRightTriggerDown();
        }
        else
        {
            // 왼손 핀치는 홀드 타이머로 처리 — 시작 시각 기록 후 Update에서 판정
            _leftPinchStartTime = Time.unscaledTime;
            _isLeftPinchFired   = false;
        }
    }

    private void OnPinchEnd(bool isRight)
    {
        if (isRight)
        {
            if (ControllerInputManager.Instance != null)
                ControllerInputManager.Instance.InvokeRightTriggerUp();
        }
        else
        {
            // 핀치 해제 시 타이머 리셋 — 홀드 미달이면 트리거 없이 종료
            _leftPinchStartTime = -1f;
            _isLeftPinchFired   = false;
            if (_isDebug) Debug.Log("[XRHandGestureAdapter] 왼손 핀치 해제 — 홀드 리셋");
        }
    }

    // 홀드 시간 충족 시 실제 Y버튼 이벤트 발행
    private void FireLeftPinchHold()
    {
        if (_isDebug) Debug.Log("[XRHandGestureAdapter] 왼손 핀치 홀드 완료 — 일시정지 발행");
        if (ControllerInputManager.Instance != null)
            ControllerInputManager.Instance.InvokeYButtonDown();
    }

    private void OnFistStart(bool isRight)
    {
        if (_isDebug) Debug.Log($"[XRHandGestureAdapter] {(isRight ? "오른" : "왼")}손 주먹 시작");
        if (ControllerInputManager.Instance == null) return;

        if (isRight)
            ControllerInputManager.Instance.InvokeRightGripDown();
        else
            ControllerInputManager.Instance.InvokeLeftGripDown();
    }

    private void OnFistEnd(bool isRight)
    {
        if (ControllerInputManager.Instance == null) return;

        if (isRight)
            ControllerInputManager.Instance.InvokeRightGripUp();
        else
            ControllerInputManager.Instance.InvokeLeftGripUp();
    }

    private void OnHandTrackingChanged(bool isTracking)
    {
        if (_isDebug) Debug.Log($"[XRHandGestureAdapter] 핸드 트래킹 {(isTracking ? "활성화" : "비활성화")}");
        IsHandTrackingActive = isTracking;
        SetClimbHandleProximity(isTracking);
    }

    // 씬 내 모든 ClimbHandle의 근접 감지를 일괄 전환
    // NOTE: XR 핸드 콜라이더에 ClimbHandle.handTags에 등록된 태그가 있어야 감지됨
    private void SetClimbHandleProximity(bool enable)
    {
        var handles = FindObjectsByType<ClimbHandle>(FindObjectsSortMode.None);
        foreach (var handle in handles)
        {
            if (handle != null)
                handle.SetProximityDetection(enable);
        }
    }

    #endregion
}
