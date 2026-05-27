using UnityEngine;

/// <summary>
/// forearm.L과 hand.L 사이를 보간해 시계 오브젝트의 위치·회전·크기를 자연스럽게 제어하는 컴포넌트
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>위치: forearm.L → hand.L 사이를 PositionBlend 비율로 보간</item>
///   <item>회전: forearm.L · hand.L 회전을 RotationBlend 비율로 Slerp — 손목 비틀림을 완충</item>
///   <item>크기: ScaleOverride로 직접 지정 — (1,1,1)이면 원본 유지</item>
///   <item>LateUpdate()에서 실행 — IK 연산 이후 최종 적용</item>
/// </list>
/// </remarks>
public class WatchWristFollow : MonoBehaviour
{
    #region Inspector Settings

    [Header("VR ARM IK 본 참조")]
    [Tooltip("VR ARM IK 프리팹의 forearm.L Transform")]
    [SerializeField] private Transform _forearmBone;

    [Tooltip("VR ARM IK 프리팹의 hand.L Transform")]
    [SerializeField] private Transform _handBone;

    [Header("보간 비율")]
    [Tooltip("위치 보간. 0 = forearm 위치, 1 = hand 위치. 손목 중앙은 0.85 권장.")]
    [SerializeField, Range(0f, 1f)] private float _positionBlend = 0.85f;

    [Tooltip("회전 보간 활성화. 끄면 회전을 forearm.L 기준으로 고정.")]
    [SerializeField] private bool _enableRotationBlend = true;

    [Tooltip("회전 보간. 0 = forearm 회전(비틀림 없음), 1 = hand 회전(완전 추종). 0.4 권장.")]
    [SerializeField, Range(0f, 1f)] private float _rotationBlend = 0.4f;

    [Header("오프셋")]
    [Tooltip("보간된 기준 위치에서의 로컬 위치 오프셋 (미터 단위)")]
    [SerializeField] private Vector3 _positionOffset = Vector3.zero;

    [Tooltip("회전 오프셋 (오일러 각도)")]
    [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

    [Tooltip("시계 크기. (1, 1, 1)이면 원본 스케일 유지.")]
    [SerializeField] private Vector3 _scaleOverride = Vector3.one;

    [Header("스무딩")]
    [Tooltip("위치 스무딩 속도. 0이면 즉시 적용.")]
    [SerializeField, Range(0f, 30f)] private float _positionSmoothSpeed = 20f;

    [Tooltip("회전 스무딩 속도. 0이면 즉시 적용.")]
    [SerializeField, Range(0f, 30f)] private float _rotationSmoothSpeed = 15f;

    #endregion

    #region Internal State

    private Vector3 _smoothedPosition;
    private Quaternion _smoothedRotation;
    private bool _initialized;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // 크기는 런타임 중 변하지 않으므로 Start에서 한 번만 적용
        transform.localScale = _scaleOverride;
    }

    private void LateUpdate()
    {
        if (_forearmBone == null || _handBone == null) return;

        Quaternion blendedRot = Quaternion.Slerp(_forearmBone.rotation, _handBone.rotation, _positionBlend);
        Vector3 targetPosition = Vector3.Lerp(_forearmBone.position, _handBone.position, _positionBlend)
                                 + blendedRot * _positionOffset;

        // 회전 보간 비활성 시 hand 회전으로 고정
        Quaternion baseRotation = _enableRotationBlend
            ? Quaternion.Slerp(_forearmBone.rotation, _handBone.rotation, _rotationBlend)
            : _handBone.rotation;
        Quaternion targetRotation = baseRotation * Quaternion.Euler(_rotationOffset);

        // 첫 프레임은 즉시 적용 — 순간 이동 방지
        if (!_initialized)
        {
            _smoothedPosition = targetPosition;
            _smoothedRotation = targetRotation;
            _initialized = true;
        }

        _smoothedPosition = _positionSmoothSpeed > 0f
            ? Vector3.Lerp(_smoothedPosition, targetPosition, _positionSmoothSpeed * Time.deltaTime)
            : targetPosition;

        _smoothedRotation = _rotationSmoothSpeed > 0f
            ? Quaternion.Slerp(_smoothedRotation, targetRotation, _rotationSmoothSpeed * Time.deltaTime)
            : targetRotation;

        transform.position = _smoothedPosition;
        transform.rotation = _smoothedRotation;
    }

    #endregion

#if UNITY_EDITOR
    #region Editor Gizmos

    // NOTE: 에디터 전용 — 보간 기준점과 오프셋 적용 위치를 씬 뷰에서 확인
    private void OnDrawGizmosSelected()
    {
        if (_forearmBone == null || _handBone == null) return;

        // forearm → hand 연결선
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(_forearmBone.position, _handBone.position);

        // 보간 기준점 (위치 기준)
        Vector3 blendPos = Vector3.Lerp(_forearmBone.position, _handBone.position, _positionBlend);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(blendPos, 0.005f);

        // 오프셋 적용 후 최종 목표 위치
        Quaternion blendRot = Quaternion.Slerp(_forearmBone.rotation, _handBone.rotation, _positionBlend);
        Vector3 finalPos = blendPos + blendRot * _positionOffset;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(finalPos, 0.008f);
        Gizmos.DrawLine(blendPos, finalPos);
    }

    #endregion
#endif
}
