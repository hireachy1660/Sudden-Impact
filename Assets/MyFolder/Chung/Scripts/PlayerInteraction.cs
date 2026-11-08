using Photon.Pun;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 줍기 노드·문 상호작용 부품. 소유자 전용(IOwnerOnlyPart) — 원격 복제본에서는 Core가 Awake에서 지운다.
/// 그래서 RPC 수신자와 애니 이벤트는 두지 않는다. 플레이어 상태는 Core(PlayerController)에서 읽는다.
/// 무기(PlayerWeapon)와의 연결은 없다 — 던지기와 Q 잠금(줍기 쿨다운)은 Core가 TryPickUp의 결과를 보고 처리한다.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerInteraction : MonoBehaviourPun, IOwnerOnlyPart
{
    [Header("Interaction Settings")]
    [SerializeField] private float interactRadius = 2.0f;
    [SerializeField] private LayerMask interactableLayer;

    [Header("ForDebug")]
    [SerializeField] private WeaponPickupNode closestNode; // 최단거리 노드

    private PlayerController core;
    private Coroutine curCheckClosestNodeCoroutine;
    private List<WeaponPickupNode> nearbyNodes = new List<WeaponPickupNode>();

    public void Bind(PlayerController _core) => core = _core;

    public void OnRespawn() { }

    public void OnRoundEndReset()
    {
        // 라운드 종료 시 근접 노드 탐색 코루틴을 멈춘다(전에는 Core의 StopAllCoroutines가 맡던 것)
        StopAllCoroutines();
    }

    // Q 입력의 결과. None = 노드 없음(Core가 던지기로 넘긴다) · Blocked = 가용 아닌 노드가 옆에 있어 드롭 차단 · Requested = 줍기 요청을 보냄(Core가 Q를 잠근다)
    public enum PickUpResult { None, Blocked, Requested }

    // 노드가 있으면 줍기를 시도하고 결과만 돌려준다. 공통 가드(CanAct·performed)와 Q 잠금은 Core의 중계가 한다.
    public PickUpResult TryPickUp()
    {
        // 주변에 노드가 있으면 (가용 여부 무관) 드롭 차단
        if (closestNode == null) return PickUpResult.None;

        bool requested = closestNode.IsAvailable;
        if (requested)
        {
            closestNode.RequestPickup(photonView.ViewID);
        }
        closestNode = null;
        return requested ? PickUpResult.Requested : PickUpResult.Blocked;
    }

    // 원격에서 지워지기 전(첫 프레임)에도 트리거 콜백은 들어올 수 있어 IsMine 검사를 그대로 둔다
    private void OnTriggerEnter(Collider other)
    {
        if (!photonView.IsMine) return;

        WeaponPickupNode node;
        if (other.TryGetComponent<WeaponPickupNode>(out node))
        {
            if (!node.IsAvailable) return;
            nearbyNodes.Add(node);

            if (closestNode == null)
            {
                closestNode = node;
                curCheckClosestNodeCoroutine = StartCoroutine(CheckClosestNode());
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!photonView.IsMine) return;

        WeaponPickupNode node;
        if (other.TryGetComponent<WeaponPickupNode>(out node))
        {
            if (!nearbyNodes.Remove(node)) return;

            if (node == closestNode) closestNode.HideLabel();
        }
    }

    private IEnumerator CheckClosestNode()
    {
        WeaponPickupNode prevClosest = null;

        while (nearbyNodes.Count > 0)
        {
            yield return new WaitForSeconds(5f / 60f); // ~5 frames

            float minSqrDist = float.MaxValue;
            WeaponPickupNode tempClosest = null;

            for (int i = nearbyNodes.Count - 1; i >= 0; i--)
            {
                if (nearbyNodes[i] == null || !nearbyNodes[i].IsAvailable)
                {
                    nearbyNodes.RemoveAt(i);
                    continue;
                }

                float sqrDist = (transform.position - nearbyNodes[i].transform.position).sqrMagnitude;
                if (sqrDist < minSqrDist)
                {
                    minSqrDist = sqrDist;
                    tempClosest = nearbyNodes[i];
                }
            }

            // Toggle label: 이전 노드의 레이블을 끄고 새 노드의 레이블을 켠다
            if (tempClosest != prevClosest)
            {
                if (prevClosest != null)
                {
                    GameEvents.PickupUIUpdate(false, Vector3.zero);
                }
                if (tempClosest != null)
                {
                    GameEvents.PickupUIUpdate(true, tempClosest.transform.position, $"{tempClosest.WeaponType} [Q]");
                }
                prevClosest = tempClosest;
            }

            closestNode = tempClosest;
        }

        // 주위에 더이상 노드가 없을 떄
        if (closestNode != null)
        {
            GameEvents.PickupUIUpdate(false, Vector3.zero);
        }
        closestNode = null;
        curCheckClosestNodeCoroutine = null;
    }

    public void TryInteract(InputAction.CallbackContext ctx)
    {
        if (!core.CanAct) return;

        // 1. 내 주변 2m 안의 가구/오브젝트 검색
        Collider[] hits = Physics.OverlapSphere(transform.position, interactRadius, interactableLayer);

        foreach (var hit in hits)
        {
            // 2. 인터페이스 추출 시도
            if (hit.TryGetComponent<IInteractable>(out var target))
            {
                // 3. 된다면 실행
                target.Interact(core);
                // 상호작용 애니는 모든 클라이언트에서 보여야 하므로 Core의 RPC 안에서 발화한다
                photonView.RPC(nameof(PlayerController.InteractAnimRPC), RpcTarget.All);

                break; // 한 번에 하나만 상호작용
            }
        }
    }
}
