using UnityEngine;
using TMPro;

/// <summary>
/// 지하철 씬 119 신고 다이얼패드 입력 컴포넌트
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>숫자 키패드 입력값을 누적 표시</item>
///   <item>백스페이스로 마지막 글자 삭제</item>
///   <item>통화 버튼 클릭 시 입력값과 정답 번호 비교 후 SubwayStepManager로 결과 전달</item>
///   <item>119 신고 퀴즈(quizIndex=1) 다이얼 패널에 부착</item>
/// </list>
/// </remarks>
public class SubwayDialPad : MonoBehaviour
{
    #region Inspector Settings (References)

    [Header("Step Manager Reference")]
    [Tooltip("정답 결과를 전달할 SubwayStepManager.")]
    [SerializeField] private SubwayStepManager stepManager;

    [Header("Display")]
    [Tooltip("입력 번호를 표시할 TMP 텍스트.")]
    [SerializeField] private TextMeshProUGUI displayText;

    #endregion

    #region Inspector Settings (Quiz Logic)

    [Header("Quiz Settings")]
    [Tooltip("정답 번호 목록 — 어느 하나라도 일치하면 정답으로 인정.")]
    [SerializeField] private string[] correctNumbers = new string[] { "112", "119" };

    [Tooltip("최대 입력 자릿수.")]
    [SerializeField] private int maxLength = 11;

    [Tooltip("오답 통화 후 입력값 자동 초기화 여부.")]
    [SerializeField] private bool clearOnWrongCall = true;

    #endregion

    #region Internal State

    private string _input = string.Empty;

    #endregion

    #region Unity Lifecycle

    private void OnEnable()
    {
        // 패널 활성화 시 입력값 초기화 — 이전 세션 잔존값 방지
        Debug.Log($"[DialPad] OnEnable 호출 — GameObject: {gameObject.name} / Active: {gameObject.activeInHierarchy} / displayText null? {displayText == null}");
        ClearInput();
    }

    #endregion

    #region Public API

    /// <summary>다이얼 키 입력 — 입력값 끝에 문자 추가 후 표시 갱신</summary>
    /// <param name="digit">키 라벨 문자열 (0-9, *, #)</param>
    public void OnDigitPressed(string digit)
    {
        Debug.Log($"[DialPad] OnDigitPressed('{digit}') 호출 / 현재 입력: '{_input}' / displayText null? {displayText == null}");
        if (string.IsNullOrEmpty(digit)) { Debug.LogWarning("[DialPad] digit이 빈 문자열 — 반환"); return; }
        if (_input.Length >= maxLength) { Debug.LogWarning($"[DialPad] maxLength({maxLength}) 도달 — 반환"); return; }

        _input += digit;
        UpdateDisplay();
        Debug.Log($"[DialPad] 갱신 후 입력: '{_input}' / displayText.text: '{(displayText != null ? displayText.text : "<null>")}'");
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.UI_Click);
    }

    /// <summary>백스페이스 — 마지막 글자 삭제 후 표시 갱신</summary>
    public void OnBackspacePressed()
    {
        if (_input.Length == 0) return;

        _input = _input.Substring(0, _input.Length - 1);
        UpdateDisplay();
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.UI_Click);
    }

    /// <summary>통화 버튼 — 입력값과 정답 목록 비교 후 SubwayStepManager에 결과 전달</summary>
    public void OnCallPressed()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.PlaySFX(SFXType.UI_Click);

        bool isMatch = IsCorrectNumber(_input);
        if (stepManager != null) stepManager.SetQuizAnswer(isMatch);

        // 오답 시 입력값 초기화 — 재입력 허용
        if (!isMatch && clearOnWrongCall) ClearInput();
    }

    /// <summary>입력값 강제 초기화 — 외부 호출 시점에서 사용</summary>
    public void ClearInput()
    {
        _input = string.Empty;
        UpdateDisplay();
    }

    #endregion

    #region Internal Logic

    private void UpdateDisplay()
    {
        if (displayText != null) displayText.text = _input;
    }

    // 입력값이 정답 목록 중 하나와 일치하는지 확인 — 빈 입력·빈 슬롯 방어 처리 포함
    private bool IsCorrectNumber(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        if (correctNumbers == null) return false;

        foreach (var num in correctNumbers)
        {
            if (!string.IsNullOrEmpty(num) && string.Equals(input, num)) return true;
        }
        return false;
    }

    #endregion
}
