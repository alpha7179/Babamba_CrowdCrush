using UnityEngine;
using System.Collections;

public class WanderAgent : MonoBehaviour
{
    [Header("이동")]
    public float moveSpeed = 1.2f;
    public float rotateSpeed = 4f;
    public float arrivalDistance = 0.6f;

    [Header("대기")]
    public float minWait = 2f;
    public float maxWait = 5f;

    [Header("회피")]
    public float raycastDistance = 1.2f;
    public float separationRadius = 0.8f;
    [Tooltip("벽/기둥/의자 레이어 선택 (NPC 제외)")]
    public LayerMask obstacleLayer;

    [Header("지면")]
    public LayerMask groundLayer;
    private float snapTimer;

    [Header("컬링")]
    public float activeDistance = 7f;

    [Header("구역")]
    [Tooltip("비워두면 WanderManager 전체 waypoint 사용, 채우면 이것만 사용")]
    public Transform[] localWaypoints;

    private Transform targetWaypoint;
    private float waitTimer;
    private bool isWaiting;
    private bool isMoving;
    private bool isActive = true;
    private Animator animator;
    private Transform cam;
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    void Awake()
    {
        animator = GetComponent<Animator>();
    }

    void Start()
    {
        cam = Camera.main != null ? Camera.main.transform : null;
        StartCoroutine(WanderCullLoop());
    }

    void OnEnable() => WanderManager.Register(this);
    void OnDisable() => WanderManager.Unregister(this);

    public void SetWaypoint(Transform wp)
    {
        targetWaypoint = wp;
        isWaiting = false;
    }

    void Update()
    {
        if (animator != null && animator.enabled && animator.isInitialized)
            animator.SetFloat(SpeedHash, isMoving ? moveSpeed : 0f, 0.2f, Time.deltaTime);
    }

    // 0.15초마다 카메라 거리 체크 → isActive 갱신
    // enabled는 건드리지 않아 NPCAnimatorCuller와 충돌 없음
    IEnumerator WanderCullLoop()
    {
        var wait = new WaitForSeconds(0.15f);
        float sqrThreshold = activeDistance * activeDistance;

        while (true)
        {
            if (cam == null && Camera.main != null)
                cam = Camera.main.transform;

            if (cam != null)
            {
                float sqrDist = (transform.position - cam.position).sqrMagnitude;
                isActive = sqrDist < sqrThreshold;
            }

            yield return wait;
        }
    }

    // WanderManager에서만 호출됨 (매 프레임 아님)
    public void ManagedUpdate(float dt)
    {
        // 카메라 범위 밖이면 이동 중단
        if (!isActive)
        {
            isMoving = false;
            return;
        }

        // 지면 스냅 (0.5초마다 Raycast로 Y 보정)
        snapTimer -= dt;
        if (snapTimer <= 0f)
        {
            snapTimer = 0.5f;
            if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out RaycastHit hit, 3f, groundLayer))
                transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
        }

        if (isWaiting)
        {
            waitTimer -= dt;
            if (waitTimer <= 0f)
            {
                targetWaypoint = PickLocalWaypoint(targetWaypoint);
                isWaiting = false;
            }
            isMoving = false;
            return;
        }

        if (targetWaypoint == null)
        {
            targetWaypoint = PickLocalWaypoint(null);
            isMoving = false;
            return;
        }

        Vector3 toTarget = targetWaypoint.position - transform.position;
        toTarget.y = 0f;

        // 도착
        if (toTarget.magnitude <= arrivalDistance)
        {
            isWaiting = true;
            isMoving = false;
            waitTimer = Random.Range(minWait, maxWait);
            return;
        }

        Vector3 moveDir = CalcMoveDir(toTarget.normalized);
        isMoving = moveDir != Vector3.zero;

        if (isMoving)
        {
            transform.position += moveDir * moveSpeed * dt;
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(moveDir),
                rotateSpeed * dt
            );
        }
    }

    Transform PickLocalWaypoint(Transform exclude)
    {
        if (localWaypoints == null || localWaypoints.Length == 0)
            return WanderManager.PickRandomWaypoint(exclude);

        if (localWaypoints.Length == 1) return localWaypoints[0];

        Transform picked;
        int tries = 0;
        do
        {
            picked = localWaypoints[Random.Range(0, localWaypoints.Length)];
            tries++;
        }
        while (picked == exclude && tries < 10);
        return picked;
    }

    Vector3 CalcMoveDir(Vector3 desired)
    {
        Vector3 origin = transform.position + Vector3.up * 0.8f;

        // 전방 레이캐스트 (Trigger 무시 → 정지 NPC 비트리거 콜라이더 감지됨)
        if (!Physics.Raycast(origin, desired, raycastDistance, obstacleLayer, QueryTriggerInteraction.Ignore))
            return ApplySeparation(desired);

        // 막혔을 때 대안 방향 시도 (45도씩)
        float[] angles = { 45f, -45f, 90f, -90f, 135f, -135f };
        foreach (float angle in angles)
        {
            Vector3 alt = Quaternion.Euler(0, angle, 0) * desired;
            if (!Physics.Raycast(origin, alt, raycastDistance, obstacleLayer, QueryTriggerInteraction.Ignore))
                return ApplySeparation(alt);
        }

        // 완전히 막히면 웨이포인트 교체
        targetWaypoint = PickLocalWaypoint(targetWaypoint);
        return Vector3.zero;
    }

    Vector3 ApplySeparation(Vector3 dir)
    {
        var agents = WanderManager.Agents;
        Vector3 separation = Vector3.zero;
        float sqrRadius = separationRadius * separationRadius;

        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i] == this) continue;
            Vector3 diff = transform.position - agents[i].transform.position;
            float sqrDist = diff.sqrMagnitude;
            if (sqrDist < sqrRadius && sqrDist > 0f)
                separation += diff.normalized / Mathf.Sqrt(sqrDist);
        }

        if (separation == Vector3.zero) return dir;

        Vector3 combined = (dir + separation * 0.3f).normalized;

        // combined 방향이 막히면 separation 무시하고 원래 방향 사용
        Vector3 origin = transform.position + Vector3.up * 0.8f;
        if (Physics.Raycast(origin, combined, raycastDistance, obstacleLayer, QueryTriggerInteraction.Ignore))
            return dir;

        return combined;
    }
}