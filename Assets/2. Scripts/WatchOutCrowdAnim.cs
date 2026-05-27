using UnityEngine;
using System.Collections;

public class WatchOutCrowdAnim : MonoBehaviour
{
    public Animator animator;
    [Tooltip("Animator 안에 등록한 State 이름들")]
    public string[] clipNames =
        {"Breathing Idle_Anim",
        "Talking_Anim",
        "Talking (1)_Anim",
        "Talking On A Cell Phone_Anim",
        "Angry_Anim"
    };

    [SerializeField] private string pushClipName = "Stumble_Anim";
    [SerializeField] private float pushClipDuration = 1.2f;

    private int lastIndex = -1;
    private Coroutine changeAnimCoroutine;

    void Start()
    {
        if (!animator) animator = GetComponent<Animator>();

        animator.speed = Random.Range(0.7f, 1.2f);
        PlayRandomAnim();
        changeAnimCoroutine = StartCoroutine(ChangeAnimOccasionally());
    }

    void PlayRandomAnim()
    {
        int newIndex = GetNextClipIndex();
        lastIndex = newIndex;

        string clip = clipNames[newIndex];
        animator.CrossFadeInFixedTime(clip, 0.25f, 0, Random.value);
    }

    int GetNextClipIndex()
    {
        if (clipNames.Length <= 1) return 0;

        int index;
        do
        {
            index = Random.Range(0, clipNames.Length);
        }
        while (index == lastIndex);

        return index;
    }

    IEnumerator ChangeAnimOccasionally()
    {
        while (true)
        {
            float wait = Random.Range(3f, 7f);
            yield return new WaitForSeconds(wait);

            PlayRandomAnim();
        }
    }

    public void TriggerPush()
    {
        StartCoroutine(PlayPushAndResume());
    }

    IEnumerator PlayPushAndResume()
    {
        if (changeAnimCoroutine != null)
        {
            StopCoroutine(changeAnimCoroutine);
            changeAnimCoroutine = null;
        }

        animator.CrossFadeInFixedTime(pushClipName, 0.1f);
        yield return new WaitForSeconds(pushClipDuration);

        PlayRandomAnim();
        changeAnimCoroutine = StartCoroutine(ChangeAnimOccasionally());
    }
}
