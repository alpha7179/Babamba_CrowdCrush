using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using Unity.XR.CoreUtils;

/// <summary>
/// 지정한 XR 핸드 조인트의 포즈를 따라 이동·회전하는 팔로워
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>손 트리거 콜라이더, 손 이펙트 등 손 모델을 따라가야 하는 오브젝트에 부착</item>
///   <item>위치·회전 오프셋으로 조인트 기준 보정 가능</item>
///   <item>회전 추적 비활성화 시 위치만 추적 (콜라이더 전용 등)</item>
/// </list>
/// </remarks>
public class XRHandJointFollower : MonoBehaviour
{
    #region Inspector Settings

    [Header("트래킹 설정")]
    [Tooltip("추적할 손 (Left / Right)")]
    public Handedness handedness = Handedness.Right;
    [Tooltip("추적할 조인트 (손바닥 중심 추천: Palm)")]
    public XRHandJointID targetJoint = XRHandJointID.Palm;

    [Header("위치·회전 보정")]
    [Tooltip("조인트 기준 로컬 위치 오프셋 (m)")]
    public Vector3 positionOffset = Vector3.zero;
    [Tooltip("조인트 기준 로컬 회전 오프셋 (도)")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("추적 옵션")]
    [Tooltip("체크 해제 시 위치만 추적하고 회전은 고정 (콜라이더 전용 모드)")]
    public bool trackRotation = true;

    #endregion

    #region Internal State

    private XRHandSubsystem _subsystem;
    private XROrigin _xrOrigin;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        _xrOrigin = FindFirstObjectByType<XROrigin>();
    }

    private void OnEnable()
    {
        if (_subsystem != null)
            _subsystem.updatedHands += OnUpdatedHands;
    }

    private void OnDisable()
    {
        if (_subsystem != null)
            _subsystem.updatedHands -= OnUpdatedHands;
    }

    private void Update()
    {
        if (_subsystem == null)
        {
            var list = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(list);
            if (list.Count > 0)
            {
                _subsystem = list[0];
                _subsystem.updatedHands += OnUpdatedHands;
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

        bool isLeft = handedness == Handedness.Left;
        var handFlag = isLeft
            ? XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints
            : XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;

        if ((flags & handFlag) == 0) return;

        var hand = isLeft ? subsystem.leftHand : subsystem.rightHand;
        if (!hand.isTracked) return;

        var joint = hand.GetJoint(targetJoint);
        if (joint.TryGetPose(out Pose pose))
            ApplyPose(pose);
    }

    #endregion

    #region Internal Logic

    private void ApplyPose(Pose pose)
    {
        if (_xrOrigin == null) return;

        Transform trackingSpace = _xrOrigin.CameraFloorOffsetObject != null
            ? _xrOrigin.CameraFloorOffsetObject.transform
            : _xrOrigin.transform;

        if (trackRotation)
        {
            Quaternion worldRot = trackingSpace.rotation * pose.rotation;
            transform.rotation = worldRot * Quaternion.Euler(rotationOffset);
        }

        // 회전 기준으로 오프셋 방향 결정 — trackRotation 비활성 시 현재 회전 유지
        Vector3 worldOffset = transform.rotation * positionOffset;
        transform.position = trackingSpace.TransformPoint(pose.position) + worldOffset;
    }

    #endregion
}
