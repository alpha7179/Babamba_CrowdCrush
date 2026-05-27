using UnityEngine;

public class NPCPush : MonoBehaviour
{
    public Vector3 originalPosition { get; private set; }
    public Vector3 targetPosition { get; set; }

    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float pushDistance = 0.4f;
    [SerializeField] private float returnDelay = 0.8f;
    [SerializeField] private float detectionRadius = 1.2f;

    private float returnTimer = 0f;
    private Transform playerTransform;
    private Vector3 prevPlayerPos;
    private Vector3 lastPushDir;

    void Awake()
    {
        originalPosition = transform.position;
        targetPosition = transform.position;
    }

    void Update()
    {
        transform.position = Vector3.Lerp(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        if (playerTransform != null)
        {
            Vector3 toPlayer = playerTransform.position - originalPosition;
            toPlayer.y = 0f;

            if (toPlayer.sqrMagnitude < detectionRadius * detectionRadius)
            {
                Vector3 playerVelocity = playerTransform.position - prevPlayerPos;
                playerVelocity.y = 0f;

                if (playerVelocity.sqrMagnitude > 0.0001f)
                {
                    lastPushDir = playerVelocity.normalized;
                    targetPosition = originalPosition + lastPushDir * pushDistance;
                    returnTimer = returnDelay;
                }
            }
            else
            {
                playerTransform = null;
            }
        }

        prevPlayerPos = playerTransform != null ? playerTransform.position : prevPlayerPos;

        if (playerTransform == null && returnTimer > 0f)
        {
            returnTimer -= Time.deltaTime;
            if (returnTimer <= 0f)
                targetPosition = originalPosition;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<CharacterController>() == null) return;
        playerTransform = other.transform;
        prevPlayerPos = other.transform.position;

        // 애니메이션 반응
        var watchOut = GetComponent<WatchOutCrowdAnim>();
        if (watchOut != null) watchOut.TriggerPush();

        var dangerous = GetComponent<DangerousCrowdAnim>();
        if (dangerous != null) dangerous.TriggerPush();
    }
}
