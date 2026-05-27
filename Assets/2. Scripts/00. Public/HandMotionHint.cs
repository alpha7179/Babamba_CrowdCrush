using System.Collections;
using UnityEngine;

/// <summary>
/// 손 메쉬 유도 모션 애니메이션을 제어하는 컴포넌트
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>인스펙터에서 시작 위치/회전을 지정하고, 오브젝트의 원래 위치/회전을 끝점으로 이동 애니메이션 재생</item>
///   <item>EasingType으로 이동 곡선 형태 선택 가능</item>
///   <item>FadeMode로 Alpha 기반 페이드 인/아웃 효과 적용 가능 (재질이 Transparent 모드여야 동작)</item>
///   <item>LoopMode로 1회 재생·반복·왕복 선택 가능, 반복 간격도 조절 가능</item>
/// </list>
/// </remarks>
public class HandMotionHint : MonoBehaviour
{
    #region Enums / Constants

    /// <summary>이동 보간 곡선 형태</summary>
    public enum EasingType
    {
        Linear,     // 일정 속도
        EaseIn,     // 느리게 시작 → 빠르게 끝
        EaseOut,    // 빠르게 시작 → 느리게 끝
        EaseInOut,  // 느리게 시작 → 빠르게 중간 → 느리게 끝
    }

    /// <summary>Alpha 페이드 효과 종류</summary>
    public enum FadeMode
    {
        None,       // 페이드 없음
        FadeIn,     // 재생 시작 시 서서히 나타남 (0 → 1)
        FadeOut,    // 재생 끝에서 서서히 사라짐 (1 → 0)
        FadeInOut,  // 시작·끝 모두 페이드 (0 → 1 → 0)
    }

    /// <summary>반복 재생 모드</summary>
    public enum LoopMode
    {
        Once,       // 1회 재생 후 끝점 유지
        Loop,       // 끝점 도달 후 시작점으로 순간 이동하며 반복
        PingPong,   // 시작 → 끝 → 시작 왕복 반복
    }

    #endregion

    #region Inspector Settings

    [Header("시작 위치 / 회전 (로컬 기준)")]
    [Tooltip("애니메이션 시작점의 로컬 포지션")]
    [SerializeField] private Vector3 _startLocalPosition = Vector3.zero;
    [Tooltip("애니메이션 시작점의 로컬 로테이션 (오일러 각도)")]
    [SerializeField] private Vector3 _startLocalRotationEuler = Vector3.zero;

    [Header("이징 / 페이드 설정")]
    [Tooltip("이동 보간 곡선 형태")]
    [SerializeField] private EasingType _easingType = EasingType.EaseInOut;
    [Tooltip("Alpha 페이드 효과 (재질이 Transparent/Fade 렌더링 모드여야 적용됨)")]
    [SerializeField] private FadeMode _fadeMode = FadeMode.None;

    [Header("재생 설정")]
    [Tooltip("애니메이션 1사이클 지속 시간 (초)")]
    [SerializeField] private float _duration = 1.0f;
    [Tooltip("반복 재생 모드")]
    [SerializeField] private LoopMode _loopMode = LoopMode.Loop;
    [Tooltip("Loop / PingPong 사용 시 다음 사이클 전 대기 시간 (초)")]
    [SerializeField] private float _loopInterval = 0.3f;
    [Tooltip("체크 시: Awake에서 자동 재생 시작")]
    [SerializeField] private bool _playOnAwake = true;

    #endregion

    #region Internal State

    // 씬 배치 시점의 로컬 위치·회전 — 끝점으로 사용
    private Vector3 _endLocalPosition;
    private Quaternion _endLocalRotation;
    private Quaternion _startLocalRotation;

    // 페이드 적용 대상 렌더러 캐시
    private Renderer[] _renderers;

    // 현재 재생 코루틴 참조
    private Coroutine _animCoroutine;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // NOTE: Awake 시점의 로컬 트랜스폼을 끝점으로 고정
        CaptureEndState();
        CacheRenderers();
    }

    private void Start()
    {
        if (_playOnAwake)
            Play();
    }

    private void OnDisable()
    {
        StopAnimation();
    }

    #endregion

    #region Initialization

    private void CaptureEndState()
    {
        _endLocalPosition = transform.localPosition;
        _endLocalRotation = transform.localRotation;
        _startLocalRotation = Quaternion.Euler(_startLocalRotationEuler);
    }

    private void CacheRenderers()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    #endregion

    #region Public API

    /// <summary>애니메이션을 처음부터 재생한다.</summary>
    public void Play()
    {
        StopAnimation();
        _animCoroutine = StartCoroutine(CoRunAnimation());
    }

    /// <summary>재생을 중단하고 끝점으로 즉시 이동한다.</summary>
    public void Stop()
    {
        StopAnimation();
        ApplyState(_endLocalPosition, _endLocalRotation, 1f);
    }

    /// <summary>재생을 중단하고 시작점으로 즉시 이동한다.</summary>
    public void ResetToStart()
    {
        StopAnimation();
        ApplyState(_startLocalPosition, _startLocalRotation, GetAlphaAtT(0f));
    }

    /// <summary>현재 재생 중인지 여부를 반환한다.</summary>
    public bool IsPlaying => _animCoroutine != null;

    #endregion

    #region Coroutines

    private IEnumerator CoRunAnimation()
    {
        bool forward = true;

        do
        {
            yield return StartCoroutine(CoPlayOnce(forward));

            if (_loopMode == LoopMode.PingPong)
            {
                // 왕복: 방향 전환
                forward = !forward;
            }
            else if (_loopMode == LoopMode.Loop)
            {
                // 반복: 시작점으로 순간 이동
                ApplyState(_startLocalPosition, _startLocalRotation, GetAlphaAtT(0f));
            }

            if (_loopMode != LoopMode.Once && _loopInterval > 0f)
                yield return new WaitForSeconds(_loopInterval);

        } while (_loopMode != LoopMode.Once);

        _animCoroutine = null;
    }

    private IEnumerator CoPlayOnce(bool forward)
    {
        float elapsed = 0f;

        while (elapsed < _duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / _duration);

            // 왕복 역방향일 때 t를 반전
            float tEval = forward ? t : 1f - t;

            float eased = ApplyEasing(tEval);
            float alpha = GetAlphaAtT(tEval);

            Vector3 pos = Vector3.Lerp(_startLocalPosition, _endLocalPosition, eased);
            Quaternion rot = Quaternion.Slerp(_startLocalRotation, _endLocalRotation, eased);

            ApplyState(pos, rot, alpha);

            yield return null;
        }

        // 정방향이면 끝점, 역방향이면 시작점 정확히 고정
        if (forward)
            ApplyState(_endLocalPosition, _endLocalRotation, GetAlphaAtT(1f));
        else
            ApplyState(_startLocalPosition, _startLocalRotation, GetAlphaAtT(0f));
    }

    #endregion

    #region Internal Logic

    private void StopAnimation()
    {
        if (_animCoroutine != null)
        {
            StopCoroutine(_animCoroutine);
            _animCoroutine = null;
        }
    }

    private void ApplyState(Vector3 localPos, Quaternion localRot, float alpha)
    {
        transform.localPosition = localPos;
        transform.localRotation = localRot;
        ApplyAlpha(alpha);
    }

    private void ApplyAlpha(float alpha)
    {
        if (_fadeMode == FadeMode.None || _renderers == null) return;

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            // NOTE: r.materials는 인스턴스 배열을 반환하므로 직접 수정 후 재할당 불필요
            foreach (var mat in r.materials)
            {
                if (mat != null && mat.HasProperty("_Color"))
                {
                    Color c = mat.color;
                    c.a = alpha;
                    mat.color = c;
                }
            }
        }
    }

    // t(0~1)에 따른 Alpha 값 계산
    private float GetAlphaAtT(float t)
    {
        return _fadeMode switch
        {
            FadeMode.None     => 1f,
            FadeMode.FadeIn   => t,
            FadeMode.FadeOut  => 1f - t,
            FadeMode.FadeInOut => t < 0.5f ? t * 2f : (1f - t) * 2f,
            _                 => 1f,
        };
    }

    // Easing 함수 적용 (2차 곡선 기반)
    private float ApplyEasing(float t)
    {
        return _easingType switch
        {
            EasingType.Linear    => t,
            EasingType.EaseIn    => t * t,
            EasingType.EaseOut   => 1f - (1f - t) * (1f - t),
            EasingType.EaseInOut => t < 0.5f
                                    ? 2f * t * t
                                    : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f,
            _                    => t,
        };
    }

    #endregion

    #region Helpers

#if UNITY_EDITOR
    [ContextMenu("현재 위치를 시작점으로 설정")]
    private void SetCurrentAsStart()
    {
        _startLocalPosition = transform.localPosition;
        _startLocalRotationEuler = transform.localRotation.eulerAngles;
        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log($"[HandMotionHint] 시작점을 현재 로컬 위치로 설정: {_startLocalPosition}");
    }

    // 씬 뷰에서 시작점(파란 구)과 끝점(노란 구) 시각화
    private void OnDrawGizmosSelected()
    {
        Matrix4x4 parentMatrix = transform.parent != null
            ? transform.parent.localToWorldMatrix
            : Matrix4x4.identity;

        Vector3 startWorld = parentMatrix.MultiplyPoint3x4(_startLocalPosition);
        Vector3 endWorld   = transform.position;

        // 시작점
        Gizmos.color = new Color(0f, 0.8f, 1f, 0.9f);
        Gizmos.DrawWireSphere(startWorld, 0.04f);
        Gizmos.DrawLine(startWorld, endWorld);

        // 끝점 (현재 위치)
        Gizmos.color = new Color(1f, 0.9f, 0f, 0.9f);
        Gizmos.DrawWireSphere(endWorld, 0.04f);

        UnityEditor.Handles.Label(startWorld + Vector3.up * 0.06f, "Start");
        UnityEditor.Handles.Label(endWorld   + Vector3.up * 0.06f, "End");
    }
#endif

    #endregion
}
