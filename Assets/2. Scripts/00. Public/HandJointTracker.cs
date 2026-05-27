using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using Unity.XR.CoreUtils;

public class HandRayCustomizer : MonoBehaviour
{
    [Header("트래킹 설정")]
    public Handedness handedness = Handedness.Right;
    public XRHandJointID originJoint = XRHandJointID.Palm; // 시작점 (손바닥 추천)

    [Header("레이 위치 및 방향 보정")]
    [Tooltip("손바닥으로부터 얼마나 띄울지 설정합니다. (Z값을 높이면 손바닥 앞쪽으로 발사됩니다)")]
    public Vector3 positionOffset = new Vector3(0, 0, 0.05f); // 기본 5cm 앞쪽
    public Vector3 rotationOffset = Vector3.zero;

    [Header("핀치 락 (흔들림 방지)")]
    public bool usePinchLock = true;
    public float lockDistance = 0.035f;
    public float unlockDistance = 0.05f;

    private XRHandSubsystem m_Subsystem;
    private XROrigin xrOrigin;
    private bool isLocked = false;

    void Start()
    {
        xrOrigin = FindFirstObjectByType<XROrigin>();
    }

    void OnEnable()
    {
        // 서브시스템 이벤트 구독 — updatedHands 콜백 내에서만 Joint 데이터 접근
        if (m_Subsystem != null)
            m_Subsystem.updatedHands += OnUpdatedHands;
    }

    void OnDisable()
    {
        if (m_Subsystem != null)
            m_Subsystem.updatedHands -= OnUpdatedHands;
    }

    void Update()
    {
        // 서브시스템 초기화 및 이벤트 재구독
        if (m_Subsystem == null)
        {
            List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            if (subsystems.Count > 0)
            {
                m_Subsystem = subsystems[0];
                m_Subsystem.updatedHands += OnUpdatedHands;
            }
        }
    }

    // NOTE: updatedHands 이벤트 콜백 내에서만 NativeArray가 유효함
    private void OnUpdatedHands(XRHandSubsystem subsystem,
        XRHandSubsystem.UpdateSuccessFlags updateSuccessFlags, XRHandSubsystem.UpdateType updateType)
    {
        // BeforeRender 타이밍에만 처리 — 렌더 직전 최신 포즈 반영
        if (updateType != XRHandSubsystem.UpdateType.BeforeRender) return;

        bool isLeft = handedness == Handedness.Left;
        bool handUpdated = isLeft
            ? (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints) != 0
            : (updateSuccessFlags & XRHandSubsystem.UpdateSuccessFlags.RightHandJoints) != 0;

        if (!handUpdated) return;

        var hand = isLeft ? subsystem.leftHand : subsystem.rightHand;
        if (!hand.isTracked) return;

        // 1. 핀치 락 체크
        UpdatePinchLock(hand);

        // 2. 락이 걸리지 않았을 때만 위치 업데이트
        if (!isLocked)
        {
            var joint = hand.GetJoint(originJoint);
            if (joint.TryGetPose(out Pose pose))
            {
                UpdateRayTransform(pose);
            }
        }
    }

    private void UpdatePinchLock(XRHand hand)
    {
        if (!usePinchLock) { isLocked = false; return; }

        var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
        var index = hand.GetJoint(XRHandJointID.IndexTip);

        if (thumb.TryGetPose(out Pose tPose) && index.TryGetPose(out Pose iPose))
        {
            float dist = Vector3.Distance(tPose.position, iPose.position);
            if (!isLocked && dist <= lockDistance) isLocked = true;
            else if (isLocked && dist >= unlockDistance) isLocked = false;
        }
    }

    private void UpdateRayTransform(Pose pose)
    {
        if (xrOrigin == null) return;

        Transform trackingSpace = xrOrigin.CameraFloorOffsetObject != null 
            ? xrOrigin.CameraFloorOffsetObject.transform 
            : xrOrigin.transform;

        // 월드 회전 먼저 계산
        Quaternion worldRot = trackingSpace.rotation * pose.rotation;
        transform.rotation = worldRot * Quaternion.Euler(rotationOffset);

        // 오프셋 적용: 회전값에 따라 positionOffset 방향을 계산하여 더해줌
        Vector3 worldOffset = transform.rotation * positionOffset;
        transform.position = trackingSpace.TransformPoint(pose.position) + worldOffset;
    }
}