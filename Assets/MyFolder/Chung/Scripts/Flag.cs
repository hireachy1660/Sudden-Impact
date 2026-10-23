using UnityEngine;

public class Flag : MonoBehaviour
{
    public int myTeam; // 1: A팀, 2: B팀
    public int flagIndex; // 맵에 여러 개일 경우를 대비한 고유 번호

    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private Collider triggerCollider;

    public void RespawnFlag()
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        meshRenderer.enabled = true;
        triggerCollider.enabled = true;
    }

    // 매니저의 지시를 받아 스스로를 숨기는 함수
    public void HideFlag()
    {
        meshRenderer.enabled = false;
        triggerCollider.enabled = false;
    }

    // 플레이어가 죽었을 때, 매니저의 지시를 받아 바닥에 다시 떨어지는 함수
    public void DropFlag(Vector3 dropPosition)
    {
        transform.position = dropPosition; // 죽은 사람 위치로 이동
        meshRenderer.enabled = true;
        triggerCollider.enabled = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        //주의: 오직 LocalPlayer 레이어하고만 충돌하게 세팅되어 있어야 함!

        // GetComponent 없이 즉시 매니저에게 "나(로컬) 터치함" 보고
        RoundManager.Instance.OnLocalPlayerTouchedFlag(myTeam);
    }
}