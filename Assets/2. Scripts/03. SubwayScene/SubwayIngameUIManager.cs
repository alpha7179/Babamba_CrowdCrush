using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR;

/// <summary>
/// 지하철 씬 인게임 HUD 제어 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>압박 게이지 UI 및 비네팅 연동</item>
///   <item>미션 타이머·진행률 표시</item>
///   <item>지시문·피드백 패널 전환</item>
///   <item>일시정지·주의사항·상황 패널 관리</item>
///   <item>Quiz Panel (지하철 씬 전용 신규)</item>
/// </list>
/// </remarks>
public class SubwayIngameUIManager : MonoBehaviour
{
    #region Inspector Settings (Input)

    [Header("Controller Input Enable Settings")]
    [Tooltip("A버튼 입력 활성화 여부")]
    [SerializeField] private bool _isAButtonEnabled = false;
    [Tooltip("B버튼 입력 활성화 여부")]
    [SerializeField] private bool _isBButtonEnabled = false;

    #endregion

    #region Inspector Settings (Panels)

    [Header("HUD Elements")]
    [SerializeField] private Canvas IngameCanvas;
    [Header("Popup Panels")]
    [SerializeField] private GameObject cautionPanel;
    [SerializeField] private GameObject situationPanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject instructionPanel;
    [SerializeField] private GameObject progressPanel;
    [SerializeField] private GameObject pressurePanel;
    [Header("Quiz Contents")]
    // 퀴즈 인덱스별 퀴즈 내용 오브젝트 배열 (0: 물건 낙하, 1: 119 신고) — instructionPanel 하위에 배치
    [SerializeField] private GameObject[] quizContents;

    #endregion

    #region Inspector Settings (UI Elements)

    [Header("Text Elements")]
    [SerializeField] private GameObject[] instruction;
    private int currentInstruction = 0;
    [SerializeField] private GameObject[] feedback;
    [SerializeField] private GameObject[] negativeFeedback;
    [Tooltip("복귀 피드백 — 잘못된 구역 진입 후 원위치 복귀 시 표시")]
    [SerializeField] private GameObject[] returnFeedback;
    private int currentFeedback = 0;
    private int currentNegativeFeedback = 0;
    private int currentReturnFeedback = 0;

    [Header("Progress Elements")]
    [SerializeField] private TextMeshProUGUI progressMissionText;
    [SerializeField] public TextMeshProUGUI progressText;
    [SerializeField] public Image barSlider;
    [SerializeField] public Image[] tipsImage;

    [Header("Pressure Elements")]
    [SerializeField] private TextMeshProUGUI pressureStateText;
    [SerializeField] public Image[] pressureGaugeImages;
    [SerializeField] public Image[] pressureHighlightImages;
    private readonly string[] PressureState = new string[] { "안전", "경고", "압박", "위험", "마비", "치명" };

    #endregion

    #region Inspector Settings (Effects & Settings)

    [Header("Visual Effects")]
    [SerializeField] private PressureVignette pressureVignette;
    [Header("Animation Settings")]
    [SerializeField] private float vignetteSmoothTime = 0.5f;
    [SerializeField] private float imageFadeDuration = 0.3f;
    [SerializeField] private float panelFadeDuration = 0.2f;
    [Header("Caution Panel Settings")]
    [Tooltip("주의사항 패널 자동 닫힘 대기 시간(초). 0 이하이면 자동 닫힘 비활성화")]
    [SerializeField] private float cautionAutoCloseDuration = 5f;
    [Header("Pulse Settings")]
    [SerializeField] private float pulseSpeed = 3.0f;
    [SerializeField] private float minPulseAlpha = 0.2f;

    #endregion

    #region External References

    [Header("External References")]
    [SerializeField] private OuttroUIManager outtroManager;

    #endregion

    #region Internal State

    private bool isDisplayPanel = false;
    // NOTE: 오답 피드백 표시 중 입력 차단 플래그 — 피드백 확인 전 패널 닫기 방지
    private bool _isFeedbackLocked = false;
    private Dictionary<GameObject, Coroutine> panelCoroutines = new Dictionary<GameObject, Coroutine>();
    private Dictionary<Image, Coroutine> imageCoroutines = new Dictionary<Image, Coroutine>();
    private float currentVignetteValue = 0f;
    private Coroutine vignetteCoroutine;
    private Coroutine _cautionAutoCloseCoroutine;
    private float cachedOriginalAlpha = 1.0f;
    private int currentPressureLevel = 0;
    // NOTE: OnEnable 시점에 Instance null이면 구독 스킵됨 — Start에서 재시도 여부 추적
    private bool _isInputSubscribed = false;

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        InitializeUI();
        InitializeEvents();
        // NOTE: OnEnable에서 ControllerInputManager.Instance가 null이었을 경우 Start에서 재시도
        if (!_isInputSubscribed) SubscribeInputEvents();
    }

    private void OnEnable()
    {
        SubscribeInputEvents();
    }

    private void OnDisable()
    {
        if (ControllerInputManager.Instance != null)
        {
            ControllerInputManager.Instance.OnRightTriggerDown -= HandleRTriggerInput;
            ControllerInputManager.Instance.OnAButtonDown -= HandleAButtonInput;
            ControllerInputManager.Instance.OnBButtonDown -= HandleBButtonInput;
            ControllerInputManager.Instance.OnYButtonDown -= HandleYButtonInput;
        }
        _isInputSubscribed = false;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnPauseStateChanged -= HandlePauseState;
        StopAllPressureSounds();
    }

    #endregion

    #region Initialization

    private void InitializeUI()
    {
        // NOTE: 씬 재진입 시 ShowOuttroUI()가 IngameCanvas를 끈 채로 씬이 저장될 수 있어 명시적으로 복원
        if (IngameCanvas) IngameCanvas.enabled = true;

        if (cautionPanel) cautionPanel.SetActive(true);
        if (situationPanel) situationPanel.SetActive(false);
        if (pausePanel) pausePanel.SetActive(false);
        if (instructionPanel) instructionPanel.SetActive(false);
        // NOTE: instructionPanel.SetActive(false)는 자식의 activeSelf를 바꾸지 않음
        //       quizContents[i].activeSelf가 true인 채로 남으면 IsAnyQuizActive()가 true → 입력 전체 차단
        if (quizContents != null)
            foreach (var content in quizContents)
                if (content != null) content.SetActive(false);
        if (progressPanel) progressPanel.SetActive(false);
        // NOTE: pressurePanel은 SubwayStepManager에서 OpenPressurePanel() 호출 전까지 숨김
        if (pressurePanel) pressurePanel.SetActive(false);

        if (outtroManager) outtroManager.gameObject.SetActive(false);

        HideAllTipsImages();

        if (pressureVignette != null) pressureVignette.SetIntensity(0f);
    }

    private void InitializeEvents()
    {
        if (GameManager.Instance != null) GameManager.Instance.OnPauseStateChanged += HandlePauseState;
    }

    private void SubscribeInputEvents()
    {
        if (_isInputSubscribed || ControllerInputManager.Instance == null) return;
        ControllerInputManager.Instance.OnRightTriggerDown += HandleRTriggerInput;
        ControllerInputManager.Instance.OnAButtonDown += HandleAButtonInput;
        ControllerInputManager.Instance.OnBButtonDown += HandleBButtonInput;
        ControllerInputManager.Instance.OnYButtonDown += HandleYButtonInput;
        _isInputSubscribed = true;
    }

    private void PlayPressureSounds()
    {
        if (AudioManager.Instance != null)
        {
            // NOTE: 페이드를 켜서 볼륨 튀는 현상 방지
            AudioManager.Instance.PlaySFX(SFXType.heartbeat, isLoop: true, useFade: true);
            AudioManager.Instance.PlaySFX(SFXType.breath, isLoop: true, useFade: true);
            AudioManager.Instance.PlaySFX(SFXType.EarRinging, isLoop: true, useFade: true);
        }
    }

    private void StopAllPressureSounds()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.StopSFX(SFXType.heartbeat, true);
            AudioManager.Instance.StopSFX(SFXType.breath, true);
            AudioManager.Instance.StopSFX(SFXType.EarRinging, true);
        }
    }

    #endregion

    #region Public API

    /// <summary>주의사항 패널을 열고 효과음을 재생함. cautionAutoCloseDuration 초 후 자동으로 닫힘</summary>
    public void OpenCautionPanel()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.Pause_Feedback);
        FadePanel(cautionPanel, true);
        SetDisplayPanel(true);
        if (cautionAutoCloseDuration > 0f)
        {
            if (_cautionAutoCloseCoroutine != null) StopCoroutine(_cautionAutoCloseCoroutine);
            _cautionAutoCloseCoroutine = StartCoroutine(CautionAutoCloseRoutine());
        }
    }
    /// <summary>주의사항 패널을 닫고 자동 닫힘 코루틴을 취소함</summary>
    public void CloseCautionPanel()
    {
        if (_cautionAutoCloseCoroutine != null) { StopCoroutine(_cautionAutoCloseCoroutine); _cautionAutoCloseCoroutine = null; }
        FadePanel(cautionPanel, false);
        SetDisplayPanel(false);
    }
    /// <summary>상황 설명 패널을 열고 효과음을 재생함</summary>
    public void OpenSituationPanel() { if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.Pause_Feedback); FadePanel(situationPanel, true); SetDisplayPanel(true); }
    /// <summary>상황 설명 패널을 닫음</summary>
    public void CloseSituationPanel() { FadePanel(situationPanel, false); SetDisplayPanel(false); }
    /// <summary>지시문 패널을 열고 효과음을 재생함</summary>
    public void OpenInstructionPanel() { if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.Pause_Feedback); FadePanel(instructionPanel, true); SetDisplayPanel(true); }
    /// <summary>지시문 패널을 닫고 지시문·피드백을 초기화함</summary>
    public void CloseInstructionPanel() { FadePanel(instructionPanel, false); SetDisplayPanel(false); CloseInstruction(); CloseFeedBack(); }
    /// <summary>진행률 패널을 열고 미션 텍스트를 설정함</summary>
    public void OpenProgressPanel(string missionText) { if (progressMissionText) progressMissionText.text = missionText; if (progressPanel) FadePanel(progressPanel, true); }
    /// <summary>진행률 패널을 닫고 팁 이미지를 숨김</summary>
    public void CloseProgressPanel() { if (progressPanel) FadePanel(progressPanel, false); HideAllTipsImages(); }
    /// <summary>압박 게이지 패널을 표시함</summary>
    public void OpenPressurePanel() { if (pressurePanel) FadePanel(pressurePanel, true); }
    /// <summary>압박 게이지 패널을 숨김</summary>
    public void ClosePressurePanel() { if (pressurePanel) FadePanel(pressurePanel, false); }
    /// <summary>인게임 캔버스를 비활성화하고 아웃트로 UI를 초기화함</summary>
    public void ShowOuttroUI() { StopAllPressureSounds(); if (IngameCanvas) IngameCanvas.enabled = false; if (outtroManager) { outtroManager.gameObject.SetActive(true); StartCoroutine(outtroManager.InitializeRoutine()); } }
    /// <summary>씬 전환 전 인게임 루프 사운드(심박음·호흡·이명) 및 군중 환경음을 정지함</summary>
    public void StopIngameSounds() { StopAllPressureSounds(); if (AudioManager.Instance != null) AudioManager.Instance.StopAMB(); }
    /// <summary>지시문 텍스트를 전환함</summary>
    public void UpdateInstruction(int instructionNum) { if (instruction[currentInstruction].activeSelf) instruction[currentInstruction].SetActive(false); instruction[instructionNum].SetActive(true); currentInstruction = instructionNum; }
    /// <summary>현재 지시문을 숨김</summary>
    public void CloseInstruction() { instruction[currentInstruction].SetActive(false); }
    /// <summary>긍정 피드백 텍스트를 전환함</summary>
    public void UpdateFeedBack(int FeedbackNum) { if (feedback[currentFeedback].activeSelf) feedback[currentFeedback].SetActive(false); feedback[FeedbackNum].SetActive(true); currentFeedback = FeedbackNum; }
    /// <summary>부정 피드백 텍스트를 전환함</summary>
    public void UpdateNegativeFeedback(int FeedbackNum) { if (negativeFeedback == null || negativeFeedback.Length == 0) return; if (negativeFeedback[currentNegativeFeedback] != null && negativeFeedback[currentNegativeFeedback].activeSelf) negativeFeedback[currentNegativeFeedback].SetActive(false); if (negativeFeedback[FeedbackNum] != null) negativeFeedback[FeedbackNum].SetActive(true); currentNegativeFeedback = FeedbackNum; }
    /// <summary>복귀 피드백 텍스트를 전환함 — 잘못된 구역 진입 후 원위치 복귀 시 사용</summary>
    public void UpdateReturnFeedback(int FeedbackNum) { if (returnFeedback == null || returnFeedback.Length == 0) return; if (returnFeedback[currentReturnFeedback] != null && returnFeedback[currentReturnFeedback].activeSelf) returnFeedback[currentReturnFeedback].SetActive(false); if (returnFeedback[FeedbackNum] != null) returnFeedback[FeedbackNum].SetActive(true); currentReturnFeedback = FeedbackNum; }
    /// <summary>현재 활성화된 긍정·부정·복귀 피드백을 모두 숨김</summary>
    public void CloseFeedBack() { if (feedback != null && feedback.Length > 0 && feedback[currentFeedback] != null && feedback[currentFeedback].activeSelf) feedback[currentFeedback].SetActive(false); if (negativeFeedback != null && negativeFeedback.Length > 0 && negativeFeedback[currentNegativeFeedback] != null && negativeFeedback[currentNegativeFeedback].activeSelf) negativeFeedback[currentNegativeFeedback].SetActive(false); if (returnFeedback != null && returnFeedback.Length > 0 && returnFeedback[currentReturnFeedback] != null && returnFeedback[currentReturnFeedback].activeSelf) returnFeedback[currentReturnFeedback].SetActive(false); }
    /// <summary>패널 표시 상태 플래그를 설정함</summary>
    public void SetDisplayPanel(bool state) { isDisplayPanel = state; }
    /// <summary>패널 표시 상태 플래그를 반환함</summary>
    public bool GetDisplayPanel() { return isDisplayPanel; }
    /// <summary>오답 피드백 표시 중 입력 차단 여부를 설정함 — true이면 A버튼·트리거로 패널 닫기 불가</summary>
    public void SetFeedbackLocked(bool locked) { _isFeedbackLocked = locked; }
    /// <summary>지정 인덱스의 팁 이미지만 표시하고 나머지를 숨김</summary>
    public void DisplayTipsImage(int pageIndex) { if (tipsImage == null) return; for (int i = 0; i < tipsImage.Length; i++) if (tipsImage[i] != null) tipsImage[i].gameObject.SetActive(i == pageIndex); }
    /// <summary>모든 팁 이미지를 숨김</summary>
    public void HideAllTipsImages() { if (tipsImage == null) return; foreach (var img in tipsImage) if (img != null) img.gameObject.SetActive(false); }

    /// <summary>
    /// 퀴즈 선택지를 활성화하고 지시 패널을 열어 표시함.
    /// 별도 퀴즈 패널 없이 지시 패널과 통합 운영.
    /// </summary>
    /// <param name="quizIndex">퀴즈 인덱스 (0: 물건 낙하, 1: 119 신고)</param>
    public void OpenQuizPanel(int quizIndex)
    {
        // 퀴즈 인덱스에 해당하는 컨텐츠만 활성화
        if (quizContents != null)
        {
            for (int i = 0; i < quizContents.Length; i++)
            {
                if (quizContents[i] != null)
                    quizContents[i].SetActive(i == quizIndex);
            }
        }

        // NOTE: quizPanel 대신 instructionPanel 사용 — 퀴즈·지시 패널 통합
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.Pause_Feedback);
        FadePanel(instructionPanel, true);
        SetDisplayPanel(true);
    }

    /// <summary>퀴즈 선택지를 비활성화하고 지시 패널을 닫음</summary>
    public void CloseQuizPanel()
    {
        if (quizContents != null)
            foreach (var content in quizContents)
                if (content != null) content.SetActive(false);

        CloseInstructionPanel();
    }

    #endregion

    #region Interaction Events

    private void TriggerInteractionFeedback()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.UI_Click);
        TriggerHapticImpulse();
    }

    private void TriggerHapticImpulse(float rawAmplitude = 0.5f, float duration = 0.1f)
    {
        float finalAmplitude = rawAmplitude;
        if (DataManager.Instance != null)
            finalAmplitude = DataManager.Instance.GetAdjustedHapticStrength(rawAmplitude);

        if (finalAmplitude <= 0.01f) return;

        var inputDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, inputDevices);

        foreach (var device in inputDevices)
        {
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
                device.SendHapticImpulse(0, finalAmplitude, duration);
        }
    }

    private void HandleRTriggerInput()
    {
        if (this == null || !gameObject.activeInHierarchy) return;
        // 퀴즈 진행 중엔 입력으로 instructionPanel 닫기 차단 — ShowQuizAndWaitForAnswer 데드락 방지
        if (IsAnyQuizActive()) return;
        // 오답 피드백 표시 중 클릭 차단 — 피드백 확인 전 퀴즈 재오픈 방지
        if (_isFeedbackLocked) return;
        bool actionTaken = false;

        if (instructionPanel != null && instructionPanel.activeSelf)
        {
            CloseInstructionPanel();
            SetDisplayPanel(false);
            actionTaken = true;
        }
        else if (_isAButtonEnabled && pausePanel != null && pausePanel.activeSelf)
        {
            OnClickReturnToMenuScene();
            actionTaken = true;
        }
        else if (cautionPanel != null && cautionPanel.activeSelf)
        {
            CloseCautionPanel();
            SetDisplayPanel(false);
            actionTaken = true;
        }
        else if (situationPanel != null && situationPanel.activeSelf)
        {
            CloseSituationPanel();
            SetDisplayPanel(false);
            actionTaken = true;
        }
        if (actionTaken) TriggerInteractionFeedback();
    }

    private void HandleAButtonInput()
    {
        if (this == null || !gameObject.activeInHierarchy) return;
        // 퀴즈 진행 중엔 입력으로 instructionPanel 닫기 차단 — ShowQuizAndWaitForAnswer 데드락 방지
        if (IsAnyQuizActive()) return;
        // 오답 피드백 표시 중 클릭 차단 — 피드백 확인 전 퀴즈 재오픈 방지
        if (_isFeedbackLocked) return;
        bool actionTaken = false;

        if (instructionPanel != null && instructionPanel.activeSelf)
        {
            CloseInstructionPanel();
            SetDisplayPanel(false);
            actionTaken = true;
        }
        else if (_isAButtonEnabled && pausePanel != null && pausePanel.activeSelf)
        {
            OnClickReturnToMenuScene();
            actionTaken = true;
        }
        else if (cautionPanel != null && cautionPanel.activeSelf)
        {
            CloseCautionPanel();
            SetDisplayPanel(false);
            actionTaken = true;
        }
        else if (situationPanel != null && situationPanel.activeSelf)
        {
            CloseSituationPanel();
            SetDisplayPanel(false);
            actionTaken = true;
        }
        if (actionTaken) TriggerInteractionFeedback();
    }

    /// <summary>quizContents 중 활성화된 항목이 있는지 반환 — 퀴즈 진행 중 여부 판정</summary>
    private bool IsAnyQuizActive()
    {
        if (quizContents == null) return false;
        for (int i = 0; i < quizContents.Length; i++)
            if (quizContents[i] != null && quizContents[i].activeSelf) return true;
        return false;
    }

    private void HandleBButtonInput()
    {
        if (this == null || !gameObject.activeInHierarchy) return;

        if (_isBButtonEnabled && pausePanel != null && pausePanel.activeSelf)
            OnClickReturnToGame();
    }

    private void HandleYButtonInput()
    {
        if (this == null || !gameObject.activeInHierarchy) return;
        TriggerInteractionFeedback();
        if (GameManager.Instance != null)
        {
            GameManager.Instance.TogglePause();
            SetDisplayPanel(true);
        }
    }

    /// <summary>일시정지 패널에서 메인 메뉴로 복귀함</summary>
    public void OnClickReturnToMenuScene()
    {
        if (pausePanel == null || !pausePanel.activeSelf) return;
        TriggerInteractionFeedback();
        Time.timeScale = 1f;
        if (AudioManager.Instance != null) AudioManager.Instance.StopAllAudio();
        GameManager.Instance.LoadScene("Main_Intro");
    }

    /// <summary>일시정지 패널에서 게임으로 복귀함</summary>
    public void OnClickReturnToGame()
    {
        if (pausePanel == null || !pausePanel.activeSelf) return;
        TriggerInteractionFeedback();
        if (GameManager.Instance != null) GameManager.Instance.TogglePause();
        SetDisplayPanel(false);
    }

    #endregion

    #region Internal Logic

    /// <summary>비네팅 강도를 목표값까지 부드럽게 보간함</summary>
    /// <param name="targetIntensity">목표 비네팅 강도 (0~1)</param>
    public void SetPressureIntensity(float targetIntensity) { if (pressureVignette == null) return; if (vignetteCoroutine != null) StopCoroutine(vignetteCoroutine); vignetteCoroutine = StartCoroutine(SmoothVignetteRoutine(targetIntensity)); }

    private IEnumerator SmoothVignetteRoutine(float target) { float start = currentVignetteValue; float elapsed = 0f; while (elapsed < vignetteSmoothTime) { elapsed += Time.deltaTime; currentVignetteValue = Mathf.Lerp(start, target, elapsed / vignetteSmoothTime); pressureVignette.SetIntensity(currentVignetteValue); yield return null; } currentVignetteValue = target; pressureVignette.SetIntensity(target); }

    /// <summary>
    /// 압박 레벨에 따라 게이지 UI·사운드·햅틱·비네팅을 갱신함
    /// </summary>
    /// <param name="level">압박 레벨 (0=안전, 1~5=경고~치명)</param>
    public void UpdatePressureGauge(int level)
    {
        if (currentPressureLevel == 0 && level > 0) PlayPressureSounds();
        else if (level == 0) StopAllPressureSounds();

        currentPressureLevel = level;
        UpdatePressureSoundVolume(level);

        if (BodyHaptic.Instance != null)
        {
            if (level == 0) BodyHaptic.Instance.StopBodyHaptics();
            else BodyHaptic.Instance.PlayBodyHaptics(level);
        }

        if (ControllerHaptic.Instance != null)
            ControllerHaptic.Instance.PlayPressureHaptic(level);

        if (pressureStateText)
        {
            int stateIndex = Mathf.Clamp(level, 0, PressureState.Length - 1);
            pressureStateText.text = PressureState[stateIndex];
        }

        float maxLevel = 5.0f;
        float intensity = Mathf.Clamp01((float)level / maxLevel);
        SetPressureIntensity(intensity);

        if (pressureGaugeImages == null) return;
        int targetIndex = level - 1;

        for (int i = 0; i < pressureGaugeImages.Length; i++)
        {
            Image img = pressureGaugeImages[i]; Image highlightImg = pressureHighlightImages[i];
            if (img == null || highlightImg == null) continue;

            if (imageCoroutines.ContainsKey(img) && imageCoroutines[img] != null) StopCoroutine(imageCoroutines[img]);
            if (imageCoroutines.ContainsKey(highlightImg) && imageCoroutines[highlightImg] != null) StopCoroutine(imageCoroutines[highlightImg]);

            bool shouldBeOn = (i < level); bool isPulseTarget = (i == targetIndex);

            if (shouldBeOn) imageCoroutines[img] = StartCoroutine(FadeImageRoutine(img, 1.0f, true, false));
            else imageCoroutines[img] = StartCoroutine(FadeImageRoutine(img, 0.0f, false, false));

            if (isPulseTarget) imageCoroutines[highlightImg] = StartCoroutine(FadeImageRoutine(highlightImg, 1.0f, true, isPulseTarget));
            else imageCoroutines[highlightImg] = StartCoroutine(FadeImageRoutine(highlightImg, 0.0f, false, false));
        }
    }

    private void UpdatePressureSoundVolume(int level)
    {
        if (AudioManager.Instance == null) return;

        // NOTE: 레벨별 독립 볼륨 커브 — index가 압박 레벨, 0은 정지 상태
        float[] heartbeatCurve  = { 0f, 0.44f, 0.58f, 0.72f, 0.86f, 1.00f };
        float[] breathCurve     = { 0f, 0.30f, 0.44f, 0.58f, 0.72f, 0.86f };
        float[] earRingingCurve = { 0f, 0.05f, 0.125f, 0.25f, 0.375f, 0.50f };

        int idx = Mathf.Clamp(level, 0, heartbeatCurve.Length - 1);

        AudioManager.Instance.SetLoopingSFXScale(SFXType.heartbeat,  heartbeatCurve[idx]);
        AudioManager.Instance.SetLoopingSFXScale(SFXType.breath,     breathCurve[idx]);
        AudioManager.Instance.SetLoopingSFXScale(SFXType.EarRinging, earRingingCurve[idx]);
    }

    #endregion

    #region Coroutines

    // cautionAutoCloseDuration 초 후 주의사항 패널을 자동으로 닫음
    private IEnumerator CautionAutoCloseRoutine()
    {
        yield return new WaitForSeconds(cautionAutoCloseDuration);
        if (cautionPanel != null && cautionPanel.activeSelf) CloseCautionPanel();
    }

    /// <summary>
    /// 미션 타이머를 시작하고 완료 조건 충족 시 성공 처리함
    /// </summary>
    /// <param name="missionText">진행률 패널에 표시할 미션 설명</param>
    /// <param name="totalTime">제한 시간(초)</param>
    /// <param name="isMissionCompleteCondition">미션 완료 판정 함수</param>
    /// <param name="progressCalculator">진행률 계산 함수 (null이면 남은 시간 표시)</param>
    /// <param name="isDisplyPanel">true이면 진행률 패널을 자동으로 열고 닫음</param>
    public IEnumerator StartMissionTimer(string missionText, float totalTime, System.Func<bool> isMissionCompleteCondition, System.Func<float> progressCalculator = null, bool isDisplyPanel = false)
    {
        float currentTime = totalTime; float timeSpent = 0f;
        if (progressCalculator != null && progressText) progressText.text = "0 %"; else if (progressText) progressText.text = $"{totalTime} s";
        if (isDisplyPanel) OpenProgressPanel(missionText);
        while (!isMissionCompleteCondition.Invoke())
        {
            currentTime -= Time.deltaTime; timeSpent += Time.deltaTime;
            if (progressCalculator != null) { float currentProgress = progressCalculator.Invoke(); if (progressText) progressText.text = $"{(currentProgress * 100f):F0} %"; if (barSlider) barSlider.fillAmount = currentProgress; }
            else { if (progressText) progressText.text = $"{Mathf.CeilToInt(currentTime)} s"; if (barSlider) barSlider.fillAmount = currentTime / totalTime; }
            yield return null;
        }
        if (DataManager.Instance != null) { DataManager.Instance.AddSuccessCount(); DataManager.Instance.AddPlayTime(timeSpent); }
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.Success_Feedback);
        if (isDisplyPanel) CloseProgressPanel();
    }

    #endregion

    #region Helpers

    private void HandlePauseState(bool isPaused) { if (pausePanel) pausePanel.SetActive(isPaused); if (isPaused && AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.Pause_Feedback); }
    private void FadePanel(GameObject panel, bool show) { if (panel == null) return; CanvasGroup cg = panel.GetComponent<CanvasGroup>(); if (cg == null) cg = panel.AddComponent<CanvasGroup>(); if (panelCoroutines.ContainsKey(panel) && panelCoroutines[panel] != null) StopCoroutine(panelCoroutines[panel]); panelCoroutines[panel] = StartCoroutine(FadePanelRoutine(panel, cg, show)); }
    private IEnumerator FadePanelRoutine(GameObject panel, CanvasGroup cg, bool show) { float targetAlpha = show ? 1.0f : 0.0f; float startAlpha = cg.alpha; float elapsed = 0f; if (show) { panel.SetActive(true); cg.alpha = 0f; startAlpha = 0f; } while (elapsed < panelFadeDuration) { elapsed += Time.deltaTime; cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / panelFadeDuration); yield return null; } cg.alpha = targetAlpha; if (!show) panel.SetActive(false); }
    private IEnumerator FadeImageRoutine(Image targetImage, float targetAlpha, bool activeState, bool startPulseAfterFade) { if (activeState && !targetImage.gameObject.activeSelf) { targetImage.gameObject.SetActive(true); Color c = targetImage.color; targetImage.color = new Color(c.r, c.g, c.b, 0f); } else if (!activeState && !targetImage.gameObject.activeSelf) { yield break; } Color color = targetImage.color; float startAlpha = color.a; float elapsed = 0f; while (elapsed < imageFadeDuration) { elapsed += Time.deltaTime; float newAlpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / imageFadeDuration); targetImage.color = new Color(color.r, color.g, color.b, newAlpha); yield return null; } targetImage.color = new Color(color.r, color.g, color.b, targetAlpha); if (!activeState) { targetImage.gameObject.SetActive(false); } else if (startPulseAfterFade) { cachedOriginalAlpha = targetAlpha; imageCoroutines[targetImage] = StartCoroutine(PulseImageRoutine(targetImage)); } }
    private IEnumerator PulseImageRoutine(Image targetImage) { Color originalColor = targetImage.color; while (true) { float alphaRatio = (Mathf.Sin(Time.time * pulseSpeed) + 1.0f) / 2.0f; float targetAlpha = Mathf.Lerp(minPulseAlpha, cachedOriginalAlpha, alphaRatio); targetImage.color = new Color(originalColor.r, originalColor.g, originalColor.b, targetAlpha); yield return null; } }

    #endregion
}
