using Photon.Pun;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using static Photon.Pun.UtilityScripts.PunTeams;

public class PlayerController : MonoBehaviourPun, IAttackReceiver
{
    public delegate void StunDelegate(bool _isInStun);

    private StunDelegate stunCallback;

    public StunDelegate StunCallback { set { stunCallback = value; } }

    public enum PlayerState
    {
        NotReady, Idle, Sprint, Rolling, Stunned, Dead
    }
    [Header("PlayerReference")]
    [SerializeField] private Rigidbody myRigidbody;
    [SerializeField] private PhotonTransformView myTransformView;
    [SerializeField] private Transform weaponAttachPoint;
    [SerializeField] private Weapon myKnife;
    [SerializeField] private PlayerRegistry registry;
    [SerializeField] private GameObject dummyFlagMesh;
    [Tooltip("혈흔 프리팹")]
    [SerializeField] private GameObject neonBloodPrefab; 

    [Header("Parameters")]
    [SerializeField] private float maxHp = 100f;
    [SerializeField] private float moveSpeed = 10f;
    [SerializeField] private float sprintSpeed = 1.4f;
    [SerializeField] private float rollDistance = 2.0f;
    [SerializeField] private float rollDuration = 0.2f;
    [SerializeField] private float pickUpDistance = 1f;
    [SerializeField] private float stunDuration = 1.5f;
    [SerializeField] private float rollCooldown = 4f;
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

    [Header("Camera Shake (Damage)")]
    [SerializeField] private CinemachineImpulseSource damageImpulseSource;
    [SerializeField] private float damageShakeForce = 1.5f;

    [Header("Animator Bridge Data")]
    // 애니메이터가 읽어갈 퍼블릭 게터
    public Vector3 CurrentVelocity => myRigidbody.linearVelocity;
    public float MoveSpeed => moveSpeed;
    public float NormalizedSpeed => CurrentVelocity.magnitude / moveSpeed;
    public bool UseGun => useGun;

    // 애니메이터에게 행동을 알리는 이벤트
    public event Action OnAttackEvent;
    public event Action OnRollEvent;
    public event Action OnThrowEvent;
    public event Action OnInteractEvent;
    public event Action OnStunnedEvent;
    public event Action OnHitEvent;

    [Header("ForDebug")]
    [SerializeField] private float curHp;
    [SerializeField] private WeaponPickupNode closestNode; // 최단거리 노드
    [SerializeField] private Weapon myEquippedGun;
    [SerializeField] private bool useGun;
    [SerializeField] private bool hasEnemyFlag;
    [SerializeField] private int myTeam;
    [SerializeField] private PlayerState playerState;

    private Coroutine curCheckClosestNodeCoroutine; 
    private List<WeaponPickupNode> nearbyNodes = new List<WeaponPickupNode>(); private Vector3 curMoveInput;
    private Vector3 lastMoveDir;
    private bool isSprinting;
    private float lastRollTime;
    private float _pickupCooldownEnd = 0f;
    private float _meleeCooldownEnd = 0f;
    Collider col;

    public int MyTeam { get { return myTeam; } }
    public bool HasEnemyFlag { get { return hasEnemyFlag; } }
    public PlayerState GetPlayerState { get { return playerState; } }
    public Weapon MyEquippedGun { get { return myEquippedGun; } }

    private void Awake()
    {
        myKnife.SetOwner(photonView.Owner.ActorNumber, registry.MyTeam);
        if (photonView.IsMine)
        {
            myRigidbody.isKinematic = false;
        }

        col = GetComponent<Collider>();
    }

    private void OnEnable()
    {
        curHp = maxHp;
        SetPlayerState(PlayerState.Idle);
        GameEvents.WeaponChanged(myKnife.WeaponType.ToString(), false);
        col.enabled = true;

        if(PhotonNetwork.IsMasterClient)
        { WeaponSpawnManager.Instance.RegisterGunCatalog(allGuns); }
    }

    private void OnDisable()
    {
        // 죽거나 꺼질 때, 만약 내가 기절 상태였다면 입력을 강제로 복구하고 나감
        if (photonView.IsMine)
        {
            stunCallback?.Invoke(true);
        }
    }

    private void FixedUpdate()
    {
        if (!photonView.IsMine) return;

        // 구르기 중에는 구르기 코루틴이 처리.
        if (playerState == PlayerState.Rolling) return;

        // 상태 이상일 때는 강제로 속도를 0으로 만들고 기절
        if (playerState == PlayerState.Stunned || playerState == PlayerState.Dead || playerState == PlayerState.NotReady)
        {
            myRigidbody.linearVelocity = Vector3.zero;
            myRigidbody.angularVelocity = Vector3.zero;
            return;
        }

        // MovePosition을 버리고 linearVelocity(물리 속도)를 직접 제어합니다.
        if (curMoveInput.sqrMagnitude > 0.01f)
        {
            float currentSpeed = isSprinting ? (moveSpeed * sprintSpeed) : moveSpeed;

            // 입력 방향으로 목표 속도를 계산합니다.
            Vector3 targetVelocity = curMoveInput.normalized * currentSpeed;

            // 물리 엔진의 속도(Velocity)에 직접 값을 넣습니다. 
            // 엔진이 스스로 충돌을 계산하므로 아무리 얇은 벽이라도 절대 뚫지 못합니다.
            myRigidbody.linearVelocity = targetVelocity;
        }
        else
        {
            // 입력이 없을 때는 즉시 속도를 0으로 만들어 빙판길 미끄러짐을 방지.
            myRigidbody.linearVelocity = Vector3.zero;
        }

        // 회전력은 매 프레임 죽여서 넘어지는 것을 방지
        myRigidbody.angularVelocity = Vector3.zero;
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
        curHp = maxHp;
        SetPlayerState(PlayerState.Idle);
        gameObject.SetActive(true);
        myRigidbody.position = _spawnPos;
        dummyFlagMesh.SetActive(false);
        GameEvents.HpChanged(curHp);
        SetTeamColor(myTeam);
        col.enabled = true;
    }

    // 라운드 종료 시 즉각적인 초기화 및 조작 차단
    public void OnRoundEndReset()
    {
        Debug.Log($"[OnRoundEndReset] called, myEquippedGun = {myEquippedGun?.name ?? "null"}");

        // 상태를 NotReady로 바꿔서 모든 조작(이동, 사격)을 막음
        if (GetPlayerState != PlayerState.Dead)
            SetPlayerState(PlayerState.NotReady);

        // 깃발 상태 초기화 및 시각적 가짜 깃발 끄기
        hasEnemyFlag = false;
        dummyFlagMesh.SetActive(false);

        
        CleanupWeapon();

        //구르기나 기절 코루틴이 돌고 있다면 정지
        StopAllCoroutines();
    }

    private void SetPlayerState(PlayerState _state)
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

    #region 이동 및 방향 전환
    public void MovePlayer(Vector3 _moveAxis)
    {
        lastMoveDir = _moveAxis.normalized;
        curMoveInput = _moveAxis;
    }

    public void RotatePlayer(Vector3 _aimPos)
    {
        if (playerState == PlayerState.Rolling
            || playerState == PlayerState.Dead
            || playerState == PlayerState.NotReady) return;

        Vector3 lookPos = _aimPos - transform.position;
        lookPos.y = 0;

        float distance = lookPos.magnitude;

        if (distance > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookPos);
            myRigidbody.MoveRotation(targetRotation);

            if (myEquippedGun != null && weaponAttachPoint != null)
            {
                 myEquippedGun.AttackPoint.LookAt(_aimPos);
            }
        }
    }
    #endregion

    #region 구르기 로직
    public void TryRoll(InputAction.CallbackContext ctx)
    {
        if (Time.time < lastRollTime + rollCooldown)
        {
            Debug.Log("[PlayerController] Rolling is Cooling Down");
            return;
        }

        // 이동 코루틴
        StartCoroutine(RollCoroutine());

        // 구르기 쿨다운 시작 이벤트 → PlayerWorldUI의 쿨다운 링 애니메이션 트리거
        // TryRoll은 로컬 플레이어 입력에서만 호출되므로 IsMine 보장됨
        GameEvents.RollStarted(rollCooldown);

        // 무적 RPC
        photonView.RPC(nameof(StartRollCRP), RpcTarget.All);
        lastRollTime = Time.time;

    }

    [PunRPC]
    public void StartRollCRP()
    {
        StartCoroutine(RollingStateCoroutine());
    }

    private IEnumerator RollCoroutine()
    {
        if (playerState == PlayerState.Rolling
            || playerState == PlayerState.Stunned
            || playerState == PlayerState.Dead)
            yield break;



        Vector3 rollDirection;
        if (Vector3.SqrMagnitude(lastMoveDir) > 0.2f)
        {
            rollDirection = lastMoveDir;
        }
        else
        {
            rollDirection = transform.forward;
        }

        myRigidbody.MoveRotation(Quaternion.LookRotation(rollDirection));

        float startSpeed = (rollDistance / rollDuration) * 2f;

        float elapsed = 0f;


        while (elapsed < rollDuration)
        {
            // 물리 프레임 시간 누적
            elapsed += Time.fixedDeltaTime;
            float t = elapsed / rollDuration;

            // 속도를 선형적으로 줄임(EaseOut)
            float currentSpeed = Mathf.Lerp(startSpeed, 0f, t);

            // 오직 벨로시티(Velocity)만으로 구르기
            myRigidbody.linearVelocity = rollDirection * currentSpeed;

            yield return new WaitForFixedUpdate();
        }

        // 구르기가 끝난 후 잔여 속도를 소멸시켜 미끄러짐 방지
        myRigidbody.linearVelocity = Vector3.zero;

    }

    // 상태 설정용
    private IEnumerator RollingStateCoroutine()
    {
        OnRollEvent?.Invoke();

        playerState = PlayerState.Rolling;
        yield return new WaitForSeconds(rollDuration);
        playerState = PlayerState.Idle;
    }

    #endregion

    #region 무기 전환
    public void TrySwapWeapon(InputAction.CallbackContext ctx)
    {
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

    #region 달리기
    public void SprintStart(InputAction.CallbackContext ctx)
    {
        // 기절, 사망 등 조작 불능 상태면 달리기 무시
        if (playerState == PlayerState.NotReady || playerState == PlayerState.Stunned || playerState == PlayerState.Dead) return;

        isSprinting = true;
        playerState = PlayerState.Sprint;
        // 필요하다면 여기서 playerState = PlayerState.Sprint; 로 변경해도 좋습니다.
        Debug.Log("[PlayerController] Im Start Sprinting");
    }

    public void SprintEnd(InputAction.CallbackContext ctx)
    {
        isSprinting = false;
        playerState = PlayerState.Idle;

        Debug.Log("[PlayerController] Im end Sprinting");
    }
    #endregion

    public void TryAttack(Vector3 _aimPos, bool _isHeld = false)
    {
        if (playerState != PlayerState.Idle) return;
        

        if (useGun)
        {
            myEquippedGun.Attack(_isHeld);
        }

        else
        {
            if (Time.time < _meleeCooldownEnd) return;
            myKnife.Attack(_isHeld);
            OnAttackEvent?.Invoke();
            _meleeCooldownEnd = Time.time + meleeAttackCooldown;
        }

    }

    #region 던지기 , 줍기

    public void PickUpAndDrop(InputAction.CallbackContext ctx)
    {
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
                    Debug.Log("[Player] PickUpUIOff");
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
            Debug.Log("[Player] PickUpUIOff");
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
        Debug.Log($"[PlayerController] ForceEquipWeapon Called type : {typeInt}, Ammo : {ammo}");
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

    #region 던지기

    [PunRPC]
    private void TryThrow(int _viewID)
    {
        // 내 손에 총이 없거나, 던지라는 총의 ID가 내 손의 총과 다르면 무시
        if (myEquippedGun == null || myEquippedGun.photonView.ViewID != _viewID) return;

        // 무기가 Gun일 때만 던지기(ThrowWeapon) 실행
        if (myEquippedGun is Gun myGun)
        {
            myGun.ThrowWeapon();
            OnThrowEvent?.Invoke();
        }
        else
        {
            // 만약 칼(MeleeKnife)도 나중에 던지는 기능이 생긴다면
            Debug.Log("이 무기는 던질 수 없는 무기입니다 (Gun이 아님).");
        }

        if (photonView.IsMine)
        {
            PhotonNetwork.Destroy(myEquippedGun.gameObject);
        }

        myEquippedGun = null;
        useGun = false;
        SwapWeapon(false);
    }

    #endregion

    #endregion

    public void TryInteract(InputAction.CallbackContext ctx)
    {
        // 1. 내 주변 2m 안의 가구/오브젝트 검색
        Collider[] hits = Physics.OverlapSphere(transform.position, interactRadius, interactableLayer);

        foreach (var hit in hits)
        {
            // 2. 인터페이스 추출 시도
            if (hit.TryGetComponent<IInteractable>(out var target))
            {
                // 3. 된다면 실행
                target.Interact(this);

                break; // 한 번에 하나만 상호작용
            }
        }
    }

    #endregion

    #region OnImpact
    public void OnReceiveImpact(ImpactData _data)
    {
        Debug.Log($"[PlayerController] On Impact actor: {_data.attackerActorNumber}, ATKteam : {_data.attackerTeam}, myTeam{myTeam} , Data : {_data.type}");
        // 상태 검사
        if (
            curHp <= 0
            || playerState == PlayerState.NotReady
            || playerState == PlayerState.Dead
            || playerState == PlayerState.Rolling
            || _data.attackerTeam == myTeam
            ) { return; }

        

        if (_data.type == DamageType.Throw)
        {
            StunPlayer();
            return;
        }

        //내가 쏜 총알이 아니면 데미지 RPC호출
        if (_data.attackerActorNumber != photonView.Owner.ActorNumber)
        {
            photonView.RPC(nameof(TakeDamage), RpcTarget.All, _data.damage, _data.hitNormal, _data.attackerActorNumber);
            Debug.Log($"[PlayerController] Received");
        }

    }

    [PunRPC]
    public void TakeDamage(float _damage, Vector3 _hitNormal, int _attackerNum)
    {
        curHp -= _damage;
        OnHitEvent?.Invoke();
        Debug.Log($"[PlayerController] <color=red> Hit </color> {photonView.Owner.ActorNumber}'s Hp Is : {curHp}");

        // 피격 카메라 쉐이크
        if (photonView.IsMine && damageImpulseSource != null)
        {
            GameEvents.HpChanged(curHp);
            if (damageImpulseSource != null)
            {
                Vector3 shakeDir = -_hitNormal;
                shakeDir.y = 0f;
                if (shakeDir.sqrMagnitude < 0.01f) shakeDir = Vector3.back;
                { damageImpulseSource.GenerateImpulseWithVelocity(shakeDir.normalized * damageShakeForce); }
            }
        }

        Vector3 bloodPos = new Vector3(transform.position.x, -1.35f, transform.position.z);
        Instantiate(neonBloodPrefab, bloodPos, Quaternion.identity);

        if (curHp <= 0)
        {
            DiePlayer(_attackerNum);

            if(photonView.IsMine) 
            GameEvents.LocalPlayerDeath();
        }
    }

    private void DiePlayer(int _attackerNum)
    {
        DropWeapon();
        playerState = PlayerState.Dead;
        // 인풋 메니저에게 콜백 필요
        RoundManager.Instance?.OnPlayerDied(this, _attackerNum);

        if (hasEnemyFlag)
        {
            hasEnemyFlag = false;
            dummyFlagMesh.SetActive(false);
        }
        myRigidbody.Sleep();

        // 시체가 살아있는 플레이어 이동을 막지 않도록 콜라이더 비활성화
        if (col != null) col.enabled = false;

        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        yield return new WaitForSeconds(deathAnimDuration);
        // this.gameObject.SetActive(false);
        gameObject.SetActive(false);
        
    }



    private void StunPlayer()
    {
        photonView.RPC(nameof(StunRPC), RpcTarget.All);

    }

    [PunRPC]
    private void StunRPC()
    {
        if (!gameObject.activeSelf) return;
        StartCoroutine(StunCoroutine());
        OnStunnedEvent?.Invoke();
    }

    private IEnumerator StunCoroutine()
    {
        Debug.Log($"[PlayerController] {photonView.Owner.ActorNumber} is Stuned");

        if (photonView.IsMine)
        {
            playerState = PlayerState.Stunned;
            stunCallback?.Invoke(false);
            yield return new WaitForSeconds(stunDuration);
            playerState = PlayerState.Idle;
            stunCallback?.Invoke(true);
        }
        else
        {
            playerState = PlayerState.Stunned;
            yield return new WaitForSeconds(stunDuration);
            playerState = PlayerState.Idle;
        }
    }

    #endregion

}
