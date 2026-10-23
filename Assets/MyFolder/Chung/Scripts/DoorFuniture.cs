using UnityEngine;
using Photon.Pun;
using System.Collections;

public class Door : Furniture, IInteractable
{
    [Header("Door Controls")]
    [SerializeField] private Transform doorPivot;
    [SerializeField] private Transform spawnPivot;
    [SerializeField] private float openAngle = 90f;
    [SerializeField] private float openSpeed = 5f; // 문 열리는 속도

    [Header("Crumble Effect")]
    [SerializeField] private Transform originDoor;
    [SerializeField] private GameObject fracturedPrefab;

    // Furniture는 MonoBehaviour로 바뀌었으므로 PhotonView를 직접 캐싱
    private PhotonView _pv;

    public AudioClip doorSound;
    private bool isOpen = false;
    private Coroutine doorCoroutine; // 중복 실행 방지용

    // [CH 수정] Furniture가 MonoBehaviourPun → MonoBehaviour로 바뀌면서
    // photonView 프로퍼티가 사라짐 → GetComponent로 직접 캐싱
    // 기존: photonView.RPC(...) → _pv.RPC(...)

    
    protected override void Awake()
    {
        base.Awake();
        _pv = GetComponent<PhotonView>();
    }

    public void Interact(PlayerController player)
    {
        _pv.RPC(nameof(RPC_ToggleDoor), RpcTarget.All, player.transform.position);
    }

    [PunRPC]
    private void RPC_ToggleDoor(Vector3 _playerPos)
    {
        if (isDestroyed) return;

        isOpen = !isOpen;
        // 기존에 움직이고 있었다면 멈추고 새로 시작
        if (doorCoroutine != null) return;
       
        float targetAngle = 0f;
        if (isOpen)
        {
            Vector3 dirToPlayer = (_playerPos - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, dirToPlayer);
            targetAngle = dot > 0 ? openAngle : -openAngle;
        }

        doorCoroutine = StartCoroutine(AnimateDoor(targetAngle));
    }

    private IEnumerator AnimateDoor(float targetAngle)
    {
        Quaternion targetRot = Quaternion.Euler(0, targetAngle, 0);
        if(audioSource != null)
        {
            audioSource.PlayOneShot(doorSound);
        }
        while (Quaternion.Angle(doorPivot.localRotation, targetRot) > 0.1f)
        {
            doorPivot.localRotation = Quaternion.Slerp(
                doorPivot.localRotation,
                targetRot,
                Time.deltaTime * openSpeed
            );
            yield return null;
        }

        doorPivot.localRotation = targetRot;
        doorCoroutine = null;
    }

    protected override void OnBroken()
    {
        if (fracturedPrefab != null)
        {

            GameObject fractured = Instantiate(fracturedPrefab, originDoor.position, originDoor.rotation, transform.parent);
            fractured.transform.localScale = transform.localScale;

            // 무너뜨리기 스크립트 실행 (안전장치 포함)
            Crumble crumble = fractured.GetComponent<Crumble>();
            if (crumble != null)
            {
                crumble.SetCrumble();
            }
            else
            {
                Debug.LogError($"[장애물 파괴] {fracturedPrefab.name}에 CrumbleTutorialScript가 없습니다!");
            }
        }
        base.OnBroken();
        // 파괴 파티클 (BoxCollider 기준 크기 + 머티리얼 색상)
        //    if (FurnitureBreakEffectManager.Instance != null)
        //    {
        //        var col = GetComponentInChildren<BoxCollider>();
        //        var rend = GetComponentInChildren<Renderer>();
        //        if (col != null && rend != null)
        //        {
        //            FurnitureBreakEffectManager.Instance.Spawn(
        //                col.bounds.size,
        //                rend.material.color,
        //                col.bounds.center,
        //                lastHitNormal
        //            );
        //        }
        //    }
        
    }
}