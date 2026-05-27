using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;

/// <summary>
/// 지하철 씬 시나리오 흐름을 코루틴으로 순차 진행하는 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>SubwayPhase 기반 시나리오 단계 관리</item>
///   <item>지시·피드백·퀴즈 패널 및 내레이션 순차 표시</item>
///   <item>압박 게이지·이동 잠금 상태 제어</item>
///   <item>SubwayZoneTrigger 연동 목표 지점 활성화</item>
/// </list>
/// </remarks>
public class SubwayStepManager : MonoBehaviour
{
    #region Inspector Settings (References)

    [Header("Player References")]
    [SerializeField] private Transform PlayerTransform;

    [Header("Linked Managers")]
    [SerializeField] private SubwayIngameUIManager uiManager;
    [SerializeField] private SubwayGestureManager gestureManager;

    [Header("Zone Objects")]
    [SerializeField] private GameObject[] TargetZone;

    #endregion

    #region Inspector Settings (Game Logic)

    [Header("Action Settings")]
    [Tooltip("ABC 자세류 유지 시간 (초) — Stage 1 서류가방 ABC, Stage 6 넘어진 ABC 공용.")]
    [SerializeField] private float ABCHoldTime = 3.0f;
    [Tooltip("잡기류 유지 시간 (초) — Stage 4 기둥 잡기.")]
    [SerializeField] private float grabHoldTime = 3.0f;

    [Header("Timing Settings")]
    [SerializeField] private float phaseTime = 60.0f;
    [SerializeField] private float instructionDuration = 5.0f;
    [SerializeField] private float feedbackDuration = 5.0f;
    [SerializeField] private float nextStepDuration = 1.0f;
    [Tooltip("A버튼 대기 패널의 자동 스킵 제한 시간 (초). 이 시간이 지나면 A버튼 없이 자동으로 닫힘.")]
    [SerializeField] private float waitForInputTimeout = 10.0f;
    [Header("Guide Settings")]
    [Tooltip("유도 오브젝트 17개. 0=0-1.ToABCZone, 1=1.ABCHint, 2=1-2.ToQuizZone, 3=2.QuizHint, 4=2-3.ToFlowStart, 5=3.ToCorrectMove(1), 6=3-4.ToPillarZone, 7=4.PillarHint, 8=4-5.ToStairZone, 9=5.StairHint, 10=5-6.ToCrouchZone, 11=6.CrouchHint, 12=6-7.ToTicketgateStart, 13=7.ToCorrectMove(2), 14=7-8.ToReportZone, 15=8.ReportHint, 16=8-9.ToEscapeZone")]
    [SerializeField] private GameObject[] guideObjects;
    [Tooltip("유도 오브젝트 표시까지 대기 시간 (초). 이 시간 안에 미션 미완료 시 유도선 표시.")]
    [SerializeField] private float guideShowDelay = 120f;

    [Header("Escalator Settings")]
    [SerializeField] private Transform escalatorStartPoint;
    [SerializeField] private Transform escalatorEndPoint;
    [SerializeField] private float escalatorDuration = 10.0f;

    [Header("Stair Teleport Settings")]
    [Tooltip("계단 난간 잡기 성공 시 순간이동할 목표 좌표.")]
    [SerializeField] private Transform stairTeleportPoint;

    [Header("Fall Settings")]
    [Tooltip("Stage 6 넘어짐 연출 시 PlayerTransform.localPosition.y의 도착 절대값. 기본 자세(y=0) 기준 음수일수록 더 깊게 하강.")]
    [SerializeField] private float fallTargetY = -1.2f;

    #endregion

    #region Inspector Settings (Subway Objects)

    [Header("Subway Objects")]
    [SerializeField] private GameObject briefcaseObject;
    [SerializeField] private GameObject smartphoneObject;
    [Tooltip("지하철 출입문 — Stage 1 서류가방 ABC 자세 성공 시 OpenDoor() 호출, OnTriggerExit으로 자동 CloseDoor()")]
    [SerializeField] private SubwayDoor subwayDoor;
    [Header("Briefcase Drop Settings")]
    [Tooltip("플레이어 HMD(Main Camera) Transform. 비워두면 Camera.main으로 자동 탐색")]
    [SerializeField] private Transform headTransform;
    [Tooltip("낙하 연출 시 플레이어 시선 기준 초기 속도. Z=바라보는 앞, Y=위 — 값을 키우면 더 멀리 날아감")]
    [SerializeField] private Vector3 briefcaseThrowVelocity = new Vector3(0f, 0.5f, 2f);

    #endregion

    #region Internal State

    /// <summary>지하철 씬 시나리오 진행 단계</summary>
    public enum SubwayPhase
    {
        /// <summary>Stage 0 — 주의사항·상황 설명</summary>
        Caution,
        /// <summary>Stage 1 — 서류가방 ABC 자세</summary>
        ABCPose_Briefcase,
        /// <summary>Stage 2 — 물건 낙하 퀴즈</summary>
        BriefcaseQuiz,
        /// <summary>Stage 3 — 흐름 따라 이동</summary>
        FlowMove,
        /// <summary>Stage 4 — 기둥 잡기</summary>
        HoldPillar,
        /// <summary>Stage 5 — 계단 이동</summary>
        StairMove,
        /// <summary>Stage 6 — 넘어짐 ABC 자세</summary>
        FallABCPose,
        /// <summary>Stage 7 — 개찰구 통과</summary>
        TicketGate,
        /// <summary>Stage 8 — 119 신고</summary>
        Emergency119,
        /// <summary>Stage 9 — 에스컬레이터 탈출</summary>
        EscalatorEscape,
        /// <summary>시나리오 완료</summary>
        Finished,
        /// <summary>미설정 상태 (기본값)</summary>
        Null
    }

    /// <summary>피드백 유형 — 긍정/부정/복귀 분기</summary>
    public enum FeedbackKind
    {
        /// <summary>성공 피드백 — Success SFX + 강한 햅틱</summary>
        Positive,
        /// <summary>실패 피드백 — Fail SFX + 약한 햅틱</summary>
        Negative,
        /// <summary>복귀 피드백 — 잘못된 구역 진입 후 원위치 복귀 안내</summary>
        Return
    }

    [Header("Debug Info")]
    [SerializeField] private SubwayPhase currentPhase;

    private bool isZoneReached = false;
    private bool isActionCompleted = false;
    private bool isQuizAnswered = false;
    private bool isQuizCorrect = false;
    private float currentActionHoldTimer = 0f;
    private int targetIndex;
    private Vector3 startPosition;
    // NOTE: FallDownRoutine 시작 직전 localPosition.y 기록 — StandUpRoutine에서 정확한 원위치 복귀에 사용
    private float _standingPositionY;

    // NOTE: ReturnToSavedPosition BUG 수정 — 코루틴 참조 변수로 관리하여 중복 실행 방지
    private Coroutine _returnRoutine;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        // Caution 패널 확인 전까지 플레이어 조작 불가 — 이동·상호작용 잠금
        // NOTE: 세션 초기화는 ScenarioRoutine 진입 시 일괄 처리 — GameStepManager 패턴과 통일
        if (PlayerManager.Instance != null)
        {
            PlayerManager.Instance.SetLocomotion(false);
            PlayerManager.Instance.SetInteraction(false);
        }
        StartCoroutine(ScenarioRoutine());
    }

    #endregion

    #region Public API

    /// <summary>목표 존 도달 상태를 설정한다.</summary>
    /// <param name="reached">도달 여부</param>
    public void SetZoneReached(bool reached) { isZoneReached = reached; }

    /// <summary>현재 플레이어 위치를 복귀 기준점으로 저장한다.</summary>
    public void SavePlayerPosition() { if (PlayerTransform != null) startPosition = PlayerTransform.position; }

    /// <summary>저장된 위치로 플레이어를 복귀시킨다.</summary>
    /// <remarks>BUG 수정: StopCoroutine에 새 IEnumerator 전달 시 기존 코루틴 미중단 — _returnRoutine 참조 변수로 관리</remarks>
    public void ReturnToSavedPosition()
    {
        if (_returnRoutine != null) StopCoroutine(_returnRoutine);
        _returnRoutine = StartCoroutine(ReturnToSavedPositionRoutine());
    }

    /// <summary>퀴즈 선택 결과를 전달한다.</summary>
    /// <param name="isCorrect">정답 여부</param>
    public void SetQuizAnswer(bool isCorrect)
    {
        isQuizCorrect = isCorrect;
        isQuizAnswered = true;
    }

    /// <summary>퀴즈 정답 버튼 onClick에서 직접 호출 — Inspector 등록용</summary>
    public void OnQuizAnswerCorrect()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.UI_Click);
        SetQuizAnswer(true);
    }

    /// <summary>퀴즈 오답 버튼 onClick에서 직접 호출 — Inspector 등록용</summary>
    public void OnQuizAnswerWrong()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.UI_Click);
        SetQuizAnswer(false);
    }

    #endregion

    #region Haptic & Audio Helpers

    // 컨트롤러 햅틱 임펄스 발생 — DataManager 진동 강도 보정 적용
    private void TriggerHaptic(float rawAmplitude, float duration)
    {
        float finalAmplitude = rawAmplitude;
        if (DataManager.Instance != null) finalAmplitude = DataManager.Instance.GetAdjustedHapticStrength(rawAmplitude);
        if (finalAmplitude <= 0.01f) return;
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, devices);
        foreach (var device in devices) if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse) device.SendHapticImpulse(0, finalAmplitude, duration);
    }

    // SubwayPhase별 앰비언스 트랙 전환 — 압박 단계별 군중 소음 강도 조절
    private void UpdateAmbience(SubwayPhase phase)
    {
        if (AudioManager.Instance == null) return;
        // 지하철 씬 단계별 군중 앰비언스 전환 (0: 일상, 1: 혼잡, 2: 위기)
        switch (phase)
        {
            case SubwayPhase.Caution:
            case SubwayPhase.ABCPose_Briefcase:
            case SubwayPhase.BriefcaseQuiz:
                AudioManager.Instance.PlayAMB(AMBType.Crowd, 0); break;
            case SubwayPhase.FlowMove:
            case SubwayPhase.HoldPillar:
            case SubwayPhase.StairMove:
            case SubwayPhase.FallABCPose:
                AudioManager.Instance.PlayAMB(AMBType.Crowd, 1); break;
            case SubwayPhase.TicketGate:
            case SubwayPhase.Emergency119:
            case SubwayPhase.EscalatorEscape:
                AudioManager.Instance.PlayAMB(AMBType.Crowd, 2); break;
        }
    }

    #endregion

    #region UI & Logic Helper Coroutines

    // 지시 패널을 열고 내레이션을 재생한다.
    // 패널이 닫히거나 instructionDuration 경과 시 반환.
    // 반환 시까지 이동 잠금 상태를 유지한다.
    private IEnumerator ShowStepTextAndDelay(int instructionIndex, SubwayPhase phase, int narIndex = 0)
    {
        // 지시 패널 표시 중 이동 잠금 — 조작 혼선 방지
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);

        if (uiManager) { uiManager.CloseFeedBack(); uiManager.UpdateInstruction(instructionIndex); uiManager.OpenInstructionPanel(); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(phase, narIndex);

        float timer = 0f;
        while (uiManager != null && uiManager.GetDisplayPanel() && timer < instructionDuration) { timer += Time.deltaTime; yield return null; }
        if (uiManager && uiManager.GetDisplayPanel()) uiManager.CloseInstructionPanel();
    }

    // 피드백 패널을 열고 결과 내레이션을 재생한다.
    // kind = Positive 성공 / Negative 실패 / Return 복귀 — UI 텍스트 배열·SFX·햅틱 강도 분기.
    // waitForInput = true 이면 플레이어가 A버튼으로 닫을 때까지 패널 유지 (타이머 없음).
    // 반환 시까지 이동 잠금 상태를 유지한다.
    private IEnumerator ShowFeedbackAndDelay(int feedbackIndex, SubwayPhase phase, FeedbackKind kind = FeedbackKind.Positive, int narIndex = 1, bool waitForInput = false)
    {
        // 피드백 표시 중 이동 잠금 — 결과 확인 전까지 조작 불가
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);

        if (uiManager)
        {
            uiManager.CloseInstruction();
            switch (kind)
            {
                case FeedbackKind.Positive: uiManager.UpdateFeedBack(feedbackIndex); break;
                case FeedbackKind.Negative: uiManager.UpdateNegativeFeedback(feedbackIndex); break;
                case FeedbackKind.Return:   uiManager.UpdateReturnFeedback(feedbackIndex); break;
            }
            uiManager.OpenInstructionPanel();
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(phase, narIndex);
        if (AudioManager.Instance != null)
        {
            // 부정만 Fail SFX — 복귀는 안도감 표현으로 Success SFX 재사용
            SFXType sfx = (kind == FeedbackKind.Negative) ? SFXType.Fail_Feedback : SFXType.Success_Feedback;
            AudioManager.Instance.PlaySFX(sfx);
        }
        // 강도 — 성공: 강함 / 부정·복귀: 약함
        if (kind == FeedbackKind.Positive) TriggerHaptic(0.8f, 0.3f);
        else TriggerHaptic(0.4f, 0.1f);

        if (waitForInput)
        {
            // A버튼 클릭 대기 — waitForInputTimeout 초 경과 시 자동 스킵
            yield return StartCoroutine(WaitForPanelClose());
        }
        else
        {
            float timer = 0f;
            while (uiManager != null && uiManager.GetDisplayPanel() && timer < feedbackDuration) { timer += Time.deltaTime; yield return null; }
            if (uiManager && uiManager.GetDisplayPanel()) uiManager.CloseInstructionPanel();
        }
    }

    // guideShowDelay 초 동안 completionCondition 미충족 시 guideIndex 유도 오브젝트를 표시함.
    // 조건 충족(미션 완료) 시 오브젝트를 비활성화하고 종료.
    // guideIndex가 배열 범위 밖이거나 오브젝트가 null이면 즉시 반환.
    private IEnumerator GuideRoutine(int guideIndex, Func<bool> completionCondition)
    {
        if (guideObjects == null || guideIndex < 0 || guideIndex >= guideObjects.Length || guideObjects[guideIndex] == null)
            yield break;

        float timer = 0f;
        while (!completionCondition.Invoke() && timer < guideShowDelay) { timer += Time.deltaTime; yield return null; }

        if (!completionCondition.Invoke())
        {
            guideObjects[guideIndex].SetActive(true);
            yield return new WaitUntil(completionCondition);
            guideObjects[guideIndex].SetActive(false);
        }
    }

    // 모든 유도 오브젝트를 비활성화함 — 씬 리셋 또는 시나리오 종료 시 호출
    private void HideAllGuides()
    {
        if (guideObjects == null) return;
        foreach (var guide in guideObjects)
            if (guide != null) guide.SetActive(false);
    }

    // 지시/피드백 패널이 닫힐 때까지 대기
    // 종료 조건 — 다음 중 어느 하나 충족 시:
    //   ① 사용자 입력으로 SetDisplayPanel(false) 호출 (R트리거/A버튼)
    //   ② NAR 종료 후 waitForInputTimeout 경과 (NAR 없는 경우 즉시 카운트)
    // NOTE: NAR 재생 중엔 timer를 리셋해 자동 종료 방지 — NAR 끝까지 안내 보장
    private IEnumerator WaitForPanelClose()
    {
        yield return null; // 1프레임 대기 — NAR 재생 시작 보장
        float safetyTimer = 0f;
        while (uiManager != null && uiManager.GetDisplayPanel())
        {
            bool narPlaying = AudioManager.Instance != null && AudioManager.Instance.IsNARPlaying();

            if (narPlaying)
            {
                safetyTimer = 0f; // NAR 재생 중 — timer 리셋, 자동 종료 보류
            }
            else
            {
                safetyTimer += Time.deltaTime;
                if (safetyTimer >= waitForInputTimeout) break; // NAR 종료 후(또는 NAR 부재) timeout 경과 시 자동 종료
            }
            yield return null;
        }
        if (uiManager && uiManager.GetDisplayPanel()) uiManager.CloseInstructionPanel();
    }

    // 상황 설명 패널이 닫힐 때까지 대기
    // 종료 조건 — 다음 중 어느 하나 충족 시:
    //   ① 사용자 입력으로 SetDisplayPanel(false) 호출 (R트리거/A버튼)
    //   ② NAR 종료 후 waitForInputTimeout 경과 (NAR 없는 경우 즉시 카운트)
    // NOTE: NAR 재생 중엔 timer를 리셋해 자동 종료 방지 — NAR 끝까지 안내 보장
    private IEnumerator WaitForSituationPanelClose()
    {
        yield return null; // 1프레임 대기 — NAR 재생 시작 보장
        float safetyTimer = 0f;
        while (uiManager != null && uiManager.GetDisplayPanel())
        {
            bool narPlaying = AudioManager.Instance != null && AudioManager.Instance.IsNARPlaying();

            if (narPlaying)
            {
                safetyTimer = 0f; // NAR 재생 중 — timer 리셋, 자동 종료 보류
            }
            else
            {
                safetyTimer += Time.deltaTime;
                if (safetyTimer >= waitForInputTimeout) break; // NAR 종료 후(또는 NAR 부재) timeout 경과 시 자동 종료
            }
            yield return null;
        }
        if (uiManager && uiManager.GetDisplayPanel()) uiManager.CloseSituationPanel();
    }

    // 미션 타이머를 시작하고 missionCondition 충족 시 반환.
    // lockLocomotion = false(기본): 미션 진입 시 이동 허용.
    // lockLocomotion = true: 이동 잠금 유지 — ABC 자세처럼 제자리 동작이 필요한 미션에 사용.
    private IEnumerator ShowTimedMission(string missionText, Func<bool> missionCondition, Func<float> progressCalculator = null, bool isDisplayPanel = false, bool lockLocomotion = false)
    {
        // 지시 나레이션이 아직 재생 중이면 미션 진입 시점에 끊음
        if (AudioManager.Instance != null) AudioManager.Instance.StopNAR();

        // ABC 자세 미션 등 제자리 동작 시 lockLocomotion = true — 자세 유지 중 이동 제한
        if (!lockLocomotion && PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);

        if (uiManager) yield return uiManager.StartCoroutine(uiManager.StartMissionTimer(missionText, phaseTime, missionCondition, progressCalculator, isDisplayPanel));
        else yield return new WaitUntil(missionCondition);
    }

    // 연속 동작(자세 유지, 오브젝트 홀딩 등)을 추적하여 requiredDuration 동안 조건 유지 시 isActionCompleted = true.
    // 조건 미충족 시 타이머 2배 속도 감소 — 실수에 패널티 부여.
    // isActionCompleted == true 시 종료.
    private IEnumerator MonitorContinuousAction(Func<bool> actionCondition, float requiredDuration)
    {
        isActionCompleted = false; currentActionHoldTimer = 0f;
        if (requiredDuration > 0f)
        {
            while (!isActionCompleted)
            {
                if (actionCondition.Invoke()) { currentActionHoldTimer += Time.deltaTime; if (Time.frameCount % 10 == 0) TriggerHaptic(0.1f, 0.05f); }
                else { currentActionHoldTimer -= Time.deltaTime * 2.0f; }
                currentActionHoldTimer = Mathf.Clamp(currentActionHoldTimer, 0f, requiredDuration);
                if (currentActionHoldTimer >= requiredDuration) { isActionCompleted = true; break; }
                yield return null;
            }
        }
    }

    // 잘못된 존 진입 시 실패 피드백을 표시하고 저장된 위치로 복귀시킨다.
    // 플레이어가 A버튼으로 패널을 닫을 때까지 이동 잠금.
    // NOTE: ReturnToSavedPosition()에서 _returnRoutine 변수로 관리하여 중복 실행 방지
    // currentPhase 기반으로 인덱스 자동 결정 — Stage 3(FlowMove)/Stage 7(TicketGate) 등 모든 Stage에서 동작
    private IEnumerator ReturnToSavedPositionRoutine()
    {
        // 복귀 중 이동 잠금 — 위치 보정 중 조작 방지
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);
        // [압박 5/치명] 잘못된 구간 진입 실패 → 위험 수준 일시 상승
        if (uiManager != null) uiManager.UpdatePressureGauge(5);

        // 호출 시점의 currentPhase로 피드백 인덱스 자동 결정
        SubwayPhase scenarioPhase = currentPhase;
        int feedbackIndex = GetFeedbackIndexForPhase(scenarioPhase);

        // waitForInput = true — A버튼 클릭 전까지 패널 유지 및 이동 잠금
        yield return StartCoroutine(ShowFeedbackAndDelay(feedbackIndex, scenarioPhase, FeedbackKind.Negative, 2, waitForInput: true));

        // 페이드 아웃 → 순간이동 → 페이드 인 — 순간이동 멀미 방지
        if (SceneTransitionManager.Instance != null)
            yield return StartCoroutine(SceneTransitionManager.Instance.FadeOut());
        if (PlayerTransform != null && startPosition != Vector3.zero) PlayerTransform.position = startPosition;
        if (SceneTransitionManager.Instance != null)
            yield return StartCoroutine(SceneTransitionManager.Instance.FadeIn());

        // NOTE: 올바른 레티클 진입 시 시나리오에서 압박 완화
        yield return StartCoroutine(ShowFeedbackAndDelay(feedbackIndex, scenarioPhase, FeedbackKind.Return, 3, waitForInput: true));
        // 복귀 완료 — 이동 허용
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
    }

    // TargetZone 배열 인덱스 기반 활성화/비활성화
    private void SetZoneActive(int index, bool isActive) { if (TargetZone != null && TargetZone.Length > index && TargetZone[index] != null) TargetZone[index].SetActive(isActive); }

    // 서류가방 부모 계층 분리 + 중력 활성화 + 초기 속도 적용 — Stage 2 진입 시 앞으로 던져지듯 낙하 연출
    private void DropBriefcase()
    {
        if (briefcaseObject == null) return;

        // 부모에서 분리 — Hierarchy 루트로 이동, 월드 좌표 유지
        briefcaseObject.transform.SetParent(null, worldPositionStays: true);

        if (briefcaseObject.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            // 플레이어 시선(HMD) 기준으로 변환 후 초기 속도 부여 — 바라보는 앞으로 던져지는 효과
            if (headTransform == null && Camera.main != null) headTransform = Camera.main.transform;
            Transform lookRef = headTransform != null ? headTransform : briefcaseObject.transform;
            rb.linearVelocity = lookRef.TransformDirection(briefcaseThrowVelocity);
        }
    }

    // SubwayPhase ↔ 피드백 배열 인덱스 매핑 — instruction/feedback/negativeFeedback/returnFeedback 모두 동일 체계
    private int GetFeedbackIndexForPhase(SubwayPhase phase)
    {
        switch (phase)
        {
            case SubwayPhase.ABCPose_Briefcase: return 0;
            case SubwayPhase.BriefcaseQuiz:     return 1;
            case SubwayPhase.FlowMove:          return 2;
            case SubwayPhase.HoldPillar:        return 3;
            case SubwayPhase.StairMove:         return 4;
            case SubwayPhase.FallABCPose:       return 5;
            case SubwayPhase.TicketGate:        return 6;
            case SubwayPhase.Emergency119:      return 7;
            case SubwayPhase.EscalatorEscape:   return 8;
            default:                            return 0;
        }
    }

    #endregion

    #region Subway-Specific Coroutines

    // 퀴즈 패널을 열고 플레이어 선택을 대기한다.
    // 정답 선택 시 반환. 오답 선택 시 부정 피드백 UI 표시 → 시간 경과 또는 트리거 입력 시 퀴즈 재시도.
    // closeQuizOnWrong = true: 오답 시 퀴즈 패널을 닫고 피드백 단독 표시 후 재오픈 (Stage 2 선택지 퀴즈).
    // closeQuizOnWrong = false: 오답 시 퀴즈 패널 유지한 채 피드백 오버레이 표시 (Stage 8 전화번호 입력 등).
    // 반환 시까지 이동 잠금 상태를 유지한다.
    private IEnumerator ShowQuizAndWaitForAnswer(int quizIndex, SubwayPhase phase, bool closeQuizOnWrong = false)
    {
        // 퀴즈 표시 중 이동 잠금
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);

        if (uiManager) uiManager.OpenQuizPanel(quizIndex);

        while (true)
        {
            isQuizAnswered = false;
            isQuizCorrect = false;

            // 플레이어 선택 대기 — SetQuizAnswer()에서 플래그 설정
            yield return new WaitUntil(() => isQuizAnswered);

            if (isQuizCorrect)
            {
                if (uiManager) uiManager.CloseQuizPanel();
                break;
            }
            else
            {
                if (DataManager.Instance != null) DataManager.Instance.AddMistakeCount();

                if (closeQuizOnWrong)
                {
                    // 퀴즈 패널 닫고 부정 피드백 단독 표시 후 재오픈 — Stage 2 선택지 퀴즈
                    if (uiManager) uiManager.CloseQuizPanel();
                    yield return new WaitForSeconds(0.3f); // 패널 페이드 아웃 완료 대기

                    // NOTE: 오답 피드백 표시 중 A버튼·트리거 입력 차단 — feedbackDuration 경과 전 닫기 방지
                    if (uiManager) uiManager.SetFeedbackLocked(true);
                    if (uiManager) uiManager.UpdateNegativeFeedback(GetFeedbackIndexForPhase(phase));
                    if (uiManager) uiManager.OpenInstructionPanel();
                    if (AudioManager.Instance != null) { AudioManager.Instance.PlayNAR(phase, 2); AudioManager.Instance.PlaySFX(SFXType.Fail_Feedback); }
                    TriggerHaptic(0.4f, 0.3f);

                    yield return new WaitForSeconds(feedbackDuration);

                    if (uiManager) uiManager.SetFeedbackLocked(false);
                    if (uiManager) uiManager.CloseInstructionPanel();
                    yield return new WaitForSeconds(0.3f); // 패널 페이드 아웃 완료 대기
                    if (uiManager) uiManager.OpenQuizPanel(quizIndex);
                }
                else
                {
                    // 퀴즈 패널 유지한 채 부정 피드백 오버레이 표시 — Stage 8 전화번호 입력 등
                    if (uiManager) uiManager.UpdateNegativeFeedback(GetFeedbackIndexForPhase(phase));
                    if (AudioManager.Instance != null) { AudioManager.Instance.PlayNAR(phase, 2); AudioManager.Instance.PlaySFX(SFXType.Fail_Feedback); }
                    TriggerHaptic(0.4f, 0.3f);

                    yield return new WaitForSeconds(feedbackDuration);

                    if (uiManager) uiManager.CloseFeedBack();
                }
            }
        }
    }

    // 카메라 Y축을 급격히 하강시켜 넘어지는 연출을 수행한다.
    // EaseIn 커브로 가속 낙하 느낌. 도착 Y는 Inspector의 fallTargetY로 조절.
    private IEnumerator FallDownRoutine(float duration = 1.0f)
    {
        // 넘어짐 연출 중 이동 잠금
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);

        // 중력 비활성 — CharacterController의 SimpleMove 가 수동 localPosition 갱신과 충돌해 바닥을 뚫는 현상 방지
        SetGravityEnabled(false);

        Transform cameraOffset = PlayerTransform;
        Vector3 startPos = cameraOffset.localPosition;
        _standingPositionY = startPos.y; // 낙하 전 기립 Y 기록 — StandUpRoutine 복귀 기준
        Vector3 endPos = new Vector3(startPos.x, fallTargetY, startPos.z); // 바닥 시점으로 하강

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float easedT = t * t; // EaseIn — 가속 낙하
            cameraOffset.localPosition = Vector3.Lerp(startPos, endPos, easedT);
            yield return null;
        }
        cameraOffset.localPosition = endPos;
    }

    // 카메라 Y축을 서서히 상승시켜 일어나는 연출을 수행한다.
    // EaseOut 커브로 감속 상승.
    // NOTE: 중력 복원(SetGravityEnabled)은 호출부(ScenarioRoutine)에서 SetLocomotion(true) 이후에 수행
    //       — LocomotionProvider 비활성 상태에서 SimpleMove 호출 시 CharacterController 보정 snap 발생 방지
    private IEnumerator StandUpRoutine(float duration = 1.5f)
    {
        Transform cameraOffset = PlayerTransform;
        Vector3 startPos = cameraOffset.localPosition;
        Vector3 endPos = new Vector3(startPos.x, _standingPositionY, startPos.z); // 낙하 전 기록한 원래 높이로 복귀

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = 1f - (1f - t) * (1f - t); // EaseOut — 감속 상승
            cameraOffset.localPosition = Vector3.Lerp(startPos, endPos, easedT);
            yield return null;
        }
        cameraOffset.localPosition = endPos;
    }

    // FixGravity 컴포넌트 활성/비활성 토글 — 수동 카메라 Y 보간 중 CharacterController 중력 충돌 방지
    private void SetGravityEnabled(bool enable)
    {
        var fixGravity = FindAnyObjectByType<FixGravity>();
        if (fixGravity != null) fixGravity.enabled = enable;
    }

    // 에스컬레이터 시작점에서 끝점까지 자동 이동한다.
    // 50% 지점에서 압박 레벨 2→1 전환. 이동 중 수동 이동 잠금.
    private IEnumerator EscalatorMoveRoutine()
    {
        // 에스컬레이터 이동 중 수동 이동 잠금
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);

        if (escalatorStartPoint == null || escalatorEndPoint == null) yield break;

        Vector3 startPos = escalatorStartPoint.position;
        Vector3 endPos = escalatorEndPoint.position;

        float elapsed = 0f;
        bool pressureReduced = false;
        while (elapsed < escalatorDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / escalatorDuration);
            PlayerTransform.position = Vector3.Lerp(startPos, endPos, t);

            // [압박 1/경고] 50% 지점에서 압박 완화 — 탈출 가시화
            if (!pressureReduced && t >= 0.5f)
            {
                if (uiManager) uiManager.UpdatePressureGauge(1);
                pressureReduced = true;
            }

            yield return null;
        }
        PlayerTransform.position = endPos;
    }

    #endregion

    #region Main Scenario Coroutine
    private IEnumerator ScenarioRoutine()
    {
        // NOTE: null 체크 필수 — DataManager 없이 씬 직접 실행 시 NRE로 코루틴 전체 종료 방지
        if (DataManager.Instance != null) DataManager.Instance.InitializeSessionData();
        ClimbHandle.ResetGrabCount(); // NOTE: 정적 카운터 초기화 — 씬 재시작 시 이전 값 잔존 방지

        // ══════════════════════════════════════════════════════════════
        // Intro — 조작 전면 잠금 후 주의사항 → 상황 설명 패널 표시
        // ══════════════════════════════════════════════════════════════
        
        currentPhase = SubwayPhase.Caution;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAMB(AMBType.Crowd, 0);

        if (uiManager) { uiManager.SetDisplayPanel(true); uiManager.OpenCautionPanel(); }
        yield return new WaitUntil(() => uiManager == null || !uiManager.GetDisplayPanel()); yield return new WaitForSeconds(nextStepDuration);

        if (uiManager) { uiManager.SetDisplayPanel(true); uiManager.OpenSituationPanel(); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(SubwayPhase.Caution, 0);
        yield return StartCoroutine(WaitForSituationPanelClose()); yield return new WaitForSeconds(nextStepDuration);

        // 패널 확인 완료 후 이동·상호작용 허용 — Stage 1 진입 존으로 이동 가능
        if (PlayerManager.Instance != null) { PlayerManager.Instance.SetInteraction(true); PlayerManager.Instance.SetLocomotion(true); }


        // ══════════════════════════════════════════════════════════════
        // Stage 1: ABCPose_Briefcase — 서류가방 ABC 자세 취하기
        // [Zone  0] 1. ABC Gesture Entro : 진입 조건 — 도달 시 미션 시작
        // [Guide 0] 0-1. To ABC Gesture Zone : Zone[0] 대기 중 이동 경로 유도
        // [Guide 1] 1. ABC Gesture Hint  : 자세 미션 중 행동 힌트
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.ABCPose_Briefcase; UpdateAmbience(currentPhase);
        // [압박 3/위험] 서류가방 ABC 자세 구간 진입 — 압박 패널 최초 오픈
        if (uiManager) { uiManager.UpdatePressureGauge(3); uiManager.OpenPressurePanel(); }

        targetIndex = 0; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(0, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        if (briefcaseObject != null) briefcaseObject.SetActive(true);
        yield return StartCoroutine(ShowStepTextAndDelay(0, SubwayPhase.ABCPose_Briefcase));

        // 팁 0 = ABC 자세류
        if (uiManager) uiManager.DisplayTipsImage(0);
        if (gestureManager != null) gestureManager.SetBagZonesActive(true);
        Coroutine monitorCoroutine = StartCoroutine(MonitorContinuousAction(() => gestureManager != null && gestureManager.IsBriefcaseABCValid(), ABCHoldTime));
        StartCoroutine(GuideRoutine(1, () => isActionCompleted));
        // NOTE: lockLocomotion = true — ABC 자세 유지 중 이동 제한
        yield return StartCoroutine(ShowTimedMission("ABC 자세 취하기", () => isActionCompleted, () => currentActionHoldTimer / ABCHoldTime, true, lockLocomotion: true));
        StopCoroutine(monitorCoroutine);
        if (gestureManager != null) gestureManager.SetBagZonesActive(false);

        // 서류가방 ABC 자세 성공 → 지하철 출입문 개방 (탈출 신호)
        // NOTE: 닫힘은 SubwayDoor.OnTriggerExit 측에서 플레이어가 트리거 영역 벗어날 때 자동 처리
        Debug.Log("[SubwayStepManager] Stage 1 ABC 자세 성공 — SubwayDoor.OpenDoor() 호출 시도");
        if (subwayDoor)
        {
            Debug.Log($"[SubwayStepManager] subwayDoor 참조 정상 — 대상: {subwayDoor.name}", subwayDoor);
            subwayDoor.OpenDoor();
            Debug.Log("[SubwayStepManager] SubwayDoor.OpenDoor() 호출 완료");
        }
        else
        {
            Debug.LogWarning("[SubwayStepManager] subwayDoor 참조가 null — Inspector에서 Subway Door 필드 할당 필요", this);
        }

        // [압박 2/경계] 서류가방 ABC 자세 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(2);
        yield return StartCoroutine(ShowFeedbackAndDelay(0, SubwayPhase.ABCPose_Briefcase));
        // NOTE: 서류가방은 Stage 2 진입 시점에 DropBriefcase()로 자연 낙하 연출 — Stage 1 종료 시점엔 유지
        isActionCompleted = false;
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 2: BriefcaseQuiz — 물건 낙하 대처 퀴즈
        // [Zone  1] 2. Selection Quiz Entro : 진입 조건 — 도달 시 퀴즈 시작
        // [Guide 2] 1-2. To Selection Quiz Zone : Zone[1] 대기 중 이동 경로 유도
        // [Guide 3] 2. Selection Hint          : 퀴즈 중 정답 선택 힌트
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.BriefcaseQuiz; UpdateAmbience(currentPhase);
        // [압박 3/위험] 퀴즈 구간 진입 — 낙하 상황 연출
        if (uiManager) uiManager.UpdatePressureGauge(3);

        targetIndex = 1; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(2, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // 가방 낙하 연출 — 부모 계층 분리 + 중력 활성화로 자연 낙하
        DropBriefcase();
        yield return new WaitForSeconds(1f); // 낙하 연출 감상 후 퀴즈 팝업 표시

        // NOTE: 정답 선택 전까지 힌트 유도 오브젝트 표시 — 오답 반복 시 경로 가시화
        StartCoroutine(GuideRoutine(3, () => isQuizAnswered && isQuizCorrect));
        // quizContents[1] = Stage 2 BriefcaseQuiz (instruction/feedback 인덱스 체계와 일치)
        // closeQuizOnWrong = true — 오답 시 퀴즈 패널 닫고 피드백 단독 표시
        yield return StartCoroutine(ShowQuizAndWaitForAnswer(1, SubwayPhase.BriefcaseQuiz, closeQuizOnWrong: true));
        yield return StartCoroutine(ShowFeedbackAndDelay(1, SubwayPhase.BriefcaseQuiz));
        // NOTE: 이동 잠금 유지 — Stage 3 지시 패널 닫힘 후 SetLocomotion(true)에서 해제
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 3: FlowMove — 흐름 따라 이동
        // [Zone  2] 3. Flow Movement       : 이동 목표 — 도달 시 단계 완료
        // [Guide 4] 2-3. To Flow Move Start Zone : 지시 전 FlowMove 구역까지 경로 유도
        // [Guide 5] 3. To Correct Move Zone (1)  : 이동 미션 중 올바른 목표 존 유도
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.FlowMove; UpdateAmbience(currentPhase);
        // [압박 4/마비] 혼잡 심화 구간 진입
        if (uiManager) uiManager.UpdatePressureGauge(4);
        SavePlayerPosition();

        // NOTE: 지시 패널 표시 전까지 FlowMove 구역으로 이동 경로 유도 — 지시 시작 시 자동 종료
        bool flowMoveStarted = false;
        StartCoroutine(GuideRoutine(4, () => flowMoveStarted));
        yield return StartCoroutine(ShowStepTextAndDelay(2, SubwayPhase.FlowMove));
        flowMoveStarted = true;

        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        // 팁 2 = 이동
        targetIndex = 2; SetZoneActive(targetIndex, true); if (uiManager) uiManager.DisplayTipsImage(2);
        StartCoroutine(GuideRoutine(5, () => isZoneReached));
        yield return StartCoroutine(ShowTimedMission("흐름에 따라 이동", () => isZoneReached));

        SetZoneActive(targetIndex, false); isZoneReached = false;
        // [압박 3/위험] 올바른 구간 이동 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(3);
        yield return StartCoroutine(ShowFeedbackAndDelay(2, SubwayPhase.FlowMove));
        // NOTE: 이동 잠금 유지 — 피드백 종료 후 즉시 해제 방지
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 4: HoldPillar — 기둥 잡기
        // [Zone  3] 4-1. Pillar Entro   : 진입 조건 — 기둥 구역 도달 시 미션 시작
        // [Zone  4] 4-2. Pillar Trigger : 트리거 — 미션 중 기둥 잡기 감지 존
        // [Guide 6] 3-4. To Pillar Zone  : Zone[3] 대기 중 기둥 구역 이동 경로 유도
        // [Guide 7] 4. Pillar Grab Hint  : 기둥 잡기 미션 중 잡기 힌트
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.HoldPillar; UpdateAmbience(currentPhase);
        // NOTE: Zone 진입 대기 전 해제 — 잠금 유지 시 플레이어가 기둥 구역에 도달 불가
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        targetIndex = 3; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(6, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // [압박 5/치명] 압박 극대화 구간 진입
        if (uiManager) uiManager.UpdatePressureGauge(5);
        yield return StartCoroutine(ShowStepTextAndDelay(3, SubwayPhase.HoldPillar));

        // 팁 1 = 잡기류
        if (uiManager) uiManager.DisplayTipsImage(1);
        monitorCoroutine = StartCoroutine(MonitorContinuousAction(() => gestureManager != null && gestureManager.IsHoldingClimbHandle(), grabHoldTime));
        targetIndex = 4; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(7, () => isActionCompleted));
        yield return StartCoroutine(ShowTimedMission("기둥 잡기", () => isActionCompleted, () => currentActionHoldTimer / grabHoldTime, true));
        StopCoroutine(monitorCoroutine); SetZoneActive(targetIndex, false);

        // [압박 3/위험] 기둥 잡기 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(3);
        yield return StartCoroutine(ShowFeedbackAndDelay(3, SubwayPhase.HoldPillar));
        isActionCompleted = false;
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 5: StairMove — 계단 난간 잡기
        // [Zone  5] 5-1. railing Entro   : 진입 조건 — 계단 구역 도달 시 지시 표시
        // [Zone  6] 5-2. railing Trigger : 잡기 목표 — 계단 난간 인터랙트 존
        // [Guide 8] 4-5. To Stair Zone          : Zone[5] 대기 중 계단 구역 이동 경로 유도
        // [Guide 9] 5. Stair railing Grab Hint   : 잡기 미션 중 올바른 잡기 위치 유도
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.StairMove; UpdateAmbience(currentPhase);
        // [압박 2/경계] 계단 이동 구간 — 4단계 성공 후 압박 완화 유지
        if (uiManager) uiManager.UpdatePressureGauge(2);

        targetIndex = 5; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(8, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        yield return StartCoroutine(ShowStepTextAndDelay(4, SubwayPhase.StairMove));

        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        // 팁 1 = 잡기류
        if (uiManager) uiManager.DisplayTipsImage(1);
        monitorCoroutine = StartCoroutine(MonitorContinuousAction(() => gestureManager != null && gestureManager.IsHoldingClimbHandle(), grabHoldTime));
        targetIndex = 6; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(9, () => isActionCompleted));
        yield return StartCoroutine(ShowTimedMission("계단 난간 잡기", () => isActionCompleted, () => currentActionHoldTimer / grabHoldTime, true));
        StopCoroutine(monitorCoroutine); SetZoneActive(targetIndex, false);

        // 페이드 아웃 → 순간이동 → 페이드 인 — 멀미 방지
        if (stairTeleportPoint != null && PlayerTransform != null)
        {
            if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);
            if (SceneTransitionManager.Instance != null)
                yield return StartCoroutine(SceneTransitionManager.Instance.FadeOut());
            PlayerTransform.position = stairTeleportPoint.position;
            if (SceneTransitionManager.Instance != null)
                yield return StartCoroutine(SceneTransitionManager.Instance.FadeIn());
        }

        yield return StartCoroutine(ShowFeedbackAndDelay(4, SubwayPhase.StairMove));
        isActionCompleted = false;
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 6: FallABCPose — 넘어짐 ABC 자세
        // [Zone  7] 6. Crouching Gesture Entro : 진입 조건 — 도달 시 넘어짐 연출 시작
        // [Guide 10] 5-6. To Crouching Gesture Zone : Zone[7] 대기 중 이동 경로 유도
        // [Guide 11] 6. Crouching Gesture Hint      : FallABC 미션 중 자세 힌트
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.FallABCPose; UpdateAmbience(currentPhase);
        // [압박 5/치명] 넘어짐 상황 진입 — 최고 압박
        if (uiManager) uiManager.UpdatePressureGauge(5);

        targetIndex = 7; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(10, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        yield return StartCoroutine(FallDownRoutine());
        yield return StartCoroutine(ShowStepTextAndDelay(5, SubwayPhase.FallABCPose));

        // 팁 0 = ABC 자세류
        if (uiManager) uiManager.DisplayTipsImage(0);
        if (gestureManager != null) gestureManager.SetChestZonesActive(true);
        monitorCoroutine = StartCoroutine(MonitorContinuousAction(() => gestureManager != null && gestureManager.IsFallABCValid(), ABCHoldTime));
        StartCoroutine(GuideRoutine(11, () => isActionCompleted));
        // NOTE: lockLocomotion = true — 넘어진 ABC 자세 유지 중 이동 제한
        yield return StartCoroutine(ShowTimedMission("ABC 자세 취하기", () => isActionCompleted, () => currentActionHoldTimer / ABCHoldTime, true, lockLocomotion: true));
        StopCoroutine(monitorCoroutine);
        if (gestureManager != null) gestureManager.SetChestZonesActive(false);

        // [압박 4/마비] 넘어진 ABC 자세 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(4);
        yield return StartCoroutine(ShowFeedbackAndDelay(5, SubwayPhase.FallABCPose));
        isActionCompleted = false;
        yield return StartCoroutine(StandUpRoutine());
        // NOTE: SetLocomotion 먼저 → SetGravityEnabled 나중 순서 준수
        //       역순 시 LocomotionProvider 비활성 상태에서 SimpleMove가 호출되어 snap 발생
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        SetGravityEnabled(true);
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 7: TicketGate — 개찰구 통과
        // [Zone  8] 7. Ticket gate Movement : 이동 목표 — 개찰구 통과 완료 존
        // [Guide 12] 6-7. To Ticketgate Start Zone : 지시 전 개찰구 구역까지 경로 유도
        // [Guide 13] 7. To Correct Move Zone (2)   : 개찰구 이동 미션 중 올바른 방향 유도
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.TicketGate; UpdateAmbience(currentPhase);
        // [압박 4/마비] 개찰구 통과 구간 진입
        if (uiManager) uiManager.UpdatePressureGauge(4);
        SavePlayerPosition(); // 실패 시 복귀 기준점 갱신 — 개찰구 시작 지점으로

        // NOTE: 지시 패널 표시 전까지 개찰구 구역으로 이동 경로 유도 — 지시 시작 시 자동 종료
        bool ticketGateStarted = false;
        StartCoroutine(GuideRoutine(12, () => ticketGateStarted));
        yield return StartCoroutine(ShowStepTextAndDelay(6, SubwayPhase.TicketGate));
        ticketGateStarted = true;

        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        // 팁 2 = 이동
        targetIndex = 8; SetZoneActive(targetIndex, true); if (uiManager) uiManager.DisplayTipsImage(2);
        StartCoroutine(GuideRoutine(13, () => isZoneReached));
        yield return StartCoroutine(ShowTimedMission("개찰구 방향으로 이동", () => isZoneReached));
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // 개찰구 통과 성공 피드백
        yield return StartCoroutine(ShowFeedbackAndDelay(6, SubwayPhase.TicketGate));
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 8: Emergency119 — 119 신고
        // [Zone  9] 8. Report Quiz Entro    : 진입 조건 — 신고 위치 도달 시 퀴즈 시작
        // [Guide 14] 7-8. To Report Quiz Zone : Zone[9] 대기 중 퀴즈 존 이동 경로 유도
        // [Guide 15] 8. Report Quiz Hint      : 퀴즈 중 정답 선택 힌트
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.Emergency119; UpdateAmbience(currentPhase);
        // [압박 4/마비] 119 신고 구간 진입 — 압박 유지
        if (uiManager) uiManager.UpdatePressureGauge(4);

        targetIndex = 9; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(14, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // 신고 퀴즈 직전 미션 지시 — 휴대폰 등장 전 행동 안내
        yield return StartCoroutine(ShowStepTextAndDelay(7, SubwayPhase.Emergency119));

        if (smartphoneObject != null) smartphoneObject.SetActive(true);
        // NOTE: 정답 선택 전까지 힌트 유도 오브젝트 표시
        StartCoroutine(GuideRoutine(15, () => isQuizAnswered && isQuizCorrect));
        // quizContents[7] = Stage 8 Emergency119 (instruction/feedback 인덱스 체계와 일치)
        yield return StartCoroutine(ShowQuizAndWaitForAnswer(7, SubwayPhase.Emergency119));

        // [압박 3/위험] 119 신고 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(3);
        yield return StartCoroutine(ShowFeedbackAndDelay(7, SubwayPhase.Emergency119));
        if (smartphoneObject != null) smartphoneObject.SetActive(false);
        // NOTE: 이동 잠금 유지 — Stage 9 지시 패널 닫힘 후 SetLocomotion(true)에서 해제
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Stage 9: EscalatorEscape — 에스컬레이터 탈출
        // [Zone  10] 9. Escape Entro      : 진입 조건 — 탑승 존 도달 시 자동 이동
        // [Guide 16] 8-9. To Escape Zone  : Zone[10] 대기 중 에스컬레이터까지 경로 유도
        // ══════════════════════════════════════════════════════════════
        currentPhase = SubwayPhase.EscalatorEscape; UpdateAmbience(currentPhase);
        // [압박 2/경계] 탈출로 진입 — 8단계 성공 후 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(2);
        yield return StartCoroutine(ShowStepTextAndDelay(8, SubwayPhase.EscalatorEscape));

        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        // 팁 2 = 이동
        targetIndex = 10; SetZoneActive(targetIndex, true); if (uiManager) uiManager.DisplayTipsImage(2);
        StartCoroutine(GuideRoutine(16, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // 에스컬레이터 자동 이동 — 이동 중 압박 2→1 전환 포함
        yield return StartCoroutine(EscalatorMoveRoutine());

        // 탈출 단계는 피드백 없이 바로 Finished로 — 스트릿 씬 GameStepManager 패턴과 동일
        if (uiManager) uiManager.ClosePressurePanel();


        // ══════════════════════════════════════════════════════════════
        // Finish — 조작 잠금 후 결과 화면으로 전환
        // ══════════════════════════════════════════════════════════════
        if (PlayerManager.Instance != null) { PlayerManager.Instance.SetLocomotion(false); PlayerManager.Instance.SetInteraction(false); }
        HideAllGuides();
        currentPhase = SubwayPhase.Finished;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(SubwayPhase.Finished, 0);
        if (GameManager.Instance != null) GameManager.Instance.TriggerGameClear();
        // 압박 루프 사운드 정지 후 아웃트로 씬으로 전환
        if (uiManager != null) uiManager.StopIngameSounds();
        if (GameManager.Instance != null) GameManager.Instance.LoadScene("Main_Subway_Exit");
    }

    #endregion

    #region Editor Gizmos

    // 계단 텔레포트 좌표 시각화 — 노란 구체(목표), 청록 직선(플레이어→목표), 좌표 라벨
    private void OnDrawGizmos()
    {
        if (stairTeleportPoint == null) return;

        Vector3 target = stairTeleportPoint.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(target, 0.3f);

        if (PlayerTransform != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(PlayerTransform.position, target);
        }

#if UNITY_EDITOR
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.Label(target + Vector3.up * 0.4f,
            $"Stair Teleport\n({target.x:F2}, {target.y:F2}, {target.z:F2})");
#endif
    }

    #endregion
}
