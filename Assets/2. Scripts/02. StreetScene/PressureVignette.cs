using UnityEngine;

/// <summary>셰이더 기반 압박 비네팅 및 심장박동 펄스 효과를 제어하는 컴포넌트</summary>
public class PressureVignette : MonoBehaviour
{
    #region Inspector Settings

    [Header("Visual Settings")]
    [Tooltip("비네팅 효과의 색상입니다. (주로 붉은색이나 검은색 사용)")]
    [SerializeField] private Color vignetteColor = new Color(1f, 0f, 0f, 0.5f);

    [Tooltip("비네팅 경계의 부드러운 정도입니다. (0에 가까울수록 날카로움)")]
    [SerializeField] private float feathering = 0.5f;

    [Header("Pulse Settings")]
    [Tooltip("체크 시: 심장 박동처럼 화면이 주기적으로 울렁거립니다.")]
    [SerializeField] private bool usePulse = true;

    [Tooltip("기본 박동 속도입니다.")]
    [SerializeField] private float basePulseSpeed = 2.0f;

    [Tooltip("박동 시 조리개(Aperture) 크기의 변화 폭입니다.")]
    [SerializeField] private float pulseMagnitude = 0.05f;

    [Header("Debug (Play Mode Only)")]
    [Tooltip("테스트용 강도 슬라이더입니다. 플레이 모드에서 실시간으로 조절해 볼 수 있습니다.")]
    [Range(0f, 1f)]
    [SerializeField] private float testIntensity = 0f;

    #endregion

    #region Internal State

    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propBlock;

    /// <summary>현재 적용된 압박감 강도 (0.0 ~ 1.0)</summary>
    private float currentIntensity = 0f;

    // Shader Property ID — 성능을 위해 미리 해싱
    private static readonly int ApertureSizeID = Shader.PropertyToID("_ApertureSize");
    private static readonly int VignetteColorID = Shader.PropertyToID("_VignetteColor");
    private static readonly int FeatheringEffectID = Shader.PropertyToID("_FeatheringEffect");

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        propBlock = new MaterialPropertyBlock();

        // 초기화: 조리개 완전 개방으로 효과 비활성
        UpdateVignette(1.0f);
    }

    private void Update()
    {
        // 디버그용: Inspector 슬라이더 값 변경 시 실시간 반영
        if (testIntensity > 0)
        {
            currentIntensity = testIntensity;
        }

        // Pulse 애니메이션을 위해 매 프레임 갱신
        UpdateVisuals();
    }

    #endregion

    #region Public API

    /// <summary>
    /// 비네팅 효과 강도를 설정함.
    /// </summary>
    /// <param name="intensity">0.0(없음) ~ 1.0(최대) 사이의 값</param>
    public void SetIntensity(float intensity)
    {
        currentIntensity = Mathf.Clamp01(intensity);
        testIntensity = currentIntensity;

        // PERF: 강도 미미 시 컴포넌트 비활성화로 연산 절약
        bool shouldEnable = currentIntensity > 0.01f;

        if (enabled != shouldEnable)
        {
            enabled = shouldEnable;
            if (!enabled) UpdateVignette(1.0f);
        }
    }

    #endregion

    #region Internal Logic

    /// <summary>현재 강도와 시간 기반으로 최종 조리개 크기를 산출함</summary>
    private void UpdateVisuals()
    {
        // 강도가 높을수록 0.3까지 축소
        float minAperture = 0.3f;
        float targetAperture = Mathf.Lerp(1.0f, minAperture, currentIntensity);

        if (usePulse && currentIntensity > 0.1f)
        {
            // 강도 비례 박동 속도 증가
            float dynamicSpeed = basePulseSpeed + (currentIntensity * 5.0f);

            // Sin 파동으로 조리개 크기 변동 — 강도에 비례하여 진폭 증가
            float pulseOffset = Mathf.Sin(Time.time * dynamicSpeed) * pulseMagnitude * currentIntensity;

            targetAperture += pulseOffset;
        }

        UpdateVignette(Mathf.Clamp01(targetAperture));
    }

    /// <summary>MaterialPropertyBlock을 통해 셰이더에 값을 전달함</summary>
    private void UpdateVignette(float apertureSize)
    {
        // PERF: MaterialPropertyBlock 사용으로 Material 인스턴스 생성 방지, 배칭 유지
        if (meshRenderer == null) return;

        meshRenderer.GetPropertyBlock(propBlock);

        propBlock.SetFloat(ApertureSizeID, apertureSize);
        propBlock.SetColor(VignetteColorID, vignetteColor);
        propBlock.SetFloat(FeatheringEffectID, feathering);

        meshRenderer.SetPropertyBlock(propBlock);
    }

    #endregion
}
