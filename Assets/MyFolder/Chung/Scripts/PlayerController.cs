using Photon.Pun;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 플레이어 Core — 상태·팀·깃발·생명주기와 입력 중계. 부품(IPlayerPart)을 Awake에서 수집하고, 원격 복제본에서는 소유자 전용 부품을 끄고 지운다.
/// <para>부품 규칙</para>
/// <para>① 부품이 동작을 정의하고, 어떤 부품을 어떤 순서로 쓰는가(입력 중계·Die의 순서·Q 키의 줍기 우선)는 Core가 정한다.</para>
/// <para>② 상태 쓰기는 SetPlayerState 하나. 부품은 Core를 읽기만 한다.</para>
/// <para>③ 알림은 사실의 주인이 자기 이벤트로 — Health = 피격·기절, Weapon = 공격·던지기·장착, Core = 구르기·상호작용. 연출(PlayerAnimator)은 듣기만 한다.</para>
/// <para>④ 네트워크: RPC 수신자는 모든 클라이언트의 같은 오브젝트에 있어야 하므로 부품이 둘로 나뉜다 — 모든 클라이언트 부품(Health·Weapon: RPC와 원격이 읽는 값을 가짐)과
/// 소유자 전용 부품(Movement·Interaction: IOwnerOnlyPart, RPC와 원격이 읽는 값이 없음). 원격 애니메이터가 읽는 값(속도·상태·무기)은 Core 또는 모든 클라이언트 부품에 둔다.</para>
/// </summary>
public class PlayerController : MonoBehaviourPun
{
    public enum PlayerState
    {
        NotReady, Idle, Sprint, Rolling, Stunned, Dead
    }
    [Header("PlayerReference")]
    [SerializeField] private Rigidbody myRigidbody;
    [SerializeField] private PlayerRegistry registry;
    [SerializeField] private GameObject dummyFlagMesh;

    [Header("Parameters")]
    [SerializeField] private float deathAnimDuration = 2.0f;

    [SerializeField] private Renderer armBandRenderer;
    [SerializeField] private Color teamAColor = Color.blue;
    [SerializeField] private Color teamBColor = Color.red;

    // 애니메이터가 읽어갈 퍼블릭 게터. 원격 복제본에서도 읽으므로 Core가 가진다(PlayerMovement는 원격에서 지워진다).
    public Vector3 CurrentVelocity => myRigidbody.linearVelocity;
    public float MoveSpeed { get; private set; }
    public float NormalizedSpeed => CurrentVelocity.magnitude / MoveSpeed;

    // 애니메이터에게 행동을 알리는 이벤트(공격·던지기 이벤트는 PlayerWeapon에 있다)
    public event Action OnRollEvent;
    public event Action OnInteractEvent;

    [Header("ForDebug")]
    [SerializeField] private bool hasEnemyFlag;
    [SerializeField] private int myTeam;
    [SerializeField] private PlayerState playerState;

    private Collider col;
    // 같은 오브젝트에 붙은 부품들. Core가 리스폰·라운드 종료를 받아 순회하며 알린다.
    private IPlayerPart[] parts;
    private PlayerMovement movement;
    private PlayerHealth health;
    private PlayerInteraction interaction;
    private PlayerWeapon weapon;

    // Q 키 잠금 종료 시각. 줍기 요청 뒤(RPC 왕복 동안 재입력이 던지기로 새지 않게)와 장착 직후(발밑에 생긴 드롭 노드를 바로 되줍지 않게) 건다.
    // 줍기와 던지기를 함께 막는 잠금이라 Q를 가르는 Core가 가진다.
    private float _pickupLockEnd;
    private const float PickupLockAfterRequest = 0.5f;
    private const float PickupLockAfterEquip = 0.4f;

    public int MyTeam { get { return myTeam; } }
    public bool HasEnemyFlag { get { return hasEnemyFlag; } }
    public PlayerState GetPlayerState { get { return playerState; } }
    // 조준점(월드 좌표)은 입력 상태라 Core가 보관한다 — 던지기처럼 조준점 인자가 없는 경로가 읽는다. 소유자에서만 갱신된다.
    public Vector3 AimPosition { get; private set; }

    // 입력 진입점 공통 가드. 기절·사망·준비 전에는 어떤 조작도 받지 않는다.
    // 기절 중 입력 잠금을 InputManager의 액션맵 토글 대신 이 가드 하나로 처리한다.
    public bool CanAct => playerState != PlayerState.Stunned
                       && playerState != PlayerState.Dead
                       && playerState != PlayerState.NotReady;

    private void Awake()
    {
        if (photonView.IsMine)
        {
            myRigidbody.isKinematic = false;
        }

        col = GetComponent<Collider>();
        movement = GetComponent<PlayerMovement>();
        health = GetComponent<PlayerHealth>();
        interaction = GetComponent<PlayerInteraction>();
        weapon = GetComponent<PlayerWeapon>();
        // 같은 오브젝트·같은 수명이라 해제는 필요 없다(OnEquipped는 소유자에서만 발화한다)
        health.OnDied += Die;
        weapon.OnEquipped += LockPickupAfterEquip;
        // 칼의 소유자 정보 — 팀 값의 출처(registry)를 Core가 가지고 있어 Core가 넣어 준다
        weapon.SetKnifeOwner(photonView.Owner.ActorNumber, registry.MyTeam);

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
        }
        else
        {
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

        // 부품 수집이 Core의 일이라 Core 참조도 Core가 넣어 준다 — 부품마다 GetComponent로 Core를 찾지 않는다.
        // 지워질 부품에는 넣지 않는다(지워지기 전 창에 들어오는 콜백은 물리 콜백뿐이고, 그쪽은 IsMine 검사가 먼저다).
        foreach (IPlayerPart part in parts) part.Bind(this);
    }

    private void OnEnable()
    {
        SetPlayerState(PlayerState.Idle);
        col.enabled = true;
    }

    /// <summary>스폰 매니저가 스폰 시 초기 데이터 주입용</summary>
    /// <param name="_myTeam">팀 번호(1 = A, 그 외 = B). 총 4자루의 소유자 정보에도 넣는다</param>
    public void Init(int _myTeam)
    {
        myTeam = _myTeam;

        weapon.SetGunsOwner(photonView.Owner.ActorNumber, myTeam);
    }

    /// <summary>라운드 시작 시 초기화 목적으로 호출</summary>
    /// <param name="_spawnPos">리스폰 위치(월드 좌표) — Rigidbody.position으로 바로 옮긴다</param>
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

        // 구르기 상태 코루틴이 돌고 있다면 정지 (구르기 이동은 PlayerMovement, 기절은 PlayerHealth, 무기 정리는 PlayerWeapon이 각자 맡음)
        StopAllCoroutines();

        foreach (var p in parts) p.OnRoundEndReset();
    }

    public void SetPlayerState(PlayerState _state)
    {
        playerState = _state;
    }

    /// <param name="team">1이면 teamAColor, 그 외는 teamBColor</param>
    public void SetTeamColor(int team)
    {
        Color teamColor = team == 1 ? teamAColor : teamBColor;
        MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        armBandRenderer.GetPropertyBlock(mpb);
        mpb.SetColor("_Color", teamColor);
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

    // 밖(InputManager)에서 오는 호출은 Core가 같은 이름으로 중계한다. 논리는 각 부품(이동·상호작용·무기)에.
    // 블록인 둘(RotatePlayer·PickUpAndDrop)만 Core의 일(조준점 보관·Q 키의 순서)이 한 줄 섞여 있다.
    public void MovePlayer(Vector3 _moveAxis) => movement.MovePlayer(_moveAxis);
    public void RotatePlayer(Vector3 _aimPos)
    {
        AimPosition = _aimPos;
        movement.RotatePlayer(_aimPos);
    }
    public void SprintStart(InputAction.CallbackContext ctx) => movement.SprintStart(ctx);
    public void SprintEnd(InputAction.CallbackContext ctx) => movement.SprintEnd(ctx);
    public void TryRoll(InputAction.CallbackContext ctx) => movement.TryRoll(ctx);
    // Q 키의 규칙 — 잠금 중이면 아무것도 안 함, 노드가 있으면 줍기, 아니면 던지기.
    // 두 부품을 어떤 순서로 쓰는가와 그 사이의 잠금은 Core의 일이라 여기서 가른다. 공통 가드(CanAct·performed)도 여기.
    public void PickUpAndDrop(InputAction.CallbackContext ctx)
    {
        if (!CanAct || !ctx.performed) return;
        if (Time.time < _pickupLockEnd) return;

        PlayerInteraction.PickUpResult result = interaction.TryPickUp();
        if (result == PlayerInteraction.PickUpResult.Requested)
        {
            _pickupLockEnd = Time.time + PickupLockAfterRequest;
        }
        if (result != PlayerInteraction.PickUpResult.None) return;

        weapon.TryThrow();
    }

    // 장착이 도착한 직후 Q 연타로 발밑 노드(교체로 떨어진 총)를 즉시 되줍는 루프 방지
    private void LockPickupAfterEquip()
    {
        _pickupLockEnd = Time.time + PickupLockAfterEquip;
    }
    public void TryInteract(InputAction.CallbackContext ctx) => interaction.TryInteract(ctx);
    public void TryAttack(Vector3 _aimPos, bool _isHeld = false) => weapon.TryAttack(_aimPos, _isHeld);
    public void TrySwapWeapon(InputAction.CallbackContext ctx) => weapon.TrySwapWeapon(ctx);

    #region 구르기 로직
    // 구르기 이동은 PlayerMovement가 소유자에서 처리하고, 무적 상태(Rolling)와 애니 이벤트는 Core가 모든 클라이언트에서 건다.
    // 지속시간은 보낸 쪽 값을 받아 쓴다(프리팹 수치가 Movement로 옮겨가 Core에 없음).
    /// <param name="duration">구르기 지속 시간(초) — 보낸 쪽 PlayerMovement의 rollDuration</param>
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

        weapon.DropWeapon();
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
