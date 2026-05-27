using System;
using UnityEngine;

/// <summary>
/// 사용자 설정(볼륨·햅틱·멀미 모드)과 세션 데이터를 관리하는 싱글톤 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>PlayerPrefs 기반 설정 영구 저장 및 복원</item>
///   <item>게임 플레이 통계(성공·실패 횟수, 플레이 시간) 추적</item>
///   <item>Observer 패턴(Action)으로 볼륨·햅틱·멀미 모드 변경 이벤트 발행</item>
/// </list>
/// </remarks>
public class DataManager : MonoBehaviour
{
    #region Enums / Constants

    private const string KEY_MASTERVOLUME = "MasterVolume";
    private const string KEY_NARVOLUME = "NARVolume";
    private const string KEY_SFXVOLUME = "SFXVolume";
    private const string KEY_AMBVOLUME = "AMBVolume";
    private const string KEY_HAPTIC_INTENSITY = "HapticIntensity";
    private const string KEY_MOTION_SICKNESS = "MotionSickness";

    #endregion

    #region Singleton

    public static DataManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null;
            DontDestroyOnLoad(gameObject);
            LoadSettings();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region Inspector Settings

    [Header("Audio Settings")]
    [SerializeField][Range(0f, 1f)] private float MasterVolume = 1.0f;
    [SerializeField][Range(0f, 1f)] private float NARVolume = 1.0f;
    [SerializeField][Range(0f, 1f)] private float SFXVolume = 1.0f;
    [SerializeField][Range(0f, 1f)] private float AMBVolume = 1.0f;

    [Header("Haptic Settings (Vibration)")]
    [Tooltip("유저가 설정하는 진동 세기 (마스터)")]
    [SerializeField][Range(0f, 1f)] private float HapticIntensity = 1.0f;

    [Tooltip("하드웨어 진동의 최소 임계값")]
    [SerializeField][Range(0f, 1f)] private float MinHapticLimit = 0.0f;

    [Tooltip("하드웨어 진동의 최대 한계값")]
    [SerializeField][Range(0f, 1f)] private float MaxHapticLimit = 1.0f;

    [Header("Game Settings")]
    [Tooltip("멀미 방지 모드 활성화 여부")]
    public bool IsAntiMotionSicknessMode = true;

    #endregion

    #region Events

    /// <summary>마스터 볼륨 변경 시 발행 (float: volume)</summary>
    public event Action<float> OnMasterVolumeChanged;
    /// <summary>NAR 볼륨 변경 시 발행 (float: volume)</summary>
    public event Action<float> OnNARVolumeChanged;
    /// <summary>SFX 볼륨 변경 시 발행 (float: volume)</summary>
    public event Action<float> OnSFXVolumeChanged;
    /// <summary>AMB 볼륨 변경 시 발행 (float: volume)</summary>
    public event Action<float> OnAMBVolumeChanged;
    /// <summary>햅틱 강도 변경 시 발행 (float: intensity)</summary>
    public event Action<float> OnHapticIntensityChanged;
    /// <summary>멀미 방지 모드 변경 시 발행 (bool: isEnabled)</summary>
    public event Action<bool> OnMotionSicknessChanged;

    #endregion

    #region Internal State
    [Header("Session Data")]
    [SerializeField] private int SuccessCount = 0;
    [SerializeField] private int MistakeCount = 0;
    [SerializeField] private float PlayTime = 0f;
    [SerializeField] private string SelectedMap = null;
    #endregion

    #region Public API

    /// <summary>세션 데이터(성공·실패·시간)를 초기화함</summary>
    public void InitializeSessionData() { SuccessCount = 0; MistakeCount = 0; PlayTime = 0f; Debug.Log("[DataManager] Session Data Initialized."); }
    /// <summary>성공 횟수를 1 증가시킴</summary>
    public void AddSuccessCount() => SuccessCount++;
    /// <summary>현재 성공 횟수를 반환함</summary>
    public int GetSuccessCount() => SuccessCount;
    /// <summary>실패 횟수를 1 증가시킴</summary>
    public void AddMistakeCount() => MistakeCount++;
    /// <summary>현재 실패 횟수를 반환함</summary>
    public int GetMistakeCount() => MistakeCount;
    /// <summary>
    /// 플레이 시간을 누적함
    /// </summary>
    /// <param name="timeToAdd">추가할 시간(초)</param>
    public void AddPlayTime(float timeToAdd) => PlayTime += timeToAdd;
    /// <summary>현재 누적 플레이 시간을 반환함</summary>
    public float GetPlayTime() => PlayTime;
    /// <summary>
    /// 선택된 맵 이름을 설정함
    /// </summary>
    /// <param name="value">맵 이름 문자열</param>
    public void SetSelectedMap(string value) => SelectedMap = value;
    /// <summary>현재 선택된 맵 이름을 반환함</summary>
    public string GetSelectedMap() => SelectedMap;

    /// <summary>
    /// 마스터 볼륨을 설정하고 변경 이벤트를 발행함
    /// </summary>
    /// <param name="volume">목표 볼륨 (0.0~1.0)</param>
    public void SetMasterVolume(float volume)
    {
        float newValue = Mathf.Clamp01(volume);
        if (Mathf.Approximately(MasterVolume, newValue)) return;
        MasterVolume = newValue;
        OnMasterVolumeChanged?.Invoke(MasterVolume);
    }
    /// <summary>현재 마스터 볼륨을 반환함</summary>
    public float GetMasterVolume() => MasterVolume;

    /// <summary>
    /// NAR 볼륨을 설정하고 변경 이벤트를 발행함
    /// </summary>
    /// <param name="volume">목표 볼륨 (0.0~1.0)</param>
    public void SetNARVolume(float volume)
    {
        float newValue = Mathf.Clamp01(volume);
        if (Mathf.Approximately(NARVolume, newValue)) return;
        NARVolume = newValue;
        OnNARVolumeChanged?.Invoke(NARVolume);
    }
    /// <summary>현재 NAR 볼륨을 반환함</summary>
    public float GetNARVolume() => NARVolume;

    /// <summary>
    /// SFX 볼륨을 설정하고 변경 이벤트를 발행함
    /// </summary>
    /// <param name="volume">목표 볼륨 (0.0~1.0)</param>
    public void SetSFXVolume(float volume)
    {
        float newValue = Mathf.Clamp01(volume);
        if (Mathf.Approximately(SFXVolume, newValue)) return;
        SFXVolume = newValue;
        OnSFXVolumeChanged?.Invoke(SFXVolume);
    }
    /// <summary>현재 SFX 볼륨을 반환함</summary>
    public float GetSFXVolume() => SFXVolume;

    /// <summary>
    /// AMB 볼륨을 설정하고 변경 이벤트를 발행함
    /// </summary>
    /// <param name="volume">목표 볼륨 (0.0~1.0)</param>
    public void SetAMBVolume(float volume)
    {
        float newValue = Mathf.Clamp01(volume);
        if (Mathf.Approximately(AMBVolume, newValue)) return;
        AMBVolume = newValue;
        OnAMBVolumeChanged?.Invoke(AMBVolume);
    }
    /// <summary>현재 AMB 볼륨을 반환함</summary>
    public float GetAMBVolume() => AMBVolume;

    /// <summary>
    /// 햅틱 강도를 설정하고 변경 이벤트를 발행함
    /// </summary>
    /// <param name="intensity">목표 강도 (0.0~1.0)</param>
    public void SetHapticIntensity(float intensity)
    {
        float newValue = Mathf.Clamp01(intensity);
        if (Mathf.Approximately(HapticIntensity, newValue)) return;
        HapticIntensity = newValue;
        OnHapticIntensityChanged?.Invoke(HapticIntensity);
    }
    /// <summary>현재 햅틱 강도를 반환함</summary>
    public float GetHapticIntensity() => HapticIntensity;

    /// <summary>
    /// 원시 입력 강도를 사용자 설정 기반으로 보정한 햅틱 강도를 반환함
    /// </summary>
    /// <param name="rawInputStrength">원시 입력 강도 (0.0~1.0)</param>
    public float GetAdjustedHapticStrength(float rawInputStrength)
    {
        float input = Mathf.Clamp01(rawInputStrength);
        float effectiveMax = MaxHapticLimit * HapticIntensity;
        float effectiveMin = (HapticIntensity > 0.01f) ? MinHapticLimit : 0f;
        return Mathf.Lerp(effectiveMin, effectiveMax, input);
    }

    /// <summary>
    /// 멀미 방지 모드를 설정하고 변경 이벤트를 발행함
    /// </summary>
    /// <param name="isEnabled">true이면 멀미 방지 모드 활성화</param>
    public void SetMotionSicknessMode(bool isEnabled)
    {
        if (IsAntiMotionSicknessMode == isEnabled) return;
        IsAntiMotionSicknessMode = isEnabled;
        OnMotionSicknessChanged?.Invoke(IsAntiMotionSicknessMode);
    }

    /// <summary>현재 설정값을 PlayerPrefs에 영구 저장함</summary>
    public void SaveSettings()
    {
        PlayerPrefs.SetFloat(KEY_MASTERVOLUME, MasterVolume);
        PlayerPrefs.SetFloat(KEY_NARVOLUME, NARVolume);
        PlayerPrefs.SetFloat(KEY_SFXVOLUME, SFXVolume);
        PlayerPrefs.SetFloat(KEY_AMBVOLUME, AMBVolume);
        PlayerPrefs.SetFloat(KEY_HAPTIC_INTENSITY, HapticIntensity);
        PlayerPrefs.SetInt(KEY_MOTION_SICKNESS, IsAntiMotionSicknessMode ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log("[DataManager] Settings Saved.");
    }

    /// <summary>PlayerPrefs에서 저장된 설정값을 복원함</summary>
    public void LoadSettings()
    {
        MasterVolume = PlayerPrefs.GetFloat(KEY_MASTERVOLUME, 1.0f);
        NARVolume = PlayerPrefs.GetFloat(KEY_NARVOLUME, 1.0f);
        SFXVolume = PlayerPrefs.GetFloat(KEY_SFXVOLUME, 1.0f);
        AMBVolume = PlayerPrefs.GetFloat(KEY_AMBVOLUME, 1.0f);
        HapticIntensity = PlayerPrefs.GetFloat(KEY_HAPTIC_INTENSITY, 1.0f);
        IsAntiMotionSicknessMode = PlayerPrefs.GetInt(KEY_MOTION_SICKNESS, 0) == 1;

        Debug.Log($"[DataManager] Settings Loaded. Haptic: {HapticIntensity}, AntiMotion: {IsAntiMotionSicknessMode}");
    }

    #endregion
}
