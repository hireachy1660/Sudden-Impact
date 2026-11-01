using Photon.Pun;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

// 이동·회전·달리기·구르기 부품. 소유자(IsMine)에서만 켜지고, 상태 읽기·쓰기는 Core(PlayerController)를 거친다.
public class PlayerMovement : MonoBehaviourPun, IPlayerPart
{
    [Header("Parameters")]
    // 기본값은 P_Player2 프리팹에 저장된 수치(2026-11-01 기준)와 맞춤
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float sprintSpeed = 1.4f;
    [SerializeField] private float rollDistance = 5f;
    [SerializeField] private float rollDuration = 0.4f;
    [SerializeField] private float rollCooldown = 4f;

    private PlayerController core;
    private Rigidbody rb;

    private Vector3 curMoveInput;
    private Vector3 lastMoveDir;
    private bool isSprinting;
    private float lastRollTime;

    // 애니메이터가 읽어갈 퍼블릭 게터
    public Vector3 CurrentVelocity => rb.linearVelocity;
    public float MoveSpeed => moveSpeed;
    public float NormalizedSpeed => CurrentVelocity.magnitude / moveSpeed;

    private void Awake()
    {
        core = GetComponent<PlayerController>();
        rb = GetComponent<Rigidbody>();
        enabled = photonView.IsMine;
    }

    private void FixedUpdate()
    {
        // 구르기 중에는 구르기 코루틴이 처리.
        if (core.GetPlayerState == PlayerController.PlayerState.Rolling) return;

        // 상태 이상일 때는 강제로 속도를 0으로 만들고 기절
        if (core.GetPlayerState == PlayerController.PlayerState.Stunned
            || core.GetPlayerState == PlayerController.PlayerState.Dead
            || core.GetPlayerState == PlayerController.PlayerState.NotReady)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
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
            rb.linearVelocity = targetVelocity;
        }
        else
        {
            // 입력이 없을 때는 즉시 속도를 0으로 만들어 빙판길 미끄러짐을 방지.
            rb.linearVelocity = Vector3.zero;
        }

        // 회전력은 매 프레임 죽여서 넘어지는 것을 방지
        rb.angularVelocity = Vector3.zero;
    }

    public void MovePlayer(Vector3 _moveAxis)
    {
        lastMoveDir = _moveAxis.normalized;
        curMoveInput = _moveAxis;
    }

    public void RotatePlayer(Vector3 _aimPos)
    {
        if (!core.CanAct || core.GetPlayerState == PlayerController.PlayerState.Rolling) return;

        Vector3 lookPos = _aimPos - transform.position;
        lookPos.y = 0;

        float distance = lookPos.magnitude;

        if (distance > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(lookPos);
            rb.MoveRotation(targetRotation);

            if (core.MyEquippedGun != null && core.WeaponAttachPoint != null)
            {
                core.MyEquippedGun.AttackPoint.LookAt(_aimPos);
            }
        }
    }

    public void SprintStart(InputAction.CallbackContext ctx)
    {
        if (!core.CanAct) return;

        isSprinting = true;
        core.SetPlayerState(PlayerController.PlayerState.Sprint);
    }

    public void SprintEnd(InputAction.CallbackContext ctx)
    {
        isSprinting = false;
        // 기절·사망 중에 Shift를 떼도 그 상태를 Idle로 덮어쓰지 않는다
        if (core.GetPlayerState == PlayerController.PlayerState.Sprint) core.SetPlayerState(PlayerController.PlayerState.Idle);
    }

    public void TryRoll(InputAction.CallbackContext ctx)
    {
        if (!core.CanAct) return;
        if (Time.time < lastRollTime + rollCooldown)
        {
            return;
        }

        // 이동 코루틴
        StartCoroutine(RollCoroutine());

        // 구르기 쿨다운 시작 이벤트 → PlayerWorldUI의 쿨다운 링 애니메이션 트리거
        // TryRoll은 로컬 플레이어 입력에서만 호출되므로 IsMine 보장됨
        GameEvents.RollStarted(rollCooldown);

        // 무적 RPC. 상태(Rolling)와 애니 이벤트는 Core가 모든 클라이언트에서 걸고, 지속시간은 여기 값을 보낸다
        photonView.RPC(nameof(PlayerController.StartRollRPC), RpcTarget.All, rollDuration);
        lastRollTime = Time.time;
    }

    private IEnumerator RollCoroutine()
    {
        if (core.GetPlayerState == PlayerController.PlayerState.Rolling
            || core.GetPlayerState == PlayerController.PlayerState.Stunned
            || core.GetPlayerState == PlayerController.PlayerState.Dead)
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

        rb.MoveRotation(Quaternion.LookRotation(rollDirection));

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
            rb.linearVelocity = rollDirection * currentSpeed;

            yield return new WaitForFixedUpdate();
        }

        // 구르기가 끝난 후 잔여 속도를 소멸시켜 미끄러짐 방지
        rb.linearVelocity = Vector3.zero;
    }

    public void OnRespawn() { }

    public void OnRoundEndReset()
    {
        StopAllCoroutines();
        curMoveInput = Vector3.zero;
        isSprinting = false;
    }
}
