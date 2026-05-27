using System.Collections.Generic;
using UnityEngine;
using Bhaptics.SDK2;

/// <summary>Avatar 충돌 시 방향별 bHaptics 진동을 트리거하는 컴포넌트</summary>
public class CollisionBodyHaptic : MonoBehaviour
{
    #region Enums / Constants

    /// <summary>8방향 충돌 판정 방향</summary>
    public enum Dir8
    {
        /// <summary>정면</summary>
        Front,
        /// <summary>좌측 전방</summary>
        FrontLeft,
        /// <summary>우측 전방</summary>
        FrontRight,
        /// <summary>좌측</summary>
        Left,
        /// <summary>우측</summary>
        Right,
        /// <summary>후방</summary>
        Back,
        /// <summary>좌측 후방</summary>
        BackLeft,
        /// <summary>우측 후방</summary>
        BackRight
    }

    #endregion

    #region Inspector Settings

    [Header("각 방향별 bHaptics Event ID (Designer에서 만든 이름)")]
    public string frontEventId = "front_5";
    public string frontLeftEventId = "f_left_5";
    public string frontRightEventId = "f_right_5";
    public string leftEventId = "left_5";
    public string rightEventId = "right_5";
    public string backEventId = "back_5";
    public string backLeftEventId = "b_left_5";
    public string backRightEventId = "b_right_5";

    [Header("Avatar 태그 이름")]
    public string avatarTag = "Avatar";

    [Header("패턴 길이(초) - 모든 이벤트가 0.3초라고 가정")]
    public float patternDuration = 0.3f;

    [Header("같은 방향 재생 최소 간격 (여유 시간, 초)")]
    public float extraCooldown = 0.02f;   // 패턴 종료 후 여유 시간

    #endregion

    #region Internal State

    /// <summary>방향별 마지막 재생 시각</summary>
    private readonly Dictionary<Dir8, float> _lastPlayTime =
        new Dictionary<Dir8, float>();

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        foreach (Dir8 d in System.Enum.GetValues(typeof(Dir8)))
        {
            _lastPlayTime[d] = -999f;
        }
    }

    // NOTE: Avatar가 트리거 영역 안에 머무는 동안 매 프레임 호출됨
    private void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag(avatarTag))
            return;

        Vector3 avatarPos = other.ClosestPoint(transform.position);
        Dir8 dir = GetDirection(avatarPos);

        string eventId = GetEventId(dir);
        if (string.IsNullOrEmpty(eventId))
            return;

        // 재생 완료 + 최소 간격 경과 시에만 재트리거
        bool isPlaying = BhapticsLibrary.IsPlayingByEventId(eventId);
        float elapsed = Time.time - _lastPlayTime[dir];

        if (!isPlaying && elapsed >= (patternDuration + extraCooldown))
        {
            Debug.Log($"[AVATAR HAPTIC] {dir} -> {eventId}");
            BhapticsLibrary.Play(eventId);
            _lastPlayTime[dir] = Time.time;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(avatarTag))
            return;

        // NOTE: 이미 재생 중인 패턴은 자연 종료되도록 방치
    }

    #endregion

    #region Internal Logic

    /// <summary>
    /// Avatar 월드 좌표 기준으로 8방향을 판정함
    /// </summary>
    /// <param name="avatarWorldPos">Avatar의 월드 좌표</param>
    private Dir8 GetDirection(Vector3 avatarWorldPos)
    {
        Vector3 toAvatar = avatarWorldPos - transform.position;
        toAvatar.y = 0f;

        if (toAvatar.sqrMagnitude < 0.0001f)
            return Dir8.Front;  // 거의 동일 위치 → 정면 처리

        toAvatar.Normalize();

        // z축: forward, x축: right 기준 각도 산출
        float angle = Mathf.Atan2(toAvatar.x, toAvatar.z) * Mathf.Rad2Deg;

        if (angle > -22.5f && angle <= 22.5f)
            return Dir8.Front;
        else if (angle > 22.5f && angle <= 67.5f)
            return Dir8.FrontRight;
        else if (angle > 67.5f && angle <= 112.5f)
            return Dir8.Right;
        else if (angle > 112.5f && angle <= 157.5f)
            return Dir8.BackRight;
        else if (angle <= -157.5f || angle > 157.5f)
            return Dir8.Back;
        else if (angle > -157.5f && angle <= -112.5f)
            return Dir8.BackLeft;
        else if (angle > -112.5f && angle <= -67.5f)
            return Dir8.Left;
        else
            return Dir8.FrontLeft;  // -67.5 ~ -22.5
    }

    #endregion

    #region Helpers

    private string GetEventId(Dir8 dir)
    {
        switch (dir)
        {
            case Dir8.Front: return frontEventId;
            case Dir8.FrontLeft: return frontLeftEventId;
            case Dir8.FrontRight: return frontRightEventId;
            case Dir8.Left: return leftEventId;
            case Dir8.Right: return rightEventId;
            case Dir8.Back: return backEventId;
            case Dir8.BackLeft: return backLeftEventId;
            case Dir8.BackRight: return backRightEventId;
            default: return null;
        }
    }

    #endregion
}
