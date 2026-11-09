using Photon.Pun;
using System;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 체력·피격·기절 부품. 모든 클라이언트에서 돌며 TakeDamage·StunRPC의 입구다.
/// 기절은 코루틴 없이 종료 시각(PhotonNetwork.Time)으로 관리한다.
/// 사망의 "때"(hp가 0)만 알고 OnDied로 알린다. 후속 조치는 이를 구독한 Core가 순서를 가진다.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerHealth : MonoBehaviourPun, IAttackReceiver, IPlayerPart
{
    [Tooltip("혈흔 프리팹")]
    [SerializeField] private GameObject neonBloodPrefab;

    [Header("Parameters")]
    [SerializeField] private float maxHp = 100f;
    [SerializeField] private float stunDuration = 1.5f;

    [Header("Camera Shake (Damage)")]
    [SerializeField] private CinemachineImpulseSource damageImpulseSource;
    [SerializeField] private float damageShakeForce = 1.5f;

    [Header("ForDebug")]
    [SerializeField] private float curHp;

    private PlayerController core;
    // 기절 종료 시각(PhotonNetwork.Time 기준). 보낸 쪽이 계산해 RPC로 실어 보내므로 모든 클라이언트가 같은 순간에 풀린다.
    private double stunEndTime;

    // 애니메이터에게 피격·기절을 알리는 이벤트
    public event Action OnHitEvent;
    public event Action OnStunnedEvent;
    // hp가 0이 된 순간을 알린다(인자 = 공격자 ActorNumber). 발화는 이 부품만, 밖에서는 듣기만.
    public event Action<int> OnDied;

    public void Bind(PlayerController _core) => core = _core;

    private void OnEnable()
    {
        curHp = maxHp;
    }

    public void OnRespawn()
    {
        curHp = maxHp;
        GameEvents.HpChanged(curHp);
        stunEndTime = 0;
    }

    public void OnRoundEndReset()
    {
        // Core가 상태를 NotReady로 덮어 만료 검사는 걸리지 않지만, 종료 시각도 비워 다음 라운드로 넘기지 않는다
        stunEndTime = 0;
    }

    public void OnReceiveImpact(ImpactData _data)
    {
        // 상태 검사
        if (
            curHp <= 0
            || core.GetPlayerState == PlayerController.PlayerState.NotReady
            || core.GetPlayerState == PlayerController.PlayerState.Dead
            || core.GetPlayerState == PlayerController.PlayerState.Rolling
            || _data.attackerTeam == core.MyTeam
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
        }
    }

    [PunRPC]
    private void TakeDamage(float _damage, Vector3 _hitNormal, int _attackerNum)
    {
        // 보내는 쪽 검사(OnReceiveImpact)를 거의 동시에 통과한 두 공격이 모두 도착할 수 있다 — 이미 죽었으면 받는 쪽에서도 무시
        if (curHp <= 0) return;

        curHp -= _damage;
        OnHitEvent?.Invoke();

        if (photonView.IsMine)
        {
            // HUD 체력 갱신은 임펄스 소스 참조와 무관하게 항상
            GameEvents.HpChanged(curHp);

            // 피격 카메라 쉐이크
            if (damageImpulseSource != null)
            {
                Vector3 shakeDir = -_hitNormal;
                shakeDir.y = 0f;
                if (shakeDir.sqrMagnitude < 0.01f) shakeDir = Vector3.back;
                damageImpulseSource.GenerateImpulseWithVelocity(shakeDir.normalized * damageShakeForce);
            }
        }

        Vector3 bloodPos = new Vector3(transform.position.x, -1.35f, transform.position.z);
        Instantiate(neonBloodPrefab, bloodPos, Quaternion.identity);

        if (curHp <= 0)
        {
            OnDied?.Invoke(_attackerNum);
        }
    }

    private void StunPlayer()
    {
        // 보내는 쪽이 종료 시각을 계산해 실어 보낸다 — 도착 시차와 무관하게 모두 같은 순간에 풀린다
        photonView.RPC(nameof(StunRPC), RpcTarget.All, PhotonNetwork.Time + stunDuration);
    }

    [PunRPC]
    private void StunRPC(double _endTime)
    {
        if (!gameObject.activeSelf) return;
        // 늦게 도착한 기절이 시체나 라운드 대기 상태를 Stunned로 덮지 않게 — 보내는 쪽 검사를 받는 쪽에서도 반복
        if (core.GetPlayerState == PlayerController.PlayerState.Dead
            || core.GetPlayerState == PlayerController.PlayerState.NotReady) return;
        stunEndTime = _endTime;
        core.Stun();
        OnStunnedEvent?.Invoke();
    }

    private void Update()
    {
        // 기절 상태일 때만 만료를 검사한다 — 여기의 Stunned 검사는 매 프레임 호출을 막는 용도이고,
        // 전이 규칙("Stunned에서만 Idle로" — 기절 중 사망하면 Dead를 덮지 않는다) 자체는 Core.EndStun이 가진다.
        if (core.GetPlayerState == PlayerController.PlayerState.Stunned && PhotonNetwork.Time >= stunEndTime)
        {
            core.EndStun();
        }
    }
}
