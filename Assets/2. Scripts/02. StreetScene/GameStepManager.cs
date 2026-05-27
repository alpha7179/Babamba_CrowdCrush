using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;
using static GameManager;

/// <summary>
/// 시뮬레이션 시나리오 흐름을 코루틴으로 순차 진행하는 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>GamePhase 기반 시나리오 단계 관리</item>
///   <item>지시·피드백 패널 및 내레이션 순차 표시</item>
///   <item>압박 게이지·이동 잠금 상태 제어</item>
///   <item>ZoneTrigger 연동 목표 지점 활성화</item>
/// </list>
/// </remarks>
public class GameStepManager : MonoBehaviour
{
    #region Inspector Settings (References)
    [Header("Player References")]
    [SerializeField] private Transform PlayerTransform;
    [Header("Linked Managers")]
    [SerializeField] private IngameUIManager uiManager;
    [SerializeField] private GestureManager gestureManager;
    [Header("Zone Objects")]
    [SerializeField] private GameObject[] TargerZone;
    #endregion

    #region Inspector Settings (Game Logic)
    [Header("Action Settings")]
    [SerializeField] private float targetHoldTime = 3.0f;
    [Header("Timing Settings")]
    [SerializeField] private float phaseTime = 60.0f;
    [SerializeField] private float instructionDuration = 5.0f;
    [SerializeField] private float feedbackDuration = 5.0f;
    [SerializeField] private float nextStepDuration = 1.0f;
    [Tooltip("A버튼 대기 패널의 자동 스킵 제한 시간 (초). 이 시간이 지나면 A버튼 없이 자동으로 닫힘.")]
    [SerializeField] private float waitForInputTimeout = 10.0f;
    [Header("Guide Settings")]
    [Tooltip("단계별 유도 오브젝트 배열. 인덱스 순서: 0=Tutorial, 1=Move1진입, 2=Move1대각선, 3=ABCPose진입, 4=Move2대각선, 5=HoldPillar진입, 6=HoldPillar제스처, 7=ClimbUp진입, 8=ClimbUp제스처, 9=Escape")]
    [SerializeField] private GameObject[] guideObjects;
    [Tooltip("유도 오브젝트 표시까지 대기 시간 (초). 이 시간 안에 미션 미완료 시 유도선 표시.")]
    [SerializeField] private float guideShowDelay = 120f;
    #endregion

    #region Internal State
    /// <summary>시뮬레이션 진행 단계</summary>
    public enum GamePhase
    {
        /// <summary>주의사항·상황 설명 패널</summary>
        Caution,
        /// <summary>이동 연습 튜토리얼</summary>
        Tutorial,
        /// <summary>1차 대각선 이동</summary>
        Move1,
        /// <summary>가슴 압박 대응 — ABC 자세</summary>
        ABCPose,
        /// <summary>혼잡 심화 구간 2차 대각선 이동</summary>
        Move2,
        /// <summary>압박 극대화 — 기둥 잡기</summary>
        HoldPillar,
        /// <summary>절정 — 벽 잡고 버티기</summary>
        ClimbUp,
        /// <summary>탈출로 진입 → 안전구역 이동</summary>
        Escape,
        /// <summary>시나리오 완료 — 결과 화면 전환</summary>
        Finished,
        /// <summary>미설정 상태 (기본값)</summary>
        Null
    }
    [Header("Debug Info")]
    [SerializeField] private GamePhase currentPhase;
    private bool isZoneReached = false;
    private bool isActionCompleted = false;
    private float currentActionHoldTimer = 0f;
    private int targetIndex;
    private Vector3 startPosition;
    #endregion

    #region Unity Lifecycle
    private void Start()
    {
        // Caution 패널 확인 전까지 플레이어 조작 불가 — 이동 잠금
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);
        // 가슴 트리거 존 초기 비활성화 — ABCPose 구간에서만 활성화
        if (gestureManager != null) gestureManager.SetZonesActive(false);
        StartCoroutine(ScenarioRoutine());
    }
    #endregion

    #region Public API
    /// <summary>목표 존 도달 상태를 설정한다.</summary>
    /// <param name="reached">도달 여부</param>
    public void SetZoneReached(bool reached) { isZoneReached = reached; }

    /// <summary>현재 플레이어 위치를 복귀 기준점으로 저장한다.</summary>
    public void SavePlayerPosition() { if (PlayerTransform != null) startPosition = PlayerTransform.position; }

    // BUG: StopCoroutine에 새 IEnumerator 전달 시 기존 코루틴 미중단
    /// <summary>저장된 위치로 플레이어를 복귀시킨다.</summary>
    public void ReturnToSavedPosition() { StopCoroutine(ReturnToSavedPositionRoutine()); StartCoroutine(ReturnToSavedPositionRoutine()); }
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

    // 페이즈별 앰비언스·사이렌 볼륨 전환
    private void UpdateAmbience(GamePhase phase)
    {
        if (AudioManager.Instance == null) return;
        // 페이즈별 군중 앰비언스 트랙 전환 (0: 일상, 1: 혼잡, 2: 위기)
        switch (phase)
        {
            case GamePhase.Tutorial: case GamePhase.Move1: AudioManager.Instance.PlayAMB(AMBType.Crowd, 0); break;
            case GamePhase.ABCPose: case GamePhase.Move2: case GamePhase.HoldPillar: AudioManager.Instance.PlayAMB(AMBType.Crowd, 1); break;
            case GamePhase.ClimbUp: case GamePhase.Escape: AudioManager.Instance.PlayAMB(AMBType.Crowd, 2); break;
        }
        // ABCPose부터 사이렌 시작 — 마지막 스텝(Escape)으로 갈수록 단계적으로 볼륨 상승
        // Police : 경찰차 사이렌 (선두 등장이므로 구급차보다 먼저, 더 크게)
        // Ambulance : 구급차 사이렌 (혼란이 심화될수록 증가)
        if (phase >= GamePhase.ABCPose && phase != GamePhase.Finished)
        {
            AudioManager.Instance.PlayShuffleSFX(SFXType.Police, true);
            AudioManager.Instance.PlaySFX(SFXType.Ambulance, true, true);

            float policeVol, ambulanceVol;
            switch (phase)
            {
                case GamePhase.ABCPose:    policeVol = 0.20f; ambulanceVol = 0.10f; break;
                case GamePhase.Move2:      policeVol = 0.40f; ambulanceVol = 0.30f; break;
                case GamePhase.HoldPillar: policeVol = 0.60f; ambulanceVol = 0.55f; break;
                case GamePhase.ClimbUp:    policeVol = 0.80f; ambulanceVol = 0.75f; break;
                case GamePhase.Escape:     policeVol = 1.00f; ambulanceVol = 1.00f; break;
                default:                   policeVol = 0.20f; ambulanceVol = 0.10f; break;
            }

            AudioManager.Instance.SetLoopingSFXScale(SFXType.Police, policeVol);
            AudioManager.Instance.SetLoopingSFXScale(SFXType.Ambulance, ambulanceVol);
        }
    }
    #endregion

    #region UI & Logic Helper Coroutines

    // 지시 패널을 열고 내레이션을 재생한다.
    // 패널이 닫히거나 instructionDuration 경과 시 반환.
    // 반환 시까지 이동 잠금 상태를 유지한다.
    private IEnumerator ShowStepTextAndDelay(int instructionIndex, GamePhase phase, int narIndex = 0)
    {
        // 지시 패널 표시 중 이동 잠금 — 조작 혼선 방지
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);

        if (uiManager) { uiManager.CloseFeedBack(); uiManager.UpdateInstruction(instructionIndex); uiManager.OpenInstructionPanel(); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(GameScene.Simulator, phase, narIndex);

        float timer = 0f;
        while (uiManager.GetDisplayPanel() && timer < instructionDuration) { timer += Time.deltaTime; yield return null; }
        if (uiManager && uiManager.GetDisplayPanel()) uiManager.CloseInstructionPanel();
    }

    // 피드백 패널을 열고 결과 내레이션을 재생한다.
    // isNegative = true 이면 실패 피드백(부정 내레이션 + Fail SFX).
    // waitForInput = true 이면 플레이어가 A버튼으로 닫을 때까지 패널 유지 (타이머 없음).
    // 반환 시까지 이동 잠금 상태를 유지한다.
    private IEnumerator ShowFeedbackAndDelay(int feedbackIndex, GamePhase phase, bool isNegative = false, int narIndex = 1, bool waitForInput = false)
    {
        // 피드백 표시 중 이동 잠금 — 결과 확인 전까지 조작 불가
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);

        if (uiManager)
        {
            uiManager.CloseInstruction();
            if (!isNegative) uiManager.UpdateFeedBack(feedbackIndex);
            else uiManager.UpdateNegativeFeedback(feedbackIndex);
            uiManager.OpenInstructionPanel(); // BUG FIX: 긍정/부정 공통 — 패널을 열어야 텍스트가 표시됨
        }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(GameScene.Simulator, phase, narIndex);
        if (AudioManager.Instance != null) { SFXType sfx = isNegative ? SFXType.Fail_Feedback : SFXType.Success_Feedback; AudioManager.Instance.PlaySFX(sfx); }
        if (!isNegative) TriggerHaptic(0.8f, 0.3f); else TriggerHaptic(0.4f, 0.1f);

        if (waitForInput)
        {
            // A버튼 클릭 대기 — waitForInputTimeout 초 경과 시 자동 스킵
            yield return StartCoroutine(WaitForPanelClose());
        }
        else
        {
            float timer = 0f; while (uiManager.GetDisplayPanel() && timer < feedbackDuration) { timer += Time.deltaTime; yield return null; }
            if (uiManager && uiManager.GetDisplayPanel()) uiManager.CloseInstructionPanel();
        }
    }

    // guideShowDelay 초 동안 completionCondition 미충족 시 guideIndex 유도 오브젝트를 표시함.
    // 조건 충족(미션 완료) 시 오브젝트를 비활성화하고 종료.
    // guideIndex가 배열 범위 밖이거나 오브젝트가 null이면 즉시 반환.
    private IEnumerator GuideRoutine(int guideIndex, System.Func<bool> completionCondition)
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
        while (uiManager.GetDisplayPanel())
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
        while (uiManager.GetDisplayPanel())
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
    // 미션 진입 시 이동 허용. 정지 미션(자세 취하기 등)은 상위 시나리오에서 별도로 SetLocomotion(false) 호출 필요.
    private IEnumerator ShowTimedMission(string missionText, System.Func<bool> missionCondition, System.Func<float> progressCalculator = null, bool isDisplayPanel = false)
    {
        // 지시 나레이션이 아직 재생 중이면 미션 진입 시점에 끊음
        if (AudioManager.Instance != null) AudioManager.Instance.StopNAR();

        // 미션 진입 — 이동 허용
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);

        if (uiManager) yield return uiManager.StartCoroutine(uiManager.StartMissionTimer(missionText, phaseTime, missionCondition, progressCalculator, isDisplayPanel));
        else yield return new WaitUntil(missionCondition);
    }

    // 연속 동작(자세 유지, 오브젝트 홀딩 등)을 추적하여 requiredDuration 동안 조건 유지 시 isActionCompleted = true.
    // 조건 미충족 시 타이머 2배 속도 감소 — 실수에 패널티 부여.
    // isActionCompleted == true 시 종료.
    private IEnumerator MonitorContinuousAction(System.Func<bool> actionCondition, float requiredDuration)
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

    // ZoneTrigger가 잘못된 존(가운데) 진입을 감지했을 때 호출.
    // 실패 피드백 표시 후 저장된 위치로 플레이어 복귀.
    // 플레이어가 A버튼으로 패널을 닫을 때까지 이동 잠금.
    // NOTE: 복귀 후에도 압박 3(위험) 유지 — 올바른 레티클 진입 시 시나리오에서 완화됨
    private IEnumerator ReturnToSavedPositionRoutine()
    {
        // 복귀 중 이동 잠금 — 위치 보정 중 조작 방지
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(false);
        // [압박 3/위험] 잘못된 구간 진입 실패 → 위험 수준 상승
        if (uiManager != null) uiManager.UpdatePressureGauge(3);
        // waitForInput = true — A버튼 클릭 전까지 패널 유지 및 이동 잠금
        yield return StartCoroutine(ShowFeedbackAndDelay(0, GamePhase.Move1, true, 2, waitForInput: true));

        // 페이드 아웃 → 순간이동 → 페이드 인 — 순간이동 멀미 방지
        if (SceneTransitionManager.Instance != null)
            yield return StartCoroutine(SceneTransitionManager.Instance.FadeOut());
        if (PlayerTransform != null && startPosition != Vector3.zero) PlayerTransform.position = startPosition;
        if (SceneTransitionManager.Instance != null)
            yield return StartCoroutine(SceneTransitionManager.Instance.FadeIn());

        // NOTE: 압박 3(위험) 유지 — 올바른 레티클(targetIndex 2)에 진입해야 시나리오에서 압박 완화
        // 복귀 완료 — 이동 허용
        yield return StartCoroutine(ShowFeedbackAndDelay(1, GamePhase.Move1, true, 3, waitForInput: true));
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
    }
    #endregion

    #region Main Scenario Coroutine
    private IEnumerator ScenarioRoutine()
    {
        if (DataManager.Instance != null) DataManager.Instance.InitializeSessionData();
        ClimbHandle.ResetGrabCount(); // NOTE: 정적 카운터 초기화 — 씬 재시작 시 이전 값 잔존 방지

        // ══════════════════════════════════════════════════════════════
        // Intro — 조작 전면 잠금 후 주의사항 → 상황 설명 패널 표시
        // ══════════════════════════════════════════════════════════════
        // 조작 전면 잠금 — Caution 패널 확인 전까지 이동·상호작용 불가
        if (PlayerManager.Instance != null) { PlayerManager.Instance.SetLocomotion(false); PlayerManager.Instance.SetInteraction(false); }
        
        currentPhase = GamePhase.Caution;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayAMB(AMBType.Crowd, 0);

        if (uiManager) { uiManager.SetDisplayPanel(true); uiManager.OpenCautionPanel(); }
        yield return new WaitUntil(() => !uiManager.GetDisplayPanel()); yield return new WaitForSeconds(nextStepDuration);

        if (uiManager) { uiManager.SetDisplayPanel(true); uiManager.OpenSituationPanel(); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(GameScene.Simulator, GamePhase.Caution, 0);
        yield return StartCoroutine(WaitForSituationPanelClose()); yield return new WaitForSeconds(nextStepDuration);

        // 패널 확인 완료 후 상호작용 허용 — 이동 허용은 ShowTimedMission 진입 시 수행
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetInteraction(true);


        // ══════════════════════════════════════════════════════════════
        // Phase 0: Tutorial — 이동 연습
        // ══════════════════════════════════════════════════════════════
        currentPhase = GamePhase.Tutorial; UpdateAmbience(currentPhase);
        yield return StartCoroutine(ShowStepTextAndDelay(0, GamePhase.Tutorial));

        // [압박 2/경계] 이동 연습 시작 — 압박 패널 최초 오픈
        if (uiManager) { uiManager.UpdatePressureGauge(2); uiManager.OpenPressurePanel(); }
        targetIndex = 0; SetZoneActive(targetIndex, true); if (uiManager) uiManager.DisplayTipsImage(2);
        StartCoroutine(GuideRoutine(0, () => isZoneReached));
        yield return StartCoroutine(ShowTimedMission("목표지점으로 이동", () => isZoneReached));

        SetZoneActive(targetIndex, false); isZoneReached = false;
        yield return StartCoroutine(ShowFeedbackAndDelay(0, GamePhase.Tutorial));
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Phase 1: Move1 — 1차 대각선 이동
        // ══════════════════════════════════════════════════════════════
        currentPhase = GamePhase.Move1; UpdateAmbience(currentPhase);

        // 지시 패널 없이 바로 존 진입으로 이어지므로 이동 허용
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);
        targetIndex = 1; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(1, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // [압박 2/경계] 1차 대각선 이동 구간 진입
        if (uiManager) uiManager.UpdatePressureGauge(2);
        SavePlayerPosition(); // 실패 시 복귀 기준점 저장

        yield return StartCoroutine(ShowStepTextAndDelay(1, GamePhase.Move1));

        targetIndex = 2; SetZoneActive(targetIndex, true); if (uiManager) uiManager.DisplayTipsImage(2);
        StartCoroutine(GuideRoutine(2, () => isZoneReached));
        yield return StartCoroutine(ShowTimedMission("대각선으로 이동", () => isZoneReached));

        SetZoneActive(targetIndex, false); isZoneReached = false;
        // [압박 1/경고] 가장자리 진입 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(1);
        yield return StartCoroutine(ShowFeedbackAndDelay(1, GamePhase.Move1));
        yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Phase 2: ABCPose — 가슴 압박 대응 자세 취하기
        // ══════════════════════════════════════════════════════════════
        currentPhase = GamePhase.ABCPose; UpdateAmbience(currentPhase);
        // ABCPose 존 진입 전 이동 허용
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);

        targetIndex = 3; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(3, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // [압박 3/위험] 가슴 압박 대응 구간 진입
        if (uiManager) uiManager.UpdatePressureGauge(3);
        yield return StartCoroutine(ShowStepTextAndDelay(2, GamePhase.ABCPose));

        // ADD: ABC 자세 체크 구간 — 가슴 트리거 존 활성화
        if (gestureManager != null) gestureManager.SetZonesActive(true);

        // MonitorContinuousAction을 백그라운드로 실행하여 자세 유지 진행률을
        // ShowTimedMission의 progressCalculator에 실시간으로 전달
        Coroutine monitorCoroutine = StartCoroutine(MonitorContinuousAction(() => gestureManager.IsActionValid(), targetHoldTime));
        if (uiManager) uiManager.DisplayTipsImage(0);
        yield return StartCoroutine(ShowTimedMission("ABC 자세 취하기", () => isActionCompleted, () => currentActionHoldTimer / targetHoldTime, true));
        StopCoroutine(monitorCoroutine);

        // ADD: ABC 자세 완료 — 가슴 트리거 존 비활성화
        if (gestureManager != null) gestureManager.SetZonesActive(false);

        // [압박 2/경계] ABC 자세 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(2);
        yield return StartCoroutine(ShowFeedbackAndDelay(2, GamePhase.ABCPose));
        isActionCompleted = false; yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Phase 3: Move2 — 혼잡 심화 구간 2차 대각선 이동
        // ══════════════════════════════════════════════════════════════
        currentPhase = GamePhase.Move2; UpdateAmbience(currentPhase);
        // [압박 4/마비] 혼잡 심화 구간 진입
        if (uiManager) uiManager.UpdatePressureGauge(4);
        SavePlayerPosition(); // 실패 시 복귀 기준점 갱신
        yield return StartCoroutine(ShowStepTextAndDelay(3, GamePhase.Move2));

        targetIndex = 4; SetZoneActive(targetIndex, true); if (uiManager) uiManager.DisplayTipsImage(2);
        StartCoroutine(GuideRoutine(4, () => isZoneReached));
        yield return StartCoroutine(ShowTimedMission("대각선으로 이동", () => isZoneReached));

        SetZoneActive(targetIndex, false); isZoneReached = false;
        // [압박 3/위험] 2차 대각선 이동 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(3);
        yield return StartCoroutine(ShowFeedbackAndDelay(3, GamePhase.Move2)); yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Phase 4: HoldPillar — 압박 극대화, 기둥 잡기
        // ══════════════════════════════════════════════════════════════
        currentPhase = GamePhase.HoldPillar; UpdateAmbience(currentPhase);
        // HoldPillar 존 진입 전 이동 허용
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);

        targetIndex = 5; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(5, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // [압박 4/마비] 압박 극대화 구간 진입
        if (uiManager) uiManager.UpdatePressureGauge(4);
        yield return StartCoroutine(ShowStepTextAndDelay(4, GamePhase.HoldPillar));

        monitorCoroutine = StartCoroutine(MonitorContinuousAction(() => gestureManager.IsHoldingClimbHandle(), targetHoldTime));
        if (uiManager) uiManager.DisplayTipsImage(1);
        targetIndex = 6; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(6, () => isActionCompleted));
        yield return StartCoroutine(ShowTimedMission("기둥 잡기", () => isActionCompleted, () => currentActionHoldTimer / targetHoldTime, true));
        StopCoroutine(monitorCoroutine); SetZoneActive(targetIndex, false);

        // [압박 3/위험] 기둥 잡기 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(3);
        yield return StartCoroutine(ShowFeedbackAndDelay(4, GamePhase.HoldPillar));
        isActionCompleted = false; yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Phase 5: ClimbUp — 절정, 벽 잡고 버티기
        // ══════════════════════════════════════════════════════════════
        currentPhase = GamePhase.ClimbUp; UpdateAmbience(currentPhase);
        // ClimbUp 존 진입 전 이동 허용
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetLocomotion(true);

        targetIndex = 7; SetZoneActive(targetIndex, true);
        StartCoroutine(GuideRoutine(7, () => isZoneReached));
        yield return new WaitUntil(() => isZoneReached);
        SetZoneActive(targetIndex, false); isZoneReached = false;

        // [압박 5/치명] 절정 구간 진입 — 최고 압박
        if (uiManager) uiManager.UpdatePressureGauge(5);
        yield return StartCoroutine(ShowStepTextAndDelay(5, GamePhase.ClimbUp));

        targetIndex = 8; SetZoneActive(targetIndex, true);
        monitorCoroutine = StartCoroutine(MonitorContinuousAction(() => gestureManager.IsHoldingClimbHandle(), targetHoldTime));
        if (uiManager) uiManager.DisplayTipsImage(1);
        StartCoroutine(GuideRoutine(8, () => isActionCompleted));
        yield return StartCoroutine(ShowTimedMission("벽 잡기", () => isActionCompleted, () => currentActionHoldTimer / targetHoldTime, true));
        StopCoroutine(monitorCoroutine); SetZoneActive(targetIndex, false);

        // [압박 3/위험] 벽 잡기 성공 → 압박 완화
        if (uiManager) uiManager.UpdatePressureGauge(3);
        yield return StartCoroutine(ShowFeedbackAndDelay(5, GamePhase.ClimbUp));
        isZoneReached = false; isActionCompleted = false; yield return new WaitForSeconds(nextStepDuration);


        // ══════════════════════════════════════════════════════════════
        // Phase 6: Escape — 탈출로 진입 → 안전구역 이동
        // ══════════════════════════════════════════════════════════════
        currentPhase = GamePhase.Escape; UpdateAmbience(currentPhase);
        // [압박 2/경계] 탈출로 진입 — 아직 위험 상황이므로 압박 유지
        if (uiManager) uiManager.UpdatePressureGauge(2);
        yield return StartCoroutine(ShowStepTextAndDelay(6, GamePhase.Escape));

        targetIndex = 9; SetZoneActive(targetIndex, true); if (uiManager) uiManager.DisplayTipsImage(2);
        StartCoroutine(GuideRoutine(9, () => isZoneReached));
        yield return StartCoroutine(ShowTimedMission("안전구역으로 이동", () => isZoneReached));

        SetZoneActive(targetIndex, false); isZoneReached = false;
        // [압박 0/안전] 안전구역 도달 → 압박 완전 해제 및 패널 닫기
        if (uiManager) { uiManager.UpdatePressureGauge(0); uiManager.ClosePressurePanel(); }


        // ══════════════════════════════════════════════════════════════
        // Finish — 조작 잠금 후 결과 화면으로 전환
        // ══════════════════════════════════════════════════════════════
        // 시나리오 종료 — 이동·상호작용 전면 잠금, 잔여 유도 오브젝트 정리
        if (PlayerManager.Instance != null) { PlayerManager.Instance.SetLocomotion(false); PlayerManager.Instance.SetInteraction(false); }
        HideAllGuides();
        currentPhase = GamePhase.Finished;
        if (AudioManager.Instance != null) AudioManager.Instance.PlayNAR(GameScene.Simulator, GamePhase.Finished, 0);
        if (Instance != null) Instance.TriggerGameClear();
        if (uiManager) uiManager.ShowOuttroUI();
    }

    private void SetZoneActive(int index, bool isActive) { if (TargerZone != null && TargerZone.Length > index && TargerZone[index] != null) TargerZone[index].SetActive(isActive); }
    #endregion
}
