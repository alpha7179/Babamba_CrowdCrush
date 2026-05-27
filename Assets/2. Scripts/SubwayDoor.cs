using UnityEngine;

public class SubwayDoor : MonoBehaviour
{
    private Animator animator;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    // 서류가방 abc 미션 완료 시 호출
    public void OpenDoor()
    {
        animator.SetTrigger("Open");
    }

    // 플레이어가 열차 밖으로 나가면 호출
    public void CloseDoor()
    {
        animator.SetTrigger("Close");
    }



    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            CloseDoor();
        }
    }


}
