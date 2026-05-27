using UnityEngine;
using System.Collections;

public class NPCAnimatorCuller : MonoBehaviour
{
    [Header("Distance Settings")]

    public float renderDistance = 16f;  // 렌더러 표시 거리
    public float animDistance = 7f; // 애니메이터 활성 거리
    public float physicsColliderDistance = 3.5f;
    public float triggerColliderDistance = 5f;

    // 뒤쪽 NPC 더 빨리 컬링하기 위한 값
    [Header("Back Culling")]
    [Range(0.1f, 1f)]
    public float backCullMultiplier = 0.5f; // 뒤쪽은 거리 일단 0.5 초기화

    [Header("References")]
    public CapsuleCollider triggerCollider;
    public CapsuleCollider physicsCollider;
    public GameObject lodsRoot;                    // 교체: _UMS_LODs_ 오브젝트 (LOD Group 적용 NPC용)
    public SkinnedMeshRenderer hairRenderer;       // 교체: LOD Group 밖 Hair 직접 참조
    public SkinnedMeshRenderer eyelashRenderer;    // 교체: LOD Group 밖 Eyelashes 직접 참조

    private SkinnedMeshRenderer[] skinnedMeshes;   // 폴백: LOD Group 없는 기존 NPC용
    private Animator animator;
    private Transform cam;
    private WaitForSeconds wait;

    // 제곱 거리 캐싱 (매 프레임 계산 방지)
    private float renderDistSqr;
    private float animDistSqr;
    private float physicsDistSqr;
    private float triggerDistSqr;
    private float renderDistSqrBack;

    void Start()
    {
        animator = GetComponent<Animator>();
        cam = Camera.main != null ? Camera.main.transform : null;

        // 교체: lodsRoot 없으면 기존 방식으로 폴백 (LOD Group 없는 NPC)
        if (lodsRoot == null)
            skinnedMeshes = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        wait = new WaitForSeconds(0.12f);

        // 제곱 거리 미리 계산 (Start에서 한번만)
        animDistSqr = animDistance * animDistance;
        physicsDistSqr = physicsColliderDistance * physicsColliderDistance;
        triggerDistSqr = triggerColliderDistance * triggerColliderDistance;
        renderDistSqr = renderDistance * renderDistance;
        renderDistSqrBack = (renderDistance * backCullMultiplier)
                          * (renderDistance * backCullMultiplier);

        StartCoroutine(CullLoop(Random.Range(0f, 0.5f)));
    }

    IEnumerator CullLoop(float startDelay)
    {
        yield return new WaitForSeconds(startDelay);

        while (true)
        {
            if (cam == null)
            {
                if (Camera.main != null)
                    cam = Camera.main.transform;

                yield return wait;
                continue;
            }

            Vector3 toNPC = transform.position - cam.position;
            float sqrDist = toNPC.sqrMagnitude;

            // 뒤쪽 판별 (dot만 사용, normalized 불필요)
            float dot = Vector3.Dot(cam.forward, toNPC);
            bool isBack = dot < 0f;

            // 렌더러 - 뒤쪽이면 짧은 거리 적용
            bool renderVisible = sqrDist < (isBack ? renderDistSqrBack : renderDistSqr);

            // 교체: LOD Group 적용 NPC는 lodsRoot SetActive로 제어
            // 16f 밖이면 완전 꺼짐, 안이면 LOD Group이 거리에 따라 퀄리티 자동 조절
            if (lodsRoot != null)
            {
                lodsRoot.SetActive(renderVisible);
                if (hairRenderer != null) hairRenderer.enabled = renderVisible;
                if (eyelashRenderer != null) eyelashRenderer.enabled = renderVisible;
            }
            // 폴백: LOD Group 없는 기존 NPC는 renderer 직접 제어
            else if (skinnedMeshes != null)
            {
                for (int i = 0; i < skinnedMeshes.Length; i++)
                    if (skinnedMeshes[i] != null) skinnedMeshes[i].enabled = renderVisible;
            }

            // 애니메이션 - 거리만 체크
            if (animator != null)
                animator.enabled = sqrDist < animDistSqr;

            // 콜라이더
            if (physicsCollider != null)
                physicsCollider.enabled = sqrDist < physicsDistSqr;
            if (triggerCollider != null)
                triggerCollider.enabled = sqrDist < triggerDistSqr;

            yield return wait;
        }
    }
}