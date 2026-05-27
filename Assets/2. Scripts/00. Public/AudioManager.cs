using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GameManager;
using static GameStepManager;
using static SubwayStepManager;

#region Enums
/// <summary>효과음 종류</summary>
public enum SFXType
{
    /// <summary>UI 버튼 클릭음</summary>
    UI_Click,
    /// <summary>미션 성공 피드백음</summary>
    Success_Feedback,
    /// <summary>미션 실패 피드백음</summary>
    Fail_Feedback,
    /// <summary>일시정지 피드백음</summary>
    Pause_Feedback,
    /// <summary>게임 종료 피드백음</summary>
    Finish_Feedback,
    /// <summary>심박수 효과음 — 루프 재생</summary>
    heartbeat,
    /// <summary>거친 호흡 효과음 — 루프 재생</summary>
    breath,
    /// <summary>이명 효과음</summary>
    EarRinging,

    /// <summary>구급차 사이렌</summary>
    Ambulance,
    /// <summary>경찰차 사이렌 — 셔플 재생</summary>
    Police,

    /// <summary>재생 없음 (기본값)</summary>
    None
}

/// <summary>환경음 종류</summary>
public enum AMBType
{
    /// <summary>군중 환경음</summary>
    Crowd,
    /// <summary>재생 없음 (기본값)</summary>
    None
}
#endregion

#region Data Structures
/// <summary>SFX 클립 데이터 — Inspector에서 타입별 클립 목록을 설정함</summary>
[Serializable]
public struct SFXData
{
    public SFXType type;
    public List<AudioClip> clips;
}

/// <summary>AMB 클립 데이터 — Inspector에서 타입별 클립 목록을 설정함</summary>
[Serializable]
public struct AMBData
{
    public AMBType type;
    public List<AudioClip> clips;
}

/// <summary>반복 재생 SFX 관리 컨테이너 — 페이드 및 볼륨 스케일 상태를 보유함</summary>
public class LoopingSFXContainer
{
    public AudioSource source;
    public float fadeFactor;
    public float volumeScale;

    public LoopingSFXContainer(AudioSource src, float initialFade)
    {
        source = src;
        fadeFactor = initialFade;
        volumeScale = 1.0f;
    }
}
#endregion

/// <summary>
/// NAR·SFX·AMB 오디오 재생을 총괄하는 싱글톤 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>NAR(내레이션) 씬·페이즈별 클립 선택 및 재생</item>
///   <item>SFX 단발·루프·셔플 재생 및 페이드 인/아웃</item>
///   <item>AMB 크로스페이드 전환 및 페이드 아웃</item>
///   <item>DataManager 볼륨 설정값 실시간 반영</item>
/// </list>
/// </remarks>
public class AudioManager : MonoBehaviour
{
    #region Singleton
    /// <summary>전역 싱글톤 인스턴스</summary>
    public static AudioManager Instance { get; private set; }
    #endregion

    #region Inspector Settings

    [Header("Debug Settings")]
    [SerializeField] private bool isDebug = true;

    [Header("Audio Sources")]
    public AudioSource narSource;
    public AudioSource sfxSource;

    [Header("AMB Sources (Cross-Fade)")]
    public AudioSource ambSourceA;
    public AudioSource ambSourceB;

    [Header("Clip Data")]
    public List<SFXData> sfxList = new List<SFXData>();
    public List<AMBData> ambList = new List<AMBData>();

    [Header("NAR Clips - 스트릿씬")]
    public AudioClip[] nar_tip;
    public AudioClip[] nar_Caution;
    public AudioClip[] nar_Tutorial;
    public AudioClip[] nar_Move;
    public AudioClip[] nar_ABCPose;
    public AudioClip[] nar_HoldPillar;
    public AudioClip[] nar_ClimbUp;
    public AudioClip[] nar_Escape;
    public AudioClip[] nar_Finished;

    [Header("NAR Clips - 지하철씬")]
    public AudioClip[] subway_nar_Caution;
    public AudioClip[] subway_nar_ABCPose_Briefcase;
    public AudioClip[] subway_nar_BriefcaseQuiz;
    public AudioClip[] subway_nar_FlowMove;
    public AudioClip[] subway_nar_HoldPillar;
    public AudioClip[] subway_nar_StairMove;
    public AudioClip[] subway_nar_FallABCPose;
    public AudioClip[] subway_nar_TicketGate;
    public AudioClip[] subway_nar_Emergency119;
    public AudioClip[] subway_nar_EscalatorEscape;
    public AudioClip[] subway_nar_Finished;

    [Header("Settings")]
    [SerializeField] private float defaultFadeTime = 1.0f;

    #endregion

    #region Internal State

    /// <summary>SFXType → 클립 목록 매핑 딕셔너리</summary>
    private Dictionary<SFXType, List<AudioClip>> _sfxMap = new Dictionary<SFXType, List<AudioClip>>();
    /// <summary>AMBType → 클립 목록 매핑 딕셔너리</summary>
    private Dictionary<AMBType, List<AudioClip>> _ambMap = new Dictionary<AMBType, List<AudioClip>>();

    /// <summary>현재 활성화된 루핑/셔플 SFX 컨테이너 맵</summary>
    private Dictionary<SFXType, LoopingSFXContainer> _activeLoopingSFX = new Dictionary<SFXType, LoopingSFXContainer>();
    /// <summary>루핑·셔플 SFX 재사용 풀 — AddComponent/Destroy 반복에 의한 GC 방지</summary>
    private Queue<AudioSource> _audioSourcePool = new Queue<AudioSource>();
    /// <summary>활성 셔플 코루틴 맵 — 정지 시 참조용</summary>
    private Dictionary<SFXType, Coroutine> _activeShuffleRoutines = new Dictionary<SFXType, Coroutine>();
    /// <summary>활성 볼륨 보간 코루틴 맵 — 중복 방지용</summary>
    private Dictionary<SFXType, Coroutine> _activeSmoothVolumeRoutines = new Dictionary<SFXType, Coroutine>();

    private Coroutine _ambCrossFadeCoroutine;
    private bool _isUsingSourceA = false;
    private float _fadeFactorA = 0f;
    private float _fadeFactorB = 0f;

    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null;
            DontDestroyOnLoad(gameObject);
            InitializeDictionaries();
            InitializePool();

            if (ambSourceA != null) ambSourceA.loop = true;
            if (ambSourceB != null) ambSourceB.loop = true;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Update()
    {
        if (DataManager.Instance == null) return;

        float masterVol = DataManager.Instance.GetMasterVolume();
        float narVol = DataManager.Instance.GetNARVolume();
        float sfxVol = DataManager.Instance.GetSFXVolume();
        float ambVol = DataManager.Instance.GetAMBVolume();

        narSource.volume = masterVol * narVol;
        sfxSource.volume = masterVol * sfxVol;

        if (ambSourceA.isPlaying) ambSourceA.volume = masterVol * ambVol * _fadeFactorA;
        if (ambSourceB.isPlaying) ambSourceB.volume = masterVol * ambVol * _fadeFactorB;

        // NOTE: volumeScale을 곱해야 점진적 볼륨 증가 효과가 반영됨
        if (_activeLoopingSFX.Count > 0)
        {
            foreach (var kvp in _activeLoopingSFX)
            {
                LoopingSFXContainer container = kvp.Value;
                if (container != null && container.source != null)
                {
                    container.source.volume = masterVol * sfxVol * container.fadeFactor * container.volumeScale;
                }
            }
        }
    }
    #endregion

    #region Initialization
    // NOTE: 씬 전환 시 재사용을 위해 고정 크기로 AudioSource를 사전 할당 (heartbeat/breath/EarRinging 등 최대 동시 루핑 수 기준)
    private void InitializePool()
    {
        for (int i = 0; i < 8; i++)
        {
            AudioSource src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            _audioSourcePool.Enqueue(src);
        }
    }

    private void InitializeDictionaries()
    {
        _sfxMap.Clear();
        foreach (var item in sfxList)
        {
            if (!_sfxMap.ContainsKey(item.type)) _sfxMap.Add(item.type, item.clips);
        }

        _ambMap.Clear();
        foreach (var item in ambList)
        {
            if (!_ambMap.ContainsKey(item.type)) _ambMap.Add(item.type, item.clips);
        }
    }
    #endregion

    #region Public API

    /// <summary>
    /// 씬·페이즈 조합으로 NAR 클립을 선택하여 재생함
    /// </summary>
    /// <param name="scene">현재 게임 씬</param>
    /// <param name="phase">현재 게임 페이즈</param>
    /// <param name="num">페이즈 내 클립 인덱스</param>
    /// <param name="tipPage">메뉴 팁 페이지 인덱스</param>
    public void PlayNAR(GameScene scene, GamePhase phase = GamePhase.Null, int num = 0, int tipPage = 0)
    {
        AudioClip clip = GetNarClip(scene, phase, num, tipPage);
        // clip이 null이어도 이전 나레이션을 먼저 정지해야 함 (잔여 재생 방지)
        StopNAR();
        if (clip != null) PlayNAR(clip);
    }

    /// <summary>
    /// 지정된 AudioClip으로 NAR을 즉시 재생함
    /// </summary>
    /// <param name="clip">재생할 NAR 클립</param>
    public void PlayNAR(AudioClip clip)
    {
        if (narSource.isPlaying) narSource.Stop();
        narSource.clip = clip;
        narSource.Play();
    }

    /// <summary>
    /// 지하철 씬 SubwayPhase 조합으로 NAR 클립을 선택하여 재생함
    /// </summary>
    /// <param name="phase">현재 지하철 씬 페이즈</param>
    /// <param name="num">페이즈 내 클립 인덱스</param>
    public void PlayNAR(SubwayPhase phase, int num = 0)
    {
        AudioClip clip = GetNarSubwayClip(phase, num);
        StopNAR();
        if (clip != null) PlayNAR(clip);
    }

    /// <summary>현재 재생 중인 NAR을 즉시 정지함</summary>
    public void StopNAR() => narSource.Stop();

    /// <summary>NAR이 현재 재생 중인지 반환함</summary>
    public bool IsNARPlaying() => narSource != null && narSource.isPlaying;

    /// <summary>
    /// 지정된 SFX를 재생함
    /// </summary>
    /// <param name="type">재생할 SFX 종류</param>
    /// <param name="isLoop">true이면 루프 재생</param>
    /// <param name="useFade">true이면 페이드 인/아웃 적용</param>
    public void PlaySFX(SFXType type, bool isLoop = false, bool useFade = false)
    {
        if (!_sfxMap.ContainsKey(type)) return;
        List<AudioClip> clips = _sfxMap[type];
        if (clips == null || clips.Count == 0) return;

        AudioClip clip = clips[0];

        if (isLoop)
        {
            if (_activeLoopingSFX.ContainsKey(type)) return;

            // CHANGE: AddComponent 대신 풀에서 꺼냄 — 씬 전환 시 GC 스파이크 방지
            AudioSource loopSource = GetPooledSource();
            loopSource.clip = clip;
            loopSource.loop = true;

            float startFactor = useFade ? 0f : 1f;
            LoopingSFXContainer container = new LoopingSFXContainer(loopSource, startFactor);
            _activeLoopingSFX.Add(type, container);

            loopSource.Play();

            if (useFade) StartCoroutine(FadeLoopingSFXRoutine(type, 0f, 1f, defaultFadeTime));
        }
        else
        {
            if (useFade) StartCoroutine(PlayOneShotWithFadeRoutine(clip, defaultFadeTime));
            else sfxSource.PlayOneShot(clip, sfxSource.volume);
        }
    }

    /// <summary>
    /// 여러 클립을 랜덤 셔플하여 무한 반복 재생함
    /// </summary>
    /// <param name="type">재생할 SFX 종류</param>
    /// <param name="useFade">true이면 페이드 인 적용</param>
    public void PlayShuffleSFX(SFXType type, bool useFade = true)
    {
        if (!_sfxMap.ContainsKey(type)) return;
        if (_activeLoopingSFX.ContainsKey(type)) return;

        List<AudioClip> clips = _sfxMap[type];
        if (clips == null || clips.Count == 0) return;

        // CHANGE: AddComponent 대신 풀에서 꺼냄 — 씬 전환 시 GC 스파이크 방지
        AudioSource loopSource = GetPooledSource();
        loopSource.loop = false;

        float startFactor = useFade ? 0f : 1f;
        LoopingSFXContainer container = new LoopingSFXContainer(loopSource, startFactor);
        _activeLoopingSFX.Add(type, container);

        Coroutine routine = StartCoroutine(ShuffleSFXLogic(type, loopSource, clips));
        _activeShuffleRoutines.Add(type, routine);

        if (useFade) StartCoroutine(FadeLoopingSFXRoutine(type, 0f, 1f, defaultFadeTime));
    }

    /// <summary>
    /// 지정된 SFX 재생을 정지함
    /// </summary>
    /// <param name="type">정지할 SFX 종류</param>
    /// <param name="useFade">true이면 페이드 아웃 후 정지</param>
    public void StopSFX(SFXType type, bool useFade = false)
    {
        if (_activeShuffleRoutines.ContainsKey(type))
        {
            StopCoroutine(_activeShuffleRoutines[type]);
            _activeShuffleRoutines.Remove(type);
        }

        if (_activeLoopingSFX.ContainsKey(type))
        {
            if (useFade) StartCoroutine(FadeLoopingSFXRoutine(type, 1f, 0f, defaultFadeTime, true));
            else RemoveLoopingSource(type);
        }
    }

    /// <summary>모든 SFX 재생을 즉시 정지함</summary>
    public void StopAllSFX()
    {
        sfxSource.Stop();
        List<SFXType> keys = new List<SFXType>(_activeLoopingSFX.Keys);
        foreach (var key in keys) StopSFX(key, false);
    }

    /// <summary>
    /// 루핑 SFX의 볼륨 스케일을 보간하여 변경함
    /// </summary>
    /// <param name="type">대상 SFX 종류</param>
    /// <param name="scale">목표 볼륨 스케일 (0.0~1.0)</param>
    public void SetLoopingSFXScale(SFXType type, float scale)
    {
        if (_activeLoopingSFX.TryGetValue(type, out var container))
        {
            // NOTE: 이전 보간 코루틴이 남아있으면 중복 방지를 위해 정지
            if (_activeSmoothVolumeRoutines.TryGetValue(type, out var existing) && existing != null)
                StopCoroutine(existing);
            _activeSmoothVolumeRoutines[type] = StartCoroutine(SmoothVolumeScaleRoutine(container, scale, 1.0f));
        }
    }

    /// <summary>
    /// 지정된 AMBType의 클립을 크로스페이드로 재생함
    /// </summary>
    /// <param name="type">재생할 AMB 종류</param>
    /// <param name="num">클립 인덱스</param>
    public void PlayAMB(AMBType type, int num = 0)
    {
        if (!_ambMap.ContainsKey(type)) return;
        List<AudioClip> clips = _ambMap[type];
        if (clips == null || clips.Count == 0) return;

        int idx = Mathf.Clamp(num, 0, clips.Count - 1);
        PlayAMB(clips[idx]);
    }

    /// <summary>
    /// 지정된 AudioClip으로 AMB를 크로스페이드 전환함
    /// </summary>
    /// <param name="nextClip">전환할 AMB 클립</param>
    public void PlayAMB(AudioClip nextClip)
    {
        if (ambSourceA.isPlaying && ambSourceA.clip == nextClip && _isUsingSourceA) return;
        if (ambSourceB.isPlaying && ambSourceB.clip == nextClip && !_isUsingSourceA) return;

        if (_ambCrossFadeCoroutine != null) StopCoroutine(_ambCrossFadeCoroutine);

        AudioSource incoming = _isUsingSourceA ? ambSourceB : ambSourceA;
        AudioSource outgoing = _isUsingSourceA ? ambSourceA : ambSourceB;
        _isUsingSourceA = !_isUsingSourceA;

        _ambCrossFadeCoroutine = StartCoroutine(CrossFadeAMBRoutine(incoming, outgoing, nextClip, defaultFadeTime));
    }

    /// <summary>
    /// 모든 AMB를 페이드 아웃하여 정지함
    /// </summary>
    /// <param name="duration">페이드 아웃 시간(초)</param>
    public void StopAMB(float duration = 1.0f)
    {
        if (_ambCrossFadeCoroutine != null) StopCoroutine(_ambCrossFadeCoroutine);
        _ambCrossFadeCoroutine = StartCoroutine(FadeOutAllAMBRoutine(duration));
    }

    #endregion

    #region Internal Logic

    private AudioClip GetNarSubwayClip(SubwayPhase phase, int num)
    {
        switch (phase)
        {
            case SubwayPhase.Caution:           return GetSafeClip(subway_nar_Caution, num);
            case SubwayPhase.ABCPose_Briefcase: return GetSafeClip(subway_nar_ABCPose_Briefcase, num);
            case SubwayPhase.BriefcaseQuiz:     return GetSafeClip(subway_nar_BriefcaseQuiz, num);
            case SubwayPhase.FlowMove:          return GetSafeClip(subway_nar_FlowMove, num);
            case SubwayPhase.HoldPillar:        return GetSafeClip(subway_nar_HoldPillar, num);
            case SubwayPhase.StairMove:         return GetSafeClip(subway_nar_StairMove, num);
            case SubwayPhase.FallABCPose:       return GetSafeClip(subway_nar_FallABCPose, num);
            case SubwayPhase.TicketGate:        return GetSafeClip(subway_nar_TicketGate, num);
            case SubwayPhase.Emergency119:      return GetSafeClip(subway_nar_Emergency119, num);
            case SubwayPhase.EscalatorEscape:   return GetSafeClip(subway_nar_EscalatorEscape, num);
            case SubwayPhase.Finished:          return GetSafeClip(subway_nar_Finished, num);
            default: return null;
        }
    }

    private AudioClip GetNarClip(GameScene scene, GamePhase phase, int num, int tipPage)
    {
        switch (scene)
        {
            case GameScene.Menu: return GetSafeClip(nar_tip, tipPage);
            case GameScene.Simulator:
                switch (phase)
                {
                    case GamePhase.Caution: return GetSafeClip(nar_Caution, num);
                    case GamePhase.Tutorial: return GetSafeClip(nar_Tutorial, num);
                    case GamePhase.Move1: return GetSafeClip(nar_Move, num);
                    case GamePhase.ABCPose: return GetSafeClip(nar_ABCPose, num);
                    case GamePhase.HoldPillar: return GetSafeClip(nar_HoldPillar, num);
                    case GamePhase.Move2: return GetSafeClip(nar_Move, num);
                    case GamePhase.ClimbUp: return GetSafeClip(nar_ClimbUp, num);
                    case GamePhase.Escape: return GetSafeClip(nar_Escape, num);
                    case GamePhase.Finished: return GetSafeClip(nar_Finished, num);
                }
                break;
        }
        return null;
    }

    private void RemoveLoopingSource(SFXType type)
    {
        if (_activeLoopingSFX.TryGetValue(type, out LoopingSFXContainer container))
        {
            if (container.source != null)
            {
                // CHANGE: Destroy 대신 풀에 반환 — GC 방지
                ReturnToPool(container.source);
            }
            _activeLoopingSFX.Remove(type);
        }
    }

    #endregion

    #region Coroutines

    /// <summary>
    /// AMB 소스 A↔B 크로스페이드를 수행함.
    /// 완료 시 outSource를 정지하고 코루틴 참조를 해제함.
    /// </summary>
    private IEnumerator CrossFadeAMBRoutine(AudioSource inSource, AudioSource outSource, AudioClip nextClip, float duration)
    {
        inSource.clip = nextClip;
        inSource.loop = true;
        inSource.Play();

        float timer = 0f;
        float startOutFactor = (outSource == ambSourceA) ? _fadeFactorA : _fadeFactorB;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float ratio = timer / duration;
            if (inSource == ambSourceA) _fadeFactorA = Mathf.Lerp(0f, 1f, ratio); else _fadeFactorB = Mathf.Lerp(0f, 1f, ratio);
            if (outSource == ambSourceA) _fadeFactorA = Mathf.Lerp(startOutFactor, 0f, ratio); else _fadeFactorB = Mathf.Lerp(startOutFactor, 0f, ratio);
            yield return null;
        }
        if (inSource == ambSourceA) _fadeFactorA = 1f; else _fadeFactorB = 1f;
        if (outSource == ambSourceA) _fadeFactorA = 0f; else _fadeFactorB = 0f;
        outSource.Stop();
        _ambCrossFadeCoroutine = null;
    }

    /// <summary>
    /// 모든 AMB 소스를 동시에 페이드 아웃함.
    /// 완료 시 양쪽 소스를 정지하고 코루틴 참조를 해제함.
    /// </summary>
    private IEnumerator FadeOutAllAMBRoutine(float duration)
    {
        float timer = 0f;
        float startA = _fadeFactorA;
        float startB = _fadeFactorB;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            float ratio = timer / duration;
            _fadeFactorA = Mathf.Lerp(startA, 0f, ratio);
            _fadeFactorB = Mathf.Lerp(startB, 0f, ratio);
            yield return null;
        }
        _fadeFactorA = 0f; _fadeFactorB = 0f;
        ambSourceA.Stop(); ambSourceB.Stop();
        _ambCrossFadeCoroutine = null;
    }

    /// <summary>
    /// 루핑 SFX의 fadeFactor를 보간함.
    /// isStopAfter가 true이면 페이드 완료 후 소스를 제거함.
    /// </summary>
    private IEnumerator FadeLoopingSFXRoutine(SFXType type, float startFactor, float endFactor, float duration, bool isStopAfter = false)
    {
        if (!_activeLoopingSFX.ContainsKey(type)) yield break;
        LoopingSFXContainer container = _activeLoopingSFX[type];
        float timer = 0f;
        while (timer < duration)
        {
            if (container == null || container.source == null) yield break;
            timer += Time.deltaTime;
            container.fadeFactor = Mathf.Lerp(startFactor, endFactor, timer / duration);
            yield return null;
        }
        container.fadeFactor = endFactor;
        if (isStopAfter) RemoveLoopingSource(type);
    }

    /// <summary>
    /// 클립 목록에서 랜덤 셔플로 무한 반복 재생함.
    /// 소스가 파괴되면 종료함.
    /// </summary>
    private IEnumerator ShuffleSFXLogic(SFXType type, AudioSource source, List<AudioClip> clips)
    {
        while (true)
        {
            AudioClip nextClip = clips[UnityEngine.Random.Range(0, clips.Count)];
            source.clip = nextClip;
            source.Play();

            yield return new WaitForSeconds(nextClip.length);

            if (source == null) yield break;
        }
    }

    private IEnumerator SmoothVolumeScaleRoutine(LoopingSFXContainer container, float targetScale, float duration)
    {
        float startScale = container.volumeScale;
        float timer = 0f;
        while (timer < duration)
        {
            if (container == null) yield break;
            timer += Time.deltaTime;
            container.volumeScale = Mathf.Lerp(startScale, targetScale, timer / duration);
            yield return null;
        }
        if (container != null) container.volumeScale = targetScale;
    }

    /// <summary>
    /// 단발 SFX를 페이드 인→서스테인→페이드 아웃으로 재생함.
    /// 완료 시 임시 AudioSource를 파괴함.
    /// </summary>
    private IEnumerator PlayOneShotWithFadeRoutine(AudioClip clip, float duration)
    {
        AudioSource tempSource = gameObject.AddComponent<AudioSource>();
        tempSource.clip = clip;
        tempSource.loop = false;
        tempSource.spatialBlend = 0f;
        tempSource.Play();
        float timer = 0f;
        float fadeDuration = Mathf.Min(duration, clip.length / 2);
        while (timer < fadeDuration) { timer += Time.deltaTime; tempSource.volume = Mathf.Lerp(0f, 1f, timer / fadeDuration); yield return null; }
        float sustainTime = clip.length - (fadeDuration * 2);
        if (sustainTime > 0) yield return new WaitForSeconds(sustainTime);
        timer = 0f;
        while (timer < fadeDuration) { timer += Time.deltaTime; tempSource.volume = Mathf.Lerp(1f, 0f, timer / fadeDuration); yield return null; }
        tempSource.Stop(); Destroy(tempSource);
    }
    #endregion

    #region Helpers

    /// <summary>NAR·SFX·AMB 전체를 정지함</summary>
    public void StopAllAudio()
    {
        StopNAR();
        StopAllSFX();
        StopAMB(0.5f);
    }

    /// <summary>씬 전환 전 모든 오디오 상태를 즉시 초기화함</summary>
    public void ResetForSceneTransition()
    {
        // 진행 중인 모든 코루틴 정지
        foreach (var kvp in _activeShuffleRoutines)
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        _activeShuffleRoutines.Clear();

        foreach (var kvp in _activeSmoothVolumeRoutines)
            if (kvp.Value != null) StopCoroutine(kvp.Value);
        _activeSmoothVolumeRoutines.Clear();

        if (_ambCrossFadeCoroutine != null) { StopCoroutine(_ambCrossFadeCoroutine); _ambCrossFadeCoroutine = null; }

        // 루핑 SFX AudioSource 컴포넌트 모두 제거
        foreach (var type in new List<SFXType>(_activeLoopingSFX.Keys))
            RemoveLoopingSource(type);
        _activeLoopingSFX.Clear();

        // NAR / 기본 SFX 정지
        if (narSource != null) narSource.Stop();
        if (sfxSource != null) sfxSource.Stop();

        // AMB 즉시 정지 및 페이드 팩터 초기화
        if (ambSourceA != null) { ambSourceA.Stop(); ambSourceA.clip = null; }
        if (ambSourceB != null) { ambSourceB.Stop(); ambSourceB.clip = null; }
        _fadeFactorA = 0f;
        _fadeFactorB = 0f;
        _isUsingSourceA = false;

        if (isDebug) Debug.Log("[AudioManager] Scene transition reset complete.");
    }

    /// <summary>풀에서 AudioSource를 꺼냄. 풀이 비어 있으면 동적 할당함</summary>
    private AudioSource GetPooledSource()
    {
        if (_audioSourcePool.Count > 0)
            return _audioSourcePool.Dequeue();

        // NOTE: 풀 소진 시 동적 할당 — 동시 재생 수가 풀 크기를 초과한 경우
        AudioSource newSrc = gameObject.AddComponent<AudioSource>();
        newSrc.playOnAwake = false;
        newSrc.spatialBlend = 0f;
        return newSrc;
    }

    /// <summary>AudioSource를 초기화하여 풀에 반환함</summary>
    private void ReturnToPool(AudioSource src)
    {
        if (src == null) return;
        src.Stop();
        src.clip = null;
        src.loop = false;
        src.volume = 1f;
        _audioSourcePool.Enqueue(src);
    }

    private AudioClip GetSafeClip(AudioClip[] arr, int idx)
    {
        if (arr != null && idx >= 0 && idx < arr.Length) return arr[idx];
        return null;
    }
    #endregion
}
