using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>등반 가능 오브젝트의 잡기 상태를 관리하는 컴포넌트</summary>
/// <remarks>
/// <list type="bullet">
///   <item>그립 버튼 Select 기반 잡기 (메인)</item>
///   <item>영역 진입 기반 근접 감지 (Inspector 토글, 부수)</item>
///   <item>이중 카운트 방지 — 동일 손이 그립+근접 동시 감지 시 1회만 집계</item>
/// </list>
/// </remarks>
public class ClimbHandle : UnityEngine.XR.Interaction.Toolkit.Locomotion.Climbing.ClimbInteractable
{
    #region Inspector Settings
    [Header("Haptic Settings")]
    [Tooltip("잡았을 때 진동의 세기 (0.0 ~ 1.0)")]
    [SerializeField][Range(0, 1)] private float hapticIntensity = 0.5f;
    [Tooltip("잡았을 때 진동의 지속 시간 (초)")]
    [SerializeField] private float hapticDuration = 0.1f;

    [Header("Proximity Detection")]
    [Tooltip("체크 시: 그립 없이 해당 영역에 손을 넣는 것만으로도 잡기로 인식합니다.")]
    [SerializeField] private bool useProximityDetection = false;
    [Tooltip("근접 감지 대상 태그 목록. 왼손/오른손 태그를 각각 입력합니다.")]
    [SerializeField] private string[] handTags = { "Left Gesture Trigger", "Right Gesture Trigger" };
    #endregion

    #region Global State
    // 씬 내 현재 잡힌 핸들 수 — OnSelectEntered/Exited 및 근접 감지에서 증감
    public static int ActiveGrabCount = 0;

    // 근접 감지 손 콜라이더별 진입한 ClimbHandle 존 수 — 여러 존 중복 카운트 방지
    // key: 손 콜라이더, value: 현재 진입 중인 ClimbHandle 존 개수
    private static readonly Dictionary<Collider, int> _globalProximityRefCount = new();

    /// <summary>씬 시작 또는 시나리오 재시작 시 전역 카운터를 초기화한다.</summary>
    public static void ResetGrabCount()
    {
        ActiveGrabCount = 0;
        _globalProximityRefCount.Clear();
    }
    #endregion

    #region Internal State
    // 이 인스턴스의 트리거 존에 진입한 손 콜라이더 추적 — Exit 짝 맞춤용
    private readonly HashSet<Collider> _proximityHands = new();
    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        // 활성화 시점에 핸드 트래킹 상태를 즉시 반영 — 미션 시작 타이밍 대응
        useProximityDetection = XRHandGestureAdapter.IsHandTrackingActive;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        // NOTE: 비활성화 시 잡고 있었다면 카운트 보정
        if (isSelected)
        {
            DecreaseGrabCount();
        }
        // 근접 감지로 등록된 손 전역 참조 카운트 감소 후 해제
        foreach (var hand in _proximityHands)
            ReleaseProximityHand(hand);
        _proximityHands.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!useProximityDetection) return;
        if (!IsHandTag(other)) return;

        // NOTE: 이미 그립으로 Select 중인 콜라이더와 동일 손이면 카운트 제외
        if (IsAlreadyGrabbedByCollider(other)) return;

        // 이 인스턴스 존에 이미 등록된 경우 무시
        if (!_proximityHands.Add(other)) return;

        // 전역 참조 카운트 증가 — 처음 진입한 손이면 ActiveGrabCount 증가
        if (!_globalProximityRefCount.ContainsKey(other))
        {
            _globalProximityRefCount[other] = 0;
            ActiveGrabCount++;
        }
        _globalProximityRefCount[other]++;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!useProximityDetection) return;

        if (_proximityHands.Remove(other))
        {
            ReleaseProximityHand(other);
        }
    }

    #endregion

    #region Public API

    /// <summary>근접 감지 모드를 전환함. XR 핸드 트래킹 활성화·비활성화 시 호출.</summary>
    public void SetProximityDetection(bool enable)
    {
        useProximityDetection = enable;

        // 비활성화 시 진입 중인 손 콜라이더 정리
        if (!enable)
        {
            foreach (var hand in _proximityHands)
                ReleaseProximityHand(hand);
            _proximityHands.Clear();
        }
    }

    #endregion

    #region Interaction Events

    protected override void OnSelectEntered(SelectEnterEventArgs args)
    {
        base.OnSelectEntered(args);
        ActiveGrabCount++;
        TriggerHaptic(args.interactorObject);
    }

    protected override void OnSelectExited(SelectExitEventArgs args)
    {
        base.OnSelectExited(args);
        DecreaseGrabCount();
    }

    #endregion

    #region Internal Logic

    private void DecreaseGrabCount()
    {
        ActiveGrabCount--;
        if (ActiveGrabCount < 0) ActiveGrabCount = 0;
    }

    // 전역 참조 카운트 감소 — 모든 존에서 빠져나간 손이면 ActiveGrabCount 감소
    private static void ReleaseProximityHand(Collider hand)
    {
        if (!_globalProximityRefCount.ContainsKey(hand)) return;
        _globalProximityRefCount[hand]--;
        if (_globalProximityRefCount[hand] <= 0)
        {
            _globalProximityRefCount.Remove(hand);
            // 전역 카운트 직접 감소 (0 미만 방지)
            ActiveGrabCount--;
            if (ActiveGrabCount < 0) ActiveGrabCount = 0;
        }
    }

    // handTags 배열 중 하나라도 일치하면 손으로 인식.
    // 배열이 비어있으면 태그 무관하게 모든 콜라이더를 손으로 인식 (공간 분리 구성 시 사용).
    private bool IsHandTag(Collider other)
    {
        if (handTags == null || handTags.Length == 0) return true;
        foreach (var tag in handTags)
            if (!string.IsNullOrEmpty(tag) && other.CompareTag(tag)) return true;
        return false;
    }

    // 근접 진입한 콜라이더와 동일한 GameObject 위의 Interactor가 이미 Select 중인지 확인
    private bool IsAlreadyGrabbedByCollider(Collider other)
    {
        foreach (var interactor in interactorsSelecting)
        {
            if (interactor is MonoBehaviour mb && mb.gameObject == other.gameObject)
                return true;
            // NOTE: Interactor가 손 오브젝트의 부모일 경우를 대비해 부모도 비교
            if (interactor is MonoBehaviour mb2 && other.transform.IsChildOf(mb2.transform))
                return true;
        }
        return false;
    }

    private void TriggerHaptic(IXRSelectInteractor interactor)
    {
        // NOTE: DataManager의 햅틱 강도 정규화 적용
        float finalIntensity = hapticIntensity;
        if (DataManager.Instance != null)
        {
            finalIntensity = DataManager.Instance.GetAdjustedHapticStrength(hapticIntensity);
        }

        if (finalIntensity <= 0.01f) return;

        if (interactor is XRBaseInputInteractor inputInteractor)
        {
            inputInteractor.SendHapticImpulse(finalIntensity, hapticDuration);
        }
    }
    #endregion
}
