using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviourPun
{
    public enum PlayerState
    {
        NotReady, Idle, Sprint, Rolling, Stunned, Dead
    }
    [Header("PlayerReference")]
    [SerializeField] private Rigidbody myRigidbody;
    [SerializeField] private Transform weaponAttachPoint;
    [SerializeField] private Weapon myKnife;
    [SerializeField] private PlayerRegistry registry;
    [SerializeField] private GameObject dummyFlagMesh;

    [Header("Parameters")]
    [SerializeField] private float deathAnimDuration = 2.0f;
    [SerializeField] private float meleeAttackCooldown = 0.5f;

    [SerializeField] private Renderer armBandRenderer;
    [SerializeField] private Color teamAColor = Color.blue;
    [SerializeField] private Color teamBColor = Color.red;

    [Header("Interaction Settings")]
    [SerializeField] private float interactRadius = 2.0f;
    [SerializeField] private LayerMask interactableLayer;

    [Header("Pre-Allocated Weapons")]
    [Tooltip("미리 모든 종류의 총을 등록")]
    [SerializeField] private Gun[] allGuns;

    [Header("Animator Bridge Data")]
    // 애니메이터가 읽어갈 퍼블릭 게터
    public bool UseGun => useGun;

    // 애니메이터에게 행동을 알리는 이벤트
    public event Action OnAttackEvent;
    public event Action OnRollEvent;
    public event Action OnThrowEvent;
    public event Action OnInteractEvent;

    [Header("ForDebug")]
    [SerializeField] private WeaponPickupNode closestNode; // 최단거리 노드
    [SerializeField] private Weapon myEquippedGun;
    [SerializeField] private bool useGun;
    [SerializeField] private bool hasEnemyFlag;
    [SerializeField] private int myTeam;
    [SerializeField] private PlayerState playerState;

    private Coroutine curCheckClosestNodeCoroutine;
    private List<WeaponPickupNode> nearbyNodes = new List<WeaponPickupNode>();
    private float _pickupCooldownEnd = 0f;
    private float _meleeCooldownEnd = 0f;
    Collider col;
    // 같은 오브젝트에 붙은 부품들. Core가 리스폰·라운드 종료를 받아 순회하며 알린다.
    private IPlayerPart[] parts;
    private PlayerMovement movement;
    private PlayerHealth health;

    public int MyTeam { get { return myTeam; } }
    public bool HasEnemyFlag { get { return hasEnemyFlag; } }
    public PlayerState GetPlayerState { get { return playerState; } }
    public Weapon MyEquippedGun { get { return myEquippedGun; } }
    public Transform WeaponAttachPoint => weaponAttachPoint;

    // 입력 진입점 공통 가드. 기절·사망·준비 전에는 어떤 조작도 받지 않는다.
    // 기절 중 입력 잠금을 InputManager의 액션맵 토글 대신 이 가드 하나로 처리한다.
    public bool CanAct => playerState != PlayerState.Stunned
                       && playerState != PlayerState.Dead
                       && playerState != PlayerState.NotReady;

    private void Awake()
    {
        myKnife.SetOwner(photonView.Owner.ActorNumber, registry.MyTeam);
        if (photonView.IsMine)
        {
            myRigidbody.isKinematic = false;
        }

        col = GetComponent<Collider>();
        parts = GetComponents<IPlayerPart>();
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        // 같은 오브젝트·같은 수명이라 해제는 필요 없다
        health.OnDied += Die;
    }

    private void OnEnable()
    {
        SetPlayerState(PlayerState.Idle);
        GameEvents.WeaponChanged(myKnife.WeaponType.ToString(), false);
        col.enabled = true;

        if(PhotonNetwork.IsMasterClient)
        { WeaponSpawnManager.Instance.RegisterGunCatalog(allGuns); }
    }

    // 스폰 메니저가 스폰시 초기 데이터 주입용
    public void Init(int _myTeam)
    {
        myTeam = _myTeam;

        if (allGuns != null)
        {
            foreach (var gun in allGuns)
                if (gun != null) gun.SetOwner(photonView.Owner.ActorNumber, myTeam);
        }
    }

    // 라운드 시작시 초기화 목적으로 호출
    public void Respawn(Vector3 _spawnPos)
    {
        SetPlayerState(PlayerState.Idle);
        gameObject.SetActive(true);
        myRigidbody.position = _spawnPos;
        dummyFlagMesh.SetActive(false);
        SetTeamColor(myTeam);
        col.enabled = true;

        foreach (var p in parts) p.OnRespawn();
    }

    // 라운드 종료 시 즉각적인 초기화 및 조작 차단
    public void OnRoundEndReset()
    {
        // 상태를 NotReady로 바꿔서 모든 조작(이동, 사격)을 막음
        if (GetPlayerState != PlayerState.Dead)
            SetPlayerState(PlayerState.NotReady);

        // 깃발 상태 초기화 및 시각적 가짜 깃발 끄기
        hasEnemyFlag = false;
        dummyFlagMesh.SetActive(false);

        CleanupWeapon();

        // 구르기 상태 코루틴이 돌고 있다면 정지 (구르기 이동은 PlayerMovement, 기절은 PlayerHealth가 각자 멈춤)
        StopAllCoroutines();

        foreach (var p in parts) p.OnRoundEndReset();
    }

    public void SetPlayerState(PlayerState _state)
    {
        playerState = _state;
    }

    public void SetTeamColor(int team)
    {
        Color col = team == 1 ? teamAColor : teamBColor;
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        armBandRenderer.GetPropertyBlock(mpb);
        mpb.SetColor("_Color", col);
        armBandRenderer.SetPropertyBlock(mpb);
    }

    #region 깃발 로직

    public void GetFlag()
    {
        hasEnemyFlag = true;
        dummyFlagMesh.SetActive(true);
    }

    #endregion

    #region 조작 로직

    // 밖(InputManager)에서 오는 이동 호출은 Core가 같은 이름으로 한 줄 중계한다. 논리는 PlayerMovement에.
    public void MovePlayer(Vector3 _moveAxis) => movement.MovePlayer(_moveAxis);
    public void RotatePlayer(Vector3 _aimPos) => movement.RotatePlayer(_aimPos);
    public void SprintStart(InputAction.CallbackContext ctx) => movement.SprintStart(ctx);
    public void SprintEnd(InputAction.CallbackContext ctx) => movement.SprintEnd(ctx);
    public void TryRoll(InputAction.CallbackContext ctx) => movement.TryRoll(ctx);

    #region 구르기 로직
    // 구르기 이동은 PlayerMovement가 소유자에서 처리하고, 무적 상태(Rolling)와 애니 이벤트는 Core가 모든 클라이언트에서 건다.
    // 지속시간은 보낸 쪽 값을 받아 쓴다(프리팹 수치가 Movement로 옮겨가 Core에 없음).
    [PunRPC]
    public void StartRollRPC(float duration)
    {
        StartCoroutine(RollingStateCoroutine(duration));
    }

    // 상태 설정용
    private IEnumerator RollingStateCoroutine(float duration)
    {
        OnRollEvent?.Invoke();

        SetPlayerState(PlayerState.Rolling);
        yield return new WaitForSeconds(duration);
        SetPlayerState(PlayerState.Idle);
    }

    #endregion

    #region 무기 전환
    public void TrySwapWeapon(InputAction.CallbackContext ctx)
    {
        if (!CanAct) return;
        if (myEquippedGun != null)
        {
            useGun = useGun ? false : true;

            photonView.RPC(nameof(SwapWeapon), RpcTarget.All, useGun);
        }
        else
        {
            useGun = false;
            photonView.RPC(nameof(SwapWeapon), RpcTarget.All, useGun);
        }
    }

    [PunRPC]
    private void SwapWeapon(bool _useGun)
    {

        if (myEquippedGun != null)
        {
            myEquippedGun.gameObject.SetActive(_useGun);

        }
        myKnife.gameObject.SetActive(!_useGun);

        if (photonView.IsMine)
        {
            string curWeaponName = useGun ? myEquippedGun.WeaponType.ToString() : myKnife.WeaponType.ToString();
            GameEvents.WeaponChanged(curWeaponName, _useGun);
        }
    }
    #endregion

    public void TryAttack(Vector3 _aimPos, bool _isHeld = false)
    {
        // 달리기·구르기 중 공격 금지는 기존 규칙 그대로
        if (!CanAct || playerState == PlayerState.Sprint || playerState == PlayerState.Rolling) return;

        if (useGun)
        {
            myEquippedGun.Attack(_isHeld);
        }

        else
        {
            if (Time.time < _meleeCooldownEnd) return;
            myKnife.Attack(_isHeld);
            // 근접 공격 애니는 원격에도 보여야 하므로 RPC 안에서 발화한다(총 연출은 Gun이 자체 RPC로 처리)
            photonView.RPC(nameof(AttackAnimRPC), RpcTarget.All);
            _meleeCooldownEnd = Time.time + meleeAttackCooldown;
        }

    }

    [PunRPC]
    private void AttackAnimRPC()
    {
        OnAttackEvent?.Invoke();
    }

    #region 던지기 , 줍기

    public void PickUpAndDrop(InputAction.CallbackContext ctx)
    {
        if (!CanAct) return;
        if (!photonView.IsMine || !ctx.performed) return;
        if (Time.time < _pickupCooldownEnd) return;

        // 1. 주변에 노드가 있으면 (가용 여부 무관) 드롭 차단
        if (closestNode != null)
        {
            if (closestNode.IsAvailable)
            {
                closestNode.RequestPickup(photonView.ViewID);
                // RPC 왕복 지연 동안 Q 재입력으로 TryThrow 방지
                _pickupCooldownEnd = Time.time + 0.5f;
            }
            closestNode = null;
            return;
        }
        // 2. 노드가 완전히 없을 때만 던지기
        if (myEquippedGun != null && useGun)
        {
            photonView.RPC(nameof(RPC_TryThrow), RpcTarget.All, (int)((Gun)myEquippedGun).WeaponType);
        }
    }

    #region 줍기
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

    private void DropWeapon()
    {
        if (myEquippedGun == null) return;

        if (photonView.IsMine && myEquippedGun is Gun droppedGun)
        {
            int type = (int)droppedGun.WeaponType;
            int ammo = droppedGun.CurrentAmmo;
            Vector3 dropPos = transform.position;

            WeaponSpawnManager.Instance?.photonView?.RPC(
                nameof(WeaponSpawnManager.RPC_CreateDropNode),
                RpcTarget.MasterClient,
                dropPos, type, ammo);
        }

        myEquippedGun.gameObject.SetActive(false);
        myEquippedGun = null;
        useGun = false;
    }

    [PunRPC]
    public void RPC_ForceEquipWeapon(int typeInt, int ammo)
    {
        Weapon.EWeaponType type = (Weapon.EWeaponType)typeInt;

        if (myEquippedGun != null) DropWeapon();

        Gun newGun = FindGunByType(type);
        if (newGun != null)
        {
            myEquippedGun = newGun;
            myEquippedGun.gameObject.SetActive(true);
            newGun.SetAmmo(ammo);
            useGun = true;
            photonView.RPC(nameof(SwapWeapon), RpcTarget.All, true);
        }

        // 장착 직후 Q 연타로 발 밑 노드를 즉시 줍는 루프 방지
        if (photonView.IsMine) _pickupCooldownEnd = Time.time + 0.4f;
    }

    [PunRPC]
    public void RPC_TryThrow(int weaponTypeInt)
    {
        if (myEquippedGun == null) return;

        if (myEquippedGun is Gun myGun)
        {
            if(photonView.IsMine)
            myGun.ThrowWeapon();

            OnThrowEvent?.Invoke();
        }

        CleanupWeapon();
        SwapWeapon(false);
    }

    private void CleanupWeapon()
    {
        if (myEquippedGun == null) return;
        myEquippedGun.gameObject.SetActive(false);
        myEquippedGun = null;
        useGun = false;
    }

    private Gun FindGunByType(Weapon.EWeaponType type)
    {
        if (allGuns == null) return null;
        foreach (var gun in allGuns)
            if (gun != null && gun.WeaponType == type) return gun;
        return null;
    }

    #endregion

    #endregion

    public void TryInteract(InputAction.CallbackContext ctx)
    {
        if (!CanAct) return;

        // 1. 내 주변 2m 안의 가구/오브젝트 검색
        Collider[] hits = Physics.OverlapSphere(transform.position, interactRadius, interactableLayer);

        foreach (var hit in hits)
        {
            // 2. 인터페이스 추출 시도
            if (hit.TryGetComponent<IInteractable>(out var target))
            {
                // 3. 된다면 실행
                target.Interact(this);
                // 상호작용 애니는 모든 클라이언트에서 보여야 하므로 RPC 안에서 발화한다(구르기·던지기·기절과 같은 패턴)
                photonView.RPC(nameof(InteractAnimRPC), RpcTarget.All);

                break; // 한 번에 하나만 상호작용
            }
        }
    }

    [PunRPC]
    private void InteractAnimRPC()
    {
        OnInteractEvent?.Invoke();
    }

    #endregion

    #region 사망
    // 피격·체력은 PlayerHealth가 맡고, hp가 0이 되면 Health의 OnDied가 이것을 부른다(Awake에서 구독).
    // 죽음의 후속 조치(상태·무기·깃발·몸·라운드 알림)는 생명주기라 Core가 순서를 가지고, 밖에서 죽음을 명령할 길은 없다.
    private void Die(int _attackerNum)
    {
        // 멱등 — OnDied가 두 번 와도 사망 처리(킬 집계 포함)는 한 번
        if (playerState == PlayerState.Dead) return;

        DropWeapon();
        SetPlayerState(PlayerState.Dead);
        RoundManager.Instance?.OnPlayerDied(this, _attackerNum);
        DropFlag();
        DisableBody();
        StartCoroutine(DeathRoutine());

        if (photonView.IsMine)
            GameEvents.LocalPlayerDeath();
    }

    private void DropFlag()
    {
        if (hasEnemyFlag)
        {
            hasEnemyFlag = false;
            dummyFlagMesh.SetActive(false);
        }
    }

    // 시체가 살아있는 플레이어 이동을 막지 않도록 물리를 멈추고 콜라이더를 끈다
    private void DisableBody()
    {
        myRigidbody.Sleep();
        if (col != null) col.enabled = false;
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(deathAnimDuration);
        gameObject.SetActive(false);

    }

    #endregion

}
