using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;

/// <summary>
/// 인트로 씬의 UI 흐름 및 설정 관리 매니저.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>최상위 패널(Intro / Start)과 하위 콘텐츠 패널(Place, Manual, Tips, Setting) 전환 제어</item>
///   <item>DataManager 연동을 통한 볼륨·햅틱·편의 모드 설정 초기화 및 변경</item>
///   <item>팁(Tips) 패널 페이지 넘김 처리</item>
/// </list>
/// </remarks>
public class IntroUIManager : MonoBehaviour
{
    #region Inspector Settings (Panels)
    [Header("Top Level Panels")]
    [SerializeField] private GameObject introPanel;
    [SerializeField] private GameObject startPanel;

    [Header("Sub Panels")]
    [SerializeField] private GameObject placePanel;
    [SerializeField] private Image placeButton;
    [SerializeField] private GameObject manualPanel;
    [SerializeField] private Image manualButton;
    [SerializeField] private GameObject tipsPanel;
    [SerializeField] private Image tipsButton;
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private Image settingButton;

    [Header("Place Panels Elements")]
    [SerializeField] private GameObject place1Border;
    [SerializeField] private GameObject place1Panel;
    [SerializeField] private GameObject place2Border;
    [SerializeField] private GameObject place2Panel;
    #endregion

    #region Inspector Settings (UI Elements)
    [Header("Tips Elements")]
    [SerializeField] private GameObject tip1;
    [SerializeField] private GameObject tip2;
    [SerializeField] private GameObject tip3;
    [SerializeField] private GameObject tip4;
    [SerializeField] private GameObject tip5;
    [SerializeField] private TextMeshProUGUI tipPageText;

    [Header("Settings - Audio")]
    [SerializeField] private Slider MasterVolumeSlider;
    [SerializeField] private Slider NARVolumeSlider;
    [SerializeField] private Slider SFXVolumeSlider;
    [SerializeField] private Slider AMBVolumeSlider;
    [SerializeField] private TextMeshProUGUI MasterVolumeText;
    [SerializeField] private TextMeshProUGUI NARVolumeText;
    [SerializeField] private TextMeshProUGUI SFXVolumeText;
    [SerializeField] private TextMeshProUGUI AMBVolumeText;

    [Header("Settings - Haptic")]
    [Tooltip("진동 세기 조절 슬라이더")]
    [SerializeField] private Slider HandHapticVolumeSlider;
    [Tooltip("진동 세기 텍스트 (0-100)")]
    [SerializeField] private TextMeshProUGUI HandHapticVolumeText;

    [Header("Settings - Lang & Mode")]
    [Tooltip("멀미 방지 모드 슬라이더")]
    [SerializeField] private Slider modeSlider;
    [SerializeField] private TextMeshProUGUI modeText;

    [Header("Debug")]
    [SerializeField] private bool isDebug = true;
    #endregion

    #region Internal State
    private GameObject currentTopPanel;
    private GameObject currentMainPanel;
    private Image currentMainButton;
    private int tipPageNum = 1;
    #endregion

    #region Unity Lifecycle
    private void Start()
    {
        InitializeSettings();
        InitializeUIState();
    }
    #endregion

    #region Initialization

    private void InitializeSettings()
    {
        SetupSlider(MasterVolumeSlider, 0, 100);
        SetupSlider(NARVolumeSlider, 0, 100);
        SetupSlider(SFXVolumeSlider, 0, 100);
        SetupSlider(AMBVolumeSlider, 0, 100);

        SetupSlider(HandHapticVolumeSlider, 0, 100);

        if (DataManager.Instance != null)
        {
            SetSliderValueAndListener(MasterVolumeSlider, DataManager.Instance.GetMasterVolume() * 100f, OnMasterVolumeSliderValueChanged);
            SetSliderValueAndListener(NARVolumeSlider, DataManager.Instance.GetNARVolume() * 100f, OnNARVolumeSliderValueChanged);
            SetSliderValueAndListener(SFXVolumeSlider, DataManager.Instance.GetSFXVolume() * 100f, OnSFXVolumeSliderValueChanged);
            SetSliderValueAndListener(AMBVolumeSlider, DataManager.Instance.GetAMBVolume() * 100f, OnAMBVolumeSliderValueChanged);

            SetSliderValueAndListener(HandHapticVolumeSlider, DataManager.Instance.GetHapticIntensity() * 100f, OnHapticVolumeSliderValueChanged);
        }

        SetupSlider(modeSlider, 0, 1);
        bool isModeOn = DataManager.Instance != null && DataManager.Instance.IsAntiMotionSicknessMode;
        modeSlider.value = isModeOn ? 1 : 0;
        OnModeSliderValueChanged(isModeOn ? 1 : 0);
        modeSlider.onValueChanged.AddListener(OnModeSliderValueChanged);
    }

    private void SetupSlider(Slider slider, float min, float max)
    {
        if (slider == null) return;
        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = true;
    }

    private void SetSliderValueAndListener(Slider slider, float value, UnityEngine.Events.UnityAction<float> action)
    {
        if (slider == null) return;
        int intVal = Mathf.RoundToInt(value);
        slider.value = intVal;
        action.Invoke(intVal);
        slider.onValueChanged.AddListener(action);
    }

    private void InitializeUIState()
    {
        if (placePanel) placePanel.SetActive(false);
        if (manualPanel) manualPanel.SetActive(false);
        if (tipsPanel) tipsPanel.SetActive(false);
        if (settingPanel) settingPanel.SetActive(false);

        if (startPanel) startPanel.SetActive(false);
        if (introPanel) introPanel.SetActive(true);

        currentTopPanel = introPanel;
        currentMainPanel = null;
    }

    #endregion

    #region Public API
    /// <summary>인트로 버튼 클릭 시 Start 패널로 전환 후 장소 선택 패널을 표시함.</summary>
    public void OnClickIntroButton()
    {
        PlayUIInteraction();
        if (isDebug) Debug.Log("IntroButton Clicked");
        SwitchTopPanel(startPanel);
        SwitchMainPanel(placePanel, placeButton);
    }
    /// <summary>장소 선택 패널 활성화.</summary>
    public void OnClickPlaceButton() { PlayUIInteraction(); SwitchMainPanel(placePanel, placeButton); }
    /// <summary>매뉴얼 패널 활성화.</summary>
    public void OnClickManualButton() { PlayUIInteraction(); SwitchMainPanel(manualPanel, manualButton); }
    /// <summary>설정 패널 활성화.</summary>
    public void OnClickSettingButton() { PlayUIInteraction(); SwitchMainPanel(settingPanel, settingButton); }
    /// <summary>팁 패널 활성화 및 첫 페이지로 초기화.</summary>
    public void OnClickTipsButton() { PlayUIInteraction(); SwitchMainPanel(tipsPanel, tipsButton); ResetTipPage(); }

    /// <summary>Subway 맵 선택 — place1 비활성화, place2 활성화.</summary>
    public void OnClickPlace1()
    {
        PlayUIInteraction();
        if (place2Panel && place2Border && place1Panel && place1Border)
        {
            place2Panel.SetActive(true); place2Border.SetActive(false);
            place1Panel.SetActive(false); place1Border.SetActive(true);
            if (DataManager.Instance != null) DataManager.Instance.SetSelectedMap("Subway");
        }
    }
    /// <summary>Street 맵 선택 — place2 비활성화, place1 활성화.</summary>
    public void OnClickPlace2()
    {
        PlayUIInteraction();
        if (place2Panel && place2Border && place1Panel && place1Border)
        {
            place1Panel.SetActive(true); place1Border.SetActive(false);
            place2Panel.SetActive(false); place2Border.SetActive(true);
            if (DataManager.Instance != null) DataManager.Instance.SetSelectedMap("Street");
        }
    }
    /// <summary>
    /// 설정 저장 후 선택된 맵 씬을 로드함.
    /// Street 맵만 진입 가능하며, GameManager → SceneTransitionManager → SceneManager 순으로 폴백함.
    /// </summary>
    public void OnClickPlayButton()
    {
        PlayUIInteraction();
        string selectedMap = DataManager.Instance != null ? DataManager.Instance.GetSelectedMap() : "";
        string targetScene = selectedMap switch
        {
            "Street" => "Main_Street",
            "Subway" => "Main_Subway",
            _ => ""
        };
        if (string.IsNullOrEmpty(targetScene)) return;

        if (DataManager.Instance != null) DataManager.Instance.SaveSettings();

        if (GameManager.Instance != null) GameManager.Instance.LoadScene(targetScene);
        else if (SceneTransitionManager.Instance != null) SceneTransitionManager.Instance.LoadScene(targetScene);
        else UnityEngine.SceneManagement.SceneManager.LoadScene(targetScene);
    }
    #endregion

    #region Public API (Tips)
    /// <summary>이전 팁 페이지로 이동.</summary>
    public void OnClickPreTipButton() { PlayUIInteraction(); if (tipPageNum > 1) UpdateTipsPanelPage(tipPageNum - 1); }
    /// <summary>다음 팁 페이지로 이동.</summary>
    public void OnClickNextTipButton() { PlayUIInteraction(); if (tipPageNum < 5) UpdateTipsPanelPage(tipPageNum + 1); }
    private void ResetTipPage() => UpdateTipsPanelPage(1);
    private void UpdateTipsPanelPage(int newPage)
    {
        tipPageNum = newPage;
        tipPageText.text = $"{tipPageNum}/5";
        if (tip1) tip1.SetActive(tipPageNum == 1);
        if (tip2) tip2.SetActive(tipPageNum == 2);
        if (tip3) tip3.SetActive(tipPageNum == 3);
        if (tip4) tip4.SetActive(tipPageNum == 4);
        if (tip5) tip5.SetActive(tipPageNum == 5);
    }
    #endregion

    #region Internal Logic

    private void OnMasterVolumeSliderValueChanged(float value)
    {
        int intValue = Mathf.RoundToInt(value);
        if (MasterVolumeText != null) MasterVolumeText.text = intValue.ToString();
        if (DataManager.Instance != null) DataManager.Instance.SetMasterVolume(intValue / 100f);
    }
    private void OnNARVolumeSliderValueChanged(float value)
    {
        int intValue = Mathf.RoundToInt(value);
        if (NARVolumeText != null) NARVolumeText.text = intValue.ToString();
        if (DataManager.Instance != null) DataManager.Instance.SetNARVolume(intValue / 100f);
    }
    private void OnSFXVolumeSliderValueChanged(float value)
    {
        int intValue = Mathf.RoundToInt(value);
        if (SFXVolumeText != null) SFXVolumeText.text = intValue.ToString();
        if (DataManager.Instance != null) DataManager.Instance.SetSFXVolume(intValue / 100f);
    }
    private void OnAMBVolumeSliderValueChanged(float value)
    {
        int intValue = Mathf.RoundToInt(value);
        if (AMBVolumeText != null) AMBVolumeText.text = intValue.ToString();
        if (DataManager.Instance != null) DataManager.Instance.SetAMBVolume(intValue / 100f);
    }

    private void OnHapticVolumeSliderValueChanged(float value)
    {
        int intValue = Mathf.RoundToInt(value);
        if (HandHapticVolumeText != null) HandHapticVolumeText.text = intValue.ToString();
        if (DataManager.Instance != null) DataManager.Instance.SetHapticIntensity(intValue / 100f);
    }

    private void OnModeSliderValueChanged(float value)
    {
        int intValue = Mathf.RoundToInt(value);
        bool isModeOn = (intValue == 1);
        if (modeText != null) modeText.text = isModeOn ? "ON" : "OFF";

        // NOTE: DataManager에 저장 후 이벤트 발행 → PlayerManager가 수신
        if (DataManager.Instance != null) DataManager.Instance.SetMotionSicknessMode(isModeOn);
    }

    #endregion

    #region Helpers

    private void PlayUIInteraction()
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlaySFX(SFXType.UI_Click);
        }
        TriggerHapticImpulse();
    }

    private void TriggerHapticImpulse(float rawAmplitude = 0.5f, float duration = 0.1f)
    {
        float finalAmplitude = rawAmplitude;
        if (DataManager.Instance != null)
        {
            finalAmplitude = DataManager.Instance.GetAdjustedHapticStrength(rawAmplitude);
        }

        if (finalAmplitude <= 0.01f) return;

        var inputDevices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, inputDevices);

        foreach (var device in inputDevices)
        {
            if (device.TryGetHapticCapabilities(out var capabilities) && capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0, finalAmplitude, duration);
            }
        }
    }
    #endregion

    #region Helpers (Panel Switching)
    private void SwitchTopPanel(GameObject panelToActivate)
    {
        if (currentTopPanel == panelToActivate) return;
        if (currentTopPanel != null) currentTopPanel.SetActive(false);
        panelToActivate.SetActive(true);
        currentTopPanel = panelToActivate;
    }
    private void SwitchMainPanel(GameObject panelToActivate, Image buttonToActivate)
    {
        if (currentMainPanel == panelToActivate) return;
        if (currentMainPanel == tipsPanel)
        {
            if (tip1) tip1.SetActive(false); if (tip2) tip2.SetActive(false);
            if (tip3) tip3.SetActive(false); if (tip4) tip4.SetActive(false); if (tip5) tip5.SetActive(false);
        }
        if (currentMainPanel == placePanel)
        {
            if (place2Panel) place2Panel.SetActive(true); if (place2Border) place2Border.SetActive(false);
            if (place1Panel) place1Panel.SetActive(true); if (place1Border) place1Border.SetActive(false);
        }
        if (currentMainPanel != null) currentMainPanel.SetActive(false);
        if (currentMainButton != null)
        {
            Color c = currentMainButton.color; c.a = 0.0f; currentMainButton.color = c;
        }
        panelToActivate.SetActive(true);
        currentMainPanel = panelToActivate;
        if (buttonToActivate != null)
        {
            Color c = buttonToActivate.color; c.a = 1.0f; buttonToActivate.color = c;
            currentMainButton = buttonToActivate;
        }
    }
    #endregion
}
