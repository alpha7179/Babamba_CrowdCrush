using UnityEngine;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// XR 컨트롤러 입력을 중앙에서 감지하고 상태·이벤트를 발행하는 매니저
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>InputActionAsset 기반 버튼(A/B/X/Y), 그립, 트리거, 조이스틱 입력 감지</item>
///   <item>입력 상태를 bool 프로퍼티로 제공 (Polling)</item>
///   <item>버튼 클릭 시 Action 이벤트 발행 (Event)</item>
/// </list>
/// </remarks>
public class ControllerInputManager : MonoBehaviour
{
    #region Singleton

    public static ControllerInputManager Instance { get; private set; }

    private void Awake()
    {
        // 중복 생성 방지 및 씬 전환 시 파괴 방지
        if (Instance == null)
        {
            Instance = this;
            transform.parent = null; // DontDestroyOnLoad 대상은 최상위 계층이어야 함
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    #endregion

    #region Inspector Settings

    [Header("Input Settings")]
    [Tooltip("XR Interaction Toolkit의 기본 Input Action Asset을 할당하세요.")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("Button Enable Settings")]
    [Tooltip("A 버튼 입력 활성화 여부")]
    [SerializeField] private bool _isAButtonEnabled = true;
    [Tooltip("B 버튼 입력 활성화 여부")]
    [SerializeField] private bool _isBButtonEnabled = true;
    [Tooltip("X 버튼 입력 활성화 여부")]
    [SerializeField] private bool _isXButtonEnabled = false;
    [Tooltip("Y 버튼 입력 활성화 여부")]
    [SerializeField] private bool _isYButtonEnabled = true;

    [Header("Debug")]
    [SerializeField] private bool isDebug = true;

    #endregion

    #region Events

    /// <summary>오른손 A 버튼 클릭 시 발행</summary>
    public event Action OnAButtonDown;
    /// <summary>오른손 B 버튼 클릭 시 발행</summary>
    public event Action OnBButtonDown;
    /// <summary>왼손 Y 버튼 클릭 시 발행</summary>
    public event Action OnYButtonDown;

    /// <summary>오른손 그립 누름 시 발행</summary>
    public event Action OnRightGripDown;
    /// <summary>오른손 그립 뗄 때 발행</summary>
    public event Action OnRightGripUp;
    /// <summary>왼손 그립 누름 시 발행</summary>
    public event Action OnLeftGripDown;
    /// <summary>왼손 그립 뗄 때 발행</summary>
    public event Action OnLeftGripUp;

    /// <summary>오른손 트리거 누름 시 발행</summary>
    public event Action OnRightTriggerDown;
    /// <summary>오른손 트리거 뗄 때 발행</summary>
    public event Action OnRightTriggerUp;
    /// <summary>왼손 트리거 누름 시 발행</summary>
    public event Action OnLeftTriggerDown;
    /// <summary>왼손 트리거 뗄 때 발행</summary>
    public event Action OnLeftTriggerUp;

    #endregion

    #region Internal State

    private InputAction AButton, BButton, XButton, YButton;
    private InputAction RGripButton, LGripButton;
    private InputAction RTriggerButton, LTriggerButton;
    private InputAction RJoystick;

    // NOTE: 람다 콜백 필드 저장 — OnDestroy에서 -= 해제를 위해 필요
    private Action<InputAction.CallbackContext> _onRGripPerformed;
    private Action<InputAction.CallbackContext> _onRGripCanceled;
    private Action<InputAction.CallbackContext> _onLGripPerformed;
    private Action<InputAction.CallbackContext> _onLGripCanceled;
    private Action<InputAction.CallbackContext> _onRTriggerPerformed;
    private Action<InputAction.CallbackContext> _onRTriggerCanceled;
    private Action<InputAction.CallbackContext> _onLTriggerPerformed;
    private Action<InputAction.CallbackContext> _onLTriggerCanceled;
    private Action<InputAction.CallbackContext> _onRJoystickPerformed;
    private Action<InputAction.CallbackContext> _onRJoystickCanceled;

    #endregion

    #region Global State

    /// <summary>오른손 그립 버튼 홀드 상태</summary>
    public bool IsRightGripHeld { get; private set; }
    /// <summary>왼손 그립 버튼 홀드 상태</summary>
    public bool IsLeftGripHeld { get; private set; }
    /// <summary>오른손 트리거(검지) 버튼 홀드 상태</summary>
    public bool IsRightTriggerHeld { get; private set; }
    /// <summary>왼손 트리거(검지) 버튼 홀드 상태</summary>
    public bool IsLeftTriggerHeld { get; private set; }

    /// <summary>오른손 조이스틱 입력값</summary>
    public Vector2 RightJoystickValue { get; private set; }

    #endregion

    #region Unity Lifecycle

    private void Start()
    {
        SetupInputActions();
    }

    private void OnDestroy()
    {
        // 명시적 콜백 해제 — 씬 재진입 시 중복 등록 방지
        if (AButton != null) AButton.performed -= OnAButtonPressed;
        if (BButton != null) BButton.performed -= OnBButtonPressed;
        if (XButton != null) XButton.performed -= OnXButtonPressed;
        if (YButton != null) YButton.performed -= OnYButtonPressed;

        if (RGripButton != null)
        {
            if (_onRGripPerformed != null) RGripButton.performed -= _onRGripPerformed;
            if (_onRGripCanceled != null) RGripButton.canceled -= _onRGripCanceled;
        }
        if (LGripButton != null)
        {
            if (_onLGripPerformed != null) LGripButton.performed -= _onLGripPerformed;
            if (_onLGripCanceled != null) LGripButton.canceled -= _onLGripCanceled;
        }
        if (RTriggerButton != null)
        {
            if (_onRTriggerPerformed != null) RTriggerButton.performed -= _onRTriggerPerformed;
            if (_onRTriggerCanceled != null) RTriggerButton.canceled -= _onRTriggerCanceled;
        }
        if (LTriggerButton != null)
        {
            if (_onLTriggerPerformed != null) LTriggerButton.performed -= _onLTriggerPerformed;
            if (_onLTriggerCanceled != null) LTriggerButton.canceled -= _onLTriggerCanceled;
        }
        if (RJoystick != null)
        {
            if (_onRJoystickPerformed != null) RJoystick.performed -= _onRJoystickPerformed;
            if (_onRJoystickCanceled != null) RJoystick.canceled -= _onRJoystickCanceled;
        }

        if (inputActions != null) inputActions.Disable();
    }

    #endregion

    #region Initialization

    /// <summary>InputActionAsset에서 액션 맵·액션을 바인딩하고 활성화함</summary>
    private void SetupInputActions()
    {
        if (inputActions == null)
        {
            if (isDebug) Debug.LogError("[ControllerInputManager] InputActionAsset is missing!");
            return;
        }

        inputActions.Enable();

        var rightMap = inputActions.FindActionMap("XRI Right");
        if (rightMap != null)
        {
            AButton = rightMap.FindAction("AButton");
            if (AButton != null) { AButton.Enable(); AButton.performed += OnAButtonPressed; }

            BButton = rightMap.FindAction("BButton");
            if (BButton != null) { BButton.Enable(); BButton.performed += OnBButtonPressed; }
        }

        var leftMap = inputActions.FindActionMap("XRI Left");
        if (leftMap != null)
        {
            XButton = leftMap.FindAction("XButton");
            if (XButton != null) { XButton.Enable(); XButton.performed += OnXButtonPressed; }

            YButton = leftMap.FindAction("YButton");
            if (YButton != null) { YButton.Enable(); YButton.performed += OnYButtonPressed; }
        }

        var rInteractMap = inputActions.FindActionMap("XRI Right Interaction");
        if (rInteractMap != null)
        {
            RGripButton = rInteractMap.FindAction("Select");
            if (RGripButton != null)
            {
                RGripButton.Enable();
                _onRGripPerformed = ctx => { IsRightGripHeld = true; if (isDebug) Debug.Log("R Grip Held"); OnRightGripDown?.Invoke(); };
                _onRGripCanceled  = ctx => { IsRightGripHeld = false; if (isDebug) Debug.Log("R Grip Released"); OnRightGripUp?.Invoke(); };
                RGripButton.performed += _onRGripPerformed;
                RGripButton.canceled  += _onRGripCanceled;
            }

            RTriggerButton = rInteractMap.FindAction("Activate");
            if (RTriggerButton != null)
            {
                RTriggerButton.Enable();
                _onRTriggerPerformed = ctx => { IsRightTriggerHeld = true; if (isDebug) Debug.Log("R Trigger Held"); OnRightTriggerDown?.Invoke(); };
                _onRTriggerCanceled  = ctx => { IsRightTriggerHeld = false; if (isDebug) Debug.Log("R Trigger Released"); OnRightTriggerUp?.Invoke(); };
                RTriggerButton.performed += _onRTriggerPerformed;
                RTriggerButton.canceled  += _onRTriggerCanceled;
            }
        }

        var lInteractMap = inputActions.FindActionMap("XRI Left Interaction");
        if (lInteractMap != null)
        {
            LGripButton = lInteractMap.FindAction("Select");
            if (LGripButton != null)
            {
                LGripButton.Enable();
                _onLGripPerformed = ctx => { IsLeftGripHeld = true; if (isDebug) Debug.Log("L Grip Held"); OnLeftGripDown?.Invoke(); };
                _onLGripCanceled  = ctx => { IsLeftGripHeld = false; if (isDebug) Debug.Log("L Grip Released"); OnLeftGripUp?.Invoke(); };
                LGripButton.performed += _onLGripPerformed;
                LGripButton.canceled  += _onLGripCanceled;
            }

            LTriggerButton = lInteractMap.FindAction("Activate");
            if (LTriggerButton != null)
            {
                LTriggerButton.Enable();
                _onLTriggerPerformed = ctx => { IsLeftTriggerHeld = true; if (isDebug) Debug.Log("L Trigger Held"); OnLeftTriggerDown?.Invoke(); };
                _onLTriggerCanceled  = ctx => { IsLeftTriggerHeld = false; if (isDebug) Debug.Log("L Trigger Released"); OnLeftTriggerUp?.Invoke(); };
                LTriggerButton.performed += _onLTriggerPerformed;
                LTriggerButton.canceled  += _onLTriggerCanceled;
            }
        }

        var rLocoMap = inputActions.FindActionMap("XRI Right Locomotion");
        if (rLocoMap != null)
        {
            RJoystick = rLocoMap.FindAction("Turn");
            if (RJoystick != null)
            {
                RJoystick.Enable();
                _onRJoystickPerformed = ctx => RightJoystickValue = ctx.ReadValue<Vector2>();
                _onRJoystickCanceled  = ctx => RightJoystickValue = Vector2.zero;
                RJoystick.performed += _onRJoystickPerformed;
                RJoystick.canceled  += _onRJoystickCanceled;
            }
        }
    }

    #endregion

    #region Public API

    /// <summary>오른손 트리거 누름 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeRightTriggerDown() { IsRightTriggerHeld = true;  OnRightTriggerDown?.Invoke(); }
    /// <summary>오른손 트리거 해제 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeRightTriggerUp()   { IsRightTriggerHeld = false; OnRightTriggerUp?.Invoke(); }
    /// <summary>왼손 트리거 누름 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeLeftTriggerDown()  { IsLeftTriggerHeld = true;   OnLeftTriggerDown?.Invoke(); }
    /// <summary>왼손 트리거 해제 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeLeftTriggerUp()    { IsLeftTriggerHeld = false;  OnLeftTriggerUp?.Invoke(); }
    /// <summary>오른손 그립 누름 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeRightGripDown()    { IsRightGripHeld = true;     OnRightGripDown?.Invoke(); }
    /// <summary>오른손 그립 해제 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeRightGripUp()      { IsRightGripHeld = false;    OnRightGripUp?.Invoke(); }
    /// <summary>왼손 그립 누름 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeLeftGripDown()     { IsLeftGripHeld = true;      OnLeftGripDown?.Invoke(); }
    /// <summary>왼손 그립 해제 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeLeftGripUp()       { IsLeftGripHeld = false;     OnLeftGripUp?.Invoke(); }
    /// <summary>Y버튼 이벤트를 외부에서 발행함. XR 핸드 제스처 어댑터 전용.</summary>
    public void InvokeYButtonDown()      { OnYButtonDown?.Invoke(); }

    #endregion

    #region Internal Logic

    private void OnAButtonPressed(InputAction.CallbackContext ctx)
    {
        if (!_isAButtonEnabled) return;
        if (isDebug) Debug.Log("A Button Pressed");
        OnAButtonDown?.Invoke();
    }

    private void OnBButtonPressed(InputAction.CallbackContext ctx)
    {
        if (!_isBButtonEnabled) return;
        if (isDebug) Debug.Log("B Button Pressed");
        OnBButtonDown?.Invoke();
    }

    private void OnXButtonPressed(InputAction.CallbackContext ctx)
    {
        if (!_isXButtonEnabled) return;
        if (isDebug) Debug.Log("X Button Pressed");
        // TODO: X버튼 이벤트 필요 시 추가
    }

    private void OnYButtonPressed(InputAction.CallbackContext ctx)
    {
        if (!_isYButtonEnabled) return;
        if (isDebug) Debug.Log("Y Button Pressed");
        OnYButtonDown?.Invoke();
    }

    #endregion
}
