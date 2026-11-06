using Photon.Pun;
using System;
using System.Collections;
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

    [Header("Pre-Allocated Weapons")]
    [Tooltip("미리 모든 종류의 총을 등록")]
    [SerializeField] private Gun[] allGuns;

    [Header("Animator Bridge Data")]
    // 애니메이터가 읽어갈 퍼블릭 게터. 원격 복제본에서도 읽으므로 Core가 가진다(PlayerMovement는 원격에서 지워진다).
    public bool UseGun => useGun;
    public Vector3 CurrentVelocity => myRigidbody.linearVelocity;
    public float MoveSpeed { get; private set; }
    public float NormalizedSpeed => CurrentVelocity.magnitude / MoveSpeed;

    // 애니메이터에게 행동을 알리는 이벤트
    public event Action OnAttackEvent;
    public event Action OnRollEvent;
    public event Action OnThrowEvent;
    public event Action OnInteractEvent;
    // 장착 직후 줍기 쿨다운을 PlayerInteraction이 걸도록 알리는 이벤트(소유자에서만 발화)
    public event Action OnEquipped;

    [Header("ForDebug")]
    [SerializeField] private Weapon myEquippedGun;
    [SerializeField] private bool useGun;
    [SerializeField] private bool hasEnemyFlag;
    [SerializeField] private int myTeam;
    [SerializeField] private PlayerState playerState;

    private float _meleeCooldownEnd = 0f;
    Collider col;
    // 같은 오브젝트에 붙은 부품들. Core가 리스폰·라운드 종료를 받아 순회하며 알린다.
    private IPlayerPart[] parts;
    private PlayerMovement movement;
    private PlayerHealth health;
    private PlayerInteraction interaction;

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
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        interaction = GetComponent<PlayerInteraction>();
        // 같은 오브젝트·같은 수명이라 해제는 필요 없다
        health.OnDied += Die;

        // 애니메이터가 원격에서도 읽는 값이라 부품을 지우기 전에 Core가 보관한다
        MoveSpeed = movement.MoveSpeed;

        CollectParts();
    }

    // 소유자 전용 부품(IOwnerOnlyPart)은 원격 복제본에 둘 이유가 없다 — 끄고 지운 뒤, 남는 부품만 순회 대상으로 삼는다.
    // Destroy는 프레임 끝에 실행되므로 그 사이 콜백이 돌지 않게 먼저 끈다.
    private void CollectParts()
    {
        IPlayerPart[] all = GetComponents<IPlayerPart>();

        if (photonView.IsMine)
        {
            parts = all;
            return;
        }

        foreach (IPlayerPart part in all)
        {
            if (part is IOwnerOnlyPart)
            {
                Behaviour behaviour = (Behaviour)part;
                behaviour.enabled = false;
                Destroy(behaviour);
            }
        }

        parts = Array.FindAll(all, part => !(part is IOwnerOnlyPart));
        // 원격에서는 중계가 불리지 않는다(InputManager는 로컬 플레이어만 바인딩) — 지운 부품의 참조를 들고 있지 않는다
        movement = null;
        interaction = null;
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
    public void PickUpAndDrop(InputAction.CallbackContext ctx) => interaction.PickUpAndDrop(ctx);
    public void TryInteract(InputAction.CallbackContext ctx) => interaction.TryInteract(ctx);

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

        // useGun은 여기서 쓰지 않는다 — 보낼 값만 정하고, 실제 갱신은 모든 클라이언트의 SwapWeapon이 한다
        bool nextUseGun = myEquippedGun != null && !useGun;
        photonView.RPC(nameof(SwapWeapon), RpcTarget.All, nextUseGun);
    }

    [PunRPC]
    private void SwapWeapon(bool _useGun)
    {
        // 전환 상태를 받는 쪽 모두가 갱신한다 — 원격의 애니메이터도 이 값으로 무기 자세를 정한다.
        // 총이 없는데 true가 오면 칼로 둔다.
        useGun = _useGun && myEquippedGun != null;

        if (myEquippedGun != null)
        {
            myEquippedGun.gameObject.SetActive(useGun);
        }
        myKnife.gameObject.SetActive(!useGun);

        if (photonView.IsMine)
        {
            string curWeaponName = useGun ? myEquippedGun.WeaponType.ToString() : myKnife.WeaponType.ToString();
            GameEvents.WeaponChanged(curWeaponName, useGun);
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

    #region 무기 장착·던지기

    // 사망처럼 총을 잃고 끝나는 경로 — 총을 놓은 뒤 칼을 켠다
    private void DropWeapon()
    {
        if (myEquippedGun == null) return;

        ReleaseGun();

        // 장착한 총이 없으면 칼이 켜져 있어야 한다 — 총이 없어지는 경로는 모두 칼을 켠다
        SwapWeapon(false);
    }

    // 장착한 총을 놓는다: 소유자가 드롭 노드를 요청하고 총 오브젝트를 끈다.
    // 칼은 켜지 않는다 — 곧바로 다른 총을 장착하는 교체 경로에서 무기 변경 이벤트(HUD·전환음)가 두 번 나가지 않게 따로 뗐다.
    private void ReleaseGun()
    {
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

        // 교체: 들고 있던 총만 놓는다. 칼은 켜지 않고 바로 새 총을 장착한다
        if (myEquippedGun != null) ReleaseGun();

        Gun newGun = FindGunByType(type);
        if (newGun != null)
        {
            myEquippedGun = newGun;
            myEquippedGun.gameObject.SetActive(true);
            newGun.SetAmmo(ammo);
            useGun = true;
            // 이 RPC는 이미 모든 클라이언트에서 실행 중이다 — 여기서 다시 방송하면 전환이 클라이언트 수만큼 반복된다
            SwapWeapon(true);
        }

        // 장착 직후 Q 연타로 발 밑 노드를 즉시 줍는 루프 방지 — 쿨다운은 PlayerInteraction이 이 알림을 받아 건다
        if (photonView.IsMine) OnEquipped?.Invoke();
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
    }

    private void CleanupWeapon()
    {
        if (myEquippedGun == null) return;
        myEquippedGun.gameObject.SetActive(false);
        myEquippedGun = null;
        useGun = false;

        // DropWeapon과 같은 이유 — 총을 치운 뒤에는 칼을 켠다
        SwapWeapon(false);
    }

    private Gun FindGunByType(Weapon.EWeaponType type)
    {
        if (allGuns == null) return null;
        foreach (var gun in allGuns)
            if (gun != null && gun.WeaponType == type) return gun;
        return null;
    }

    #endregion

    // 상호작용 애니 이벤트의 RPC 입구. 논리는 PlayerInteraction에 있지만 RPC 수신자는 모든 클라이언트에 있어야 하므로 Core에 둔다.
    [PunRPC]
    public void InteractAnimRPC()
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
